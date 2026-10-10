using System.IO.Hashing;
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Server.Replication;
using Horyzont.Server.Terrain;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests.Terrain;

public sealed class ServerTerrainReplicatorTests
{
    private static readonly PlayerId _alice = new(1);
    private static readonly PlayerId _bob = new(2);
    private static readonly PlayerId _hostPlayer = new(3);
    private static readonly PlanetId _planet = WorldFactory.HomePlanet;
    private static readonly Vector3 _spot = new(4000f, 0f, 0f);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeTerrainService _terrain = new();
    private readonly ServerSimulation _sim;
    private readonly Rig _host = new();
    private readonly Source _source;
    private readonly ServerTerrainReplicator _replicator;

    public ServerTerrainReplicatorTests()
    {
        _sim = new ServerSimulation(WorldFactory.CreateDefault(), _terrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        _source = new Source(_sim, _terrain);
        _replicator = new ServerTerrainReplicator(
            _host, _source, _sim.State, new TokenBucketReplicationBudget(_time), _time, NullLogger<ServerTerrainReplicator>.Instance, "test-build");
        _sim.Applied += _replicator.OnOperationApplied;
        _host.Local.Add(_hostPlayer);
    }

    [Fact]
    public void Startup_ColdCache_SendsSortedManifestThenDataThenNothingElse()
    {
        Edit(_spot, 2f);
        var edited = _sim.State.TerrainVersions.All.Select(v => v.Key).ToList();
        _replicator.Handle(_alice, View(1, _spot));
        _replicator.OnPlayerReady(_alice);

        Pump();

        var sync = _host.Messages<TerrainSync>(_alice);
        Assert.All(sync, s => Assert.Equal((byte)0, s.Purpose));
        Assert.True(sync[^1].IsLastPage);
        Assert.Empty(sync[^1].Entries);
        var entries = sync.SelectMany(s => s.Entries).ToList();
        Assert.Equal(edited, entries.Select(e => e.Key).ToList());
        Assert.All(entries, e => Assert.Equal(TerrainBlockHash.Compute(_source.Blob(e.Key)), e.Hash));
        var data = _host.Messages<TerrainBlockData>(_alice);
        Assert.Equal(edited.Count, data.Count);
        // Manifest first: every data message follows the last manifest page.
        Assert.True(_host.IndexOf(_alice, sync[^1]) < _host.FirstIndexOf<TerrainBlockData>(_alice));
        Assert.Equal(Enumerable.Range(0, sync.Count).Select(i => (ushort)i), sync.Select(s => s.PageIndex));
    }

    [Fact]
    public void Startup_NoEditsAndNoReport_StillSendsTheEmptyFinalPage()
    {
        _replicator.OnPlayerReady(_alice);

        Pump();

        var only = Assert.Single(_host.Messages<TerrainSync>(_alice));
        Assert.True(only.IsLastPage);
        Assert.Equal((ushort)0, only.PageIndex);
        Assert.Empty(_host.Messages<TerrainBlockData>(_alice));
    }

    [Fact]
    public void Startup_ReportedVersions_SendDataOnlyWhereTheVersionDiffers()
    {
        Edit(_spot, 2f);
        var keys = _sim.State.TerrainVersions.All.ToList();
        var same = keys[0];
        var ahead = keys[1];
        var behind = keys[2];
        _replicator.Handle(_alice, new TerrainCacheReport(_planet, [same, ahead with { Version = ahead.Version + 5 }, behind with { Version = 0 }]));
        _replicator.OnPlayerReady(_alice);

        Pump();

        var sent = _host.Messages<TerrainBlockData>(_alice).Select(d => d.Key).ToHashSet();
        Assert.DoesNotContain(same.Key, sent);
        Assert.Contains(ahead.Key, sent);
        var manifest = _host.Messages<TerrainSync>(_alice).SelectMany(s => s.Entries).ToList();
        Assert.Contains(manifest, e => e.Key == same.Key && e.Version == same.Version);
        // The key without a reported version is found by the viewer only when there is one; here none was sent.
        Assert.DoesNotContain(manifest, e => e.Key == behind.Key);
    }

    [Fact]
    public void Startup_ReportedKeyThatServerNeverEdited_ReplacesItWithVersionZeroData()
    {
        var phantom = new TerrainBlockKey(_planet, 0, 7, 7, 7);
        _replicator.Handle(_alice, new TerrainCacheReport(_planet, [new TerrainBlockVersion(phantom, 9)]));
        _replicator.OnPlayerReady(_alice);

        Pump();

        var entry = Assert.Single(_host.Messages<TerrainSync>(_alice).SelectMany(s => s.Entries));
        Assert.Equal(new TerrainSyncEntry(phantom, 0, TerrainBlockHash.Compute(_source.Blob(phantom))), entry);
        var data = Assert.Single(_host.Messages<TerrainBlockData>(_alice));
        Assert.Equal(0u, data.Version);
    }

    [Fact]
    public void Startup_ReportBeyondTheLimit_KeepsOnlyTheLimit()
    {
        for (var batch = 0; batch < 17; batch++)
        {
            var entries = Enumerable.Range(0, 4096)
                .Select(i => new TerrainBlockVersion(new TerrainBlockKey(_planet, 0, batch, i % 64, i / 64), 1)).ToList();
            _replicator.Handle(_alice, new TerrainCacheReport(_planet, entries));
        }

        Assert.Equal(TerrainReplicationPolicy.MaxReportKeysPerPlanet, _replicator.ReportedKeys(_alice, _planet));
    }

    [Fact]
    public void Live_EditInView_ReachesTheViewerAndNotTheOneWhoIsFarAway()
    {
        Ready(_alice, _spot);
        Ready(_bob, new Vector3(0f, 4000f, 0f));
        _host.Clear();

        Edit(_spot, 1f);
        Pump();

        Assert.NotEmpty(_host.Messages<TerrainBlockData>(_alice));
        Assert.Empty(_host.Messages<TerrainBlockData>(_bob));
        Assert.Empty(_host.Messages<TerrainSync>(_bob));
    }

    [Fact]
    public void Live_LocalPlayerOfAHost_GetsNothingAndStoresNothing()
    {
        Assert.True(_replicator.Handle(_hostPlayer, new TerrainCacheReport(_planet, [])));
        Assert.True(_replicator.Handle(_hostPlayer, View(1, _spot)));
        _replicator.OnPlayerReady(_hostPlayer);
        Edit(_spot, 1f);

        Pump();

        Assert.Empty(_host.All(_hostPlayer));
        Assert.Equal(0, _replicator.ReadyPlayers);
    }

    [Fact]
    public void Live_AuthorOfAnEdit_GetsTheDataThenTheBarrierWithFinalPairs()
    {
        Ready(_alice, _spot);
        _host.Clear();

        var applied = Edit(_spot, 1f, _alice);
        Pump();

        var data = _host.Messages<TerrainBlockData>(_alice);
        var sync = _host.Messages<TerrainSync>(_alice);
        Assert.Equal(applied.TerrainVersions.Count, data.Count);
        Assert.All(sync, s => Assert.Equal((byte)2, s.Purpose));
        Assert.All(sync, s => Assert.Equal(applied.Id, s.BarrierOpId));
        var entries = sync.SelectMany(s => s.Entries).ToList();
        Assert.Equal(applied.TerrainVersions.Select(v => v.Key), entries.Select(e => e.Key));
        Assert.Equal(applied.TerrainVersions.Select(v => v.Version), entries.Select(e => e.Version));
        Assert.True(sync[^1].IsLastPage);
        Assert.True(_host.IndexOf(_alice, data[^1]) < _host.IndexOf(_alice, sync[0]));
    }

    [Fact]
    public void Live_EditWhileALargeStartupManifestIsBuilding_ReachesTheAuthorAndClosesTheBarrierFirst()
    {
        var phantoms = Enumerable.Range(0, 2000)
            .Select(i => new TerrainBlockVersion(new TerrainBlockKey(_planet, 0, 100 + (i % 50), 100 + (i / 50), 100), 1)).ToList();
        for (var offset = 0; offset < phantoms.Count; offset += 1000)
        {
            _replicator.Handle(_alice, new TerrainCacheReport(_planet, phantoms.GetRange(offset, 1000)));
        }

        // The block is edited before the startup: the manifest names it at version 1 while the live edit below makes version 2.
        var before = Edit(_spot, 1f, _alice);
        _replicator.Handle(_alice, View(1, _spot));
        _replicator.OnPlayerReady(_alice);
        _replicator.Step();
        _host.Clear();

        var applied = Edit(_spot, 1f, _alice);
        _replicator.Step();
        _replicator.Step();

        var data = _host.Messages<TerrainBlockData>(_alice);
        Assert.Equal(applied.TerrainVersions.Count, data.Count);
        Assert.All(data, d => Assert.Equal(2u, d.Version));
        Assert.Equal(before.TerrainVersions.Select(v => v.Key).Order(), data.Select(d => d.Key).Order());
        var sync = _host.Messages<TerrainSync>(_alice);
        Assert.Contains(sync, s => s.Purpose == 2 && s.IsLastPage && s.BarrierOpId == applied.Id);
        Assert.DoesNotContain(sync, s => s.Purpose == 0 && s.IsLastPage);
    }

    [Fact]
    public void Queue_RepeatedEditsOfOneBlock_SendTheLatestVersionAndCloseEveryBarrier()
    {
        Ready(_alice, _spot);
        _host.Clear();

        var first = Edit(_spot, 1f, _alice);
        var second = Edit(_spot, 1f, _alice);
        var third = Edit(_spot, 1f, _alice);
        Pump();

        var data = _host.Messages<TerrainBlockData>(_alice);
        Assert.Equal(first.TerrainVersions.Count, data.Count);
        Assert.All(data, d => Assert.Equal(3u, d.Version));
        var barriers = _host.Messages<TerrainSync>(_alice).Where(s => s.IsLastPage).Select(s => s.BarrierOpId).ToList();
        Assert.Equal([first.Id, second.Id, third.Id], barriers);
        var ids = _host.Messages<TerrainSync>(_alice).Where(s => s.IsLastPage && s.Purpose == 2).Select(s => s.TransferId).ToList();
        Assert.Equal(ids.Order().ToList(), ids);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public void Queue_DistantFlood_StaysInsideTheSharedByteBudget()
    {
        _source.Pad = 1000;
        Ready(_bob, _spot);
        _host.Clear();

        for (var i = 0; i < 50; i++)
        {
            Edit(new Vector3(4000f + (i * 20f), 0f, 0f), 2f);
        }

        Pump();

        // One bucket of 65 536 B and the live debt of the blocks next to the viewer; the rest waits for the bucket to refill.
        var bytes = _replicator.BlockBytesSent + _replicator.SyncBytesSent;
        Assert.InRange(bytes, 1, 65536 + 60000);
        var waiting = _replicator.QueuedBlocks(_bob);
        Assert.True(waiting > 0);
        _time.Advance(TimeSpan.FromSeconds(30));
        Pump();
        Assert.True(_replicator.QueuedBlocks(_bob) < waiting);
    }

    [Fact]
    public void Interest_ViewerMovesToEditedGround_SendsOnlyBlocksTheClientLacks()
    {
        Ready(_alice, new Vector3(0f, 4000f, 0f));
        Edit(_spot, 2f);
        _host.Clear();

        Assert.True(_replicator.Handle(_alice, View(2, _spot)));
        Pump();

        var sync = _host.Messages<TerrainSync>(_alice);
        Assert.All(sync, s => Assert.Equal((byte)1, s.Purpose));
        Assert.True(sync[^1].IsLastPage);
        Assert.NotEmpty(_host.Messages<TerrainBlockData>(_alice));

        _host.Clear();
        _replicator.Handle(_alice, View(3, _spot + new Vector3(100f, 0f, 0f)));
        Pump();

        // Everything in range is already known: no transfer, no data.
        Assert.Empty(_host.All(_alice));
    }

    [Fact]
    public void View_FarFromTheCharacterOrOutOfSequence_IsIgnored()
    {
        _host.Feet[_alice] = (_planet, _spot);
        Ready(_alice, _spot);
        Edit(new Vector3(0f, 4000f, 0f), 2f);
        _host.Clear();

        _replicator.Handle(_alice, View(5, new Vector3(0f, 4000f, 0f)));
        Pump();
        Assert.Empty(_host.All(_alice));

        _replicator.Handle(_alice, View(2, _spot));
        _replicator.Handle(_alice, View(2, new Vector3(0f, 4000f, 0f)));
        Pump();
        Assert.Empty(_host.All(_alice));
    }

    [Fact]
    public void View_VisualViewerFarFromTheCharacter_NeedsTheFreeCameraPermission()
    {
        _host.Feet[_alice] = (_planet, _spot);
        Ready(_alice, _spot);
        Edit(new Vector3(0f, 4000f, 0f), 2f);
        _host.Clear();
        var far = new TerrainView(_planet, 9, new Vector3(0f, 4000f, 0f), 2048f, _spot, 64f);

        _replicator.Handle(_alice, far);
        Pump();
        Assert.Empty(_host.All(_alice));

        _host.FreeCamera = true;
        _replicator.Handle(_alice, far with { ViewSeq = 10 });
        Pump();
        Assert.NotEmpty(_host.Messages<TerrainBlockData>(_alice));
    }

    [Fact]
    public void Check_GeneratorMismatch_RepairsWithVersionZeroAndCountsOnce()
    {
        Ready(_alice, _spot);
        var key = new TerrainBlockKey(_planet, 0, 250, 0, 0);
        _host.Clear();

        _replicator.Handle(_alice, new TerrainChecksums(_planet, [new TerrainBlockChecksum(key, 12345)]));
        Pump();

        Assert.Equal(1, _replicator.GeneratorMismatches);
        var data = Assert.Single(_host.Messages<TerrainBlockData>(_alice));
        Assert.Equal((key, 0u), (data.Key, data.Version));

        _time.Advance(TimeSpan.FromSeconds(5));
        _replicator.Handle(_alice, new TerrainChecksums(_planet, [new TerrainBlockChecksum(key, Source.Digest(key))]));
        Pump();
        Assert.Equal(1, _replicator.GeneratorMismatches);
        Assert.Single(_host.Messages<TerrainBlockData>(_alice));
    }

    [Fact]
    public void Check_ServerEditedBlock_DoesNotReportAGeneratorMismatch()
    {
        Ready(_alice, _spot);
        Edit(_spot, 1f);
        Pump();
        var edited = _sim.State.TerrainVersions.All.First();
        _host.Clear();

        _time.Advance(TimeSpan.FromSeconds(5));
        _replicator.Handle(_alice, new TerrainChecksums(_planet, [new TerrainBlockChecksum(edited.Key, 1)]));
        Pump();

        Assert.Equal(0, _replicator.GeneratorMismatches);
        Assert.Empty(_host.Messages<TerrainBlockData>(_alice));
    }

    [Fact]
    public void Check_TooFrequentOrTooFar_IsIgnored()
    {
        Ready(_alice, _spot);
        var near = new TerrainBlockKey(_planet, 0, 250, 0, 0);
        var far = new TerrainBlockKey(_planet, 0, 100, 0, 0);
        _host.Clear();

        _replicator.Handle(_alice, new TerrainChecksums(_planet, [new TerrainBlockChecksum(far, 1)]));
        Pump();
        Assert.Equal(0, _replicator.GeneratorMismatches);

        _time.Advance(TimeSpan.FromSeconds(5));
        _replicator.Handle(_alice, new TerrainChecksums(_planet, [new TerrainBlockChecksum(near, 1)]));
        _replicator.Handle(_alice, new TerrainChecksums(_planet, [new TerrainBlockChecksum(near with { X = 251 }, 1)]));
        Pump();
        Assert.Equal(1, _replicator.GeneratorMismatches);
    }

    [Fact]
    public void Resend_ClientReportsAKeyAsMissing_GetsTheCurrentDataAgain()
    {
        Edit(_spot, 1f);
        var edited = _sim.State.TerrainVersions.All.First();
        Ready(_alice, _spot);
        Pump();
        _host.Clear();

        _replicator.Handle(_alice, new TerrainCacheReport(_planet, [new TerrainBlockVersion(edited.Key, 0)]));
        Pump();

        var data = Assert.Single(_host.Messages<TerrainBlockData>(_alice));
        Assert.Equal((edited.Key, edited.Version), (data.Key, data.Version));
    }

    [Fact]
    public void Leave_ClearsThePlayersWork()
    {
        Ready(_alice, _spot);
        Edit(_spot, 1f);
        _replicator.OnPlayerLeaving(_alice, Horyzont.Core.Protocol.DisconnectCode.Normal);
        _host.Clear();

        Pump();

        Assert.Empty(_host.All(_alice));
        Assert.Equal(0, _replicator.QueuedBlocks(_alice));
    }

    [Fact]
    public void Step_IdleReadyPlayers_AllocatesNothing()
    {
        Ready(_alice, _spot);
        Pump();
        _host.Clear();
        for (var i = 0; i < 100; i++)
        {
            _replicator.Step();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            _replicator.Step();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static TerrainView View(uint seq, Vector3 at) => new(_planet, seq, at, 2048f, at, 128f);

    private AppliedOperation Edit(Vector3 center, float radius, PlayerId? actor = null)
    {
        var applied = _sim.ApplyServerOperation(new TerrainEditOp(_planet, TerrainShape.Sphere, center, radius, TerrainEditMode.Remove, 0));
        if (actor is null)
        {
            return applied;
        }

        // The simulation applied it without an author; tell the replicator who made it, as an intent would.
        var authored = applied with { Actor = actor };
        _replicator.OnOperationApplied(authored);
        return authored;
    }

    private void Ready(PlayerId player, Vector3 at)
    {
        _replicator.Handle(player, View(1, at));
        _replicator.OnPlayerReady(player);
        Pump();
    }

    private void Pump()
    {
        for (var i = 0; i < 8; i++)
        {
            _replicator.Step();
        }
    }

    private sealed class Rig : ITerrainReplicationHost
    {
        private readonly List<(PlayerId Player, Message Message)> _sent = [];

        public HashSet<PlayerId> Local { get; } = [];

        public Dictionary<PlayerId, (PlanetId _planet, Vector3 Feet)> Feet { get; } = [];

        public bool FreeCamera { get; set; }

        public bool FreeCameraAllowed => FreeCamera;

        public bool IsLocalPlayer(PlayerId player) => Local.Contains(player);

        public bool TryGetFeet(PlayerId player, out PlanetId planet, out Vector3 feet)
        {
            if (Feet.TryGetValue(player, out var found))
            {
                (planet, feet) = found;
                return true;
            }

            planet = default;
            feet = default;
            return false;
        }

        public void Send(PlayerId player, Message message) => _sent.Add((player, message));

        public void Clear() => _sent.Clear();

        public List<T> Messages<T>(PlayerId player)
            where T : Message => _sent.Where(s => s.Player == player && s.Message is T).Select(s => (T)s.Message).ToList();

        public List<Message> All(PlayerId player) => _sent.Where(s => s.Player == player).Select(s => s.Message).ToList();

        public int IndexOf(PlayerId player, Message message) => _sent.FindIndex(s => s.Player == player && ReferenceEquals(s.Message, message));

        public int FirstIndexOf<T>(PlayerId player)
            where T : Message => _sent.FindIndex(s => s.Player == player && s.Message is T);
    }

    private sealed class Source(ServerSimulation sim, FakeTerrainService terrain) : ITerrainReplicationSource
    {
        public int Pad { get; set; }

        public byte[] Blob(TerrainBlockKey key)
        {
            var data = sim.State.TerrainVersions.Get(key) > 0 ? terrain.GetBlockData(key) : Generated(key);
            return Pad == 0 ? data : [.. data, .. new byte[Pad]];
        }

        public static ulong Digest(TerrainBlockKey key) => XxHash64.HashToUInt64(Generated(key));

        public bool TryExportBlock(TerrainBlockKey key, out uint version, out byte[] blob)
        {
            version = sim.State.TerrainVersions.Get(key);
            blob = Blob(key);
            return true;
        }

        public bool TryGetGeneratedDigest(TerrainBlockKey key, out ulong digest)
        {
            digest = Digest(key);
            return true;
        }

        private static byte[] Generated(TerrainBlockKey key) =>
            [(byte)key.Lod, (byte)key.X, (byte)(key.X >> 8), (byte)key.Y, (byte)key.Z, 7];
    }
}
