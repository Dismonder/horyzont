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
    private static readonly PlayerId Alice = new(1);
    private static readonly PlayerId Bob = new(2);
    private static readonly PlayerId Host = new(3);
    private static readonly PlanetId Planet = WorldFactory.HomePlanet;
    private static readonly Vector3 Spot = new(4000f, 0f, 0f);

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
        _host.Local.Add(Host);
    }

    [Fact]
    public void Startup_ColdCache_SendsSortedManifestThenDataThenNothingElse()
    {
        Edit(Spot, 2f);
        var edited = _sim.State.TerrainVersions.All.Select(v => v.Key).ToList();
        _replicator.Handle(Alice, View(1, Spot));
        _replicator.OnPlayerReady(Alice);

        Pump();

        var sync = _host.Messages<TerrainSync>(Alice);
        Assert.All(sync, s => Assert.Equal((byte)0, s.Purpose));
        Assert.True(sync[^1].IsLastPage);
        Assert.Empty(sync[^1].Entries);
        var entries = sync.SelectMany(s => s.Entries).ToList();
        Assert.Equal(edited, entries.Select(e => e.Key).ToList());
        Assert.All(entries, e => Assert.Equal(TerrainBlockHash.Compute(_source.Blob(e.Key)), e.Hash));
        var data = _host.Messages<TerrainBlockData>(Alice);
        Assert.Equal(edited.Count, data.Count);
        // Manifest first: every data message follows the last manifest page.
        Assert.True(_host.IndexOf(Alice, sync[^1]) < _host.FirstIndexOf<TerrainBlockData>(Alice));
        Assert.Equal(Enumerable.Range(0, sync.Count).Select(i => (ushort)i), sync.Select(s => s.PageIndex));
    }

    [Fact]
    public void Startup_NoEditsAndNoReport_StillSendsTheEmptyFinalPage()
    {
        _replicator.OnPlayerReady(Alice);

        Pump();

        var only = Assert.Single(_host.Messages<TerrainSync>(Alice));
        Assert.True(only.IsLastPage);
        Assert.Equal((ushort)0, only.PageIndex);
        Assert.Empty(_host.Messages<TerrainBlockData>(Alice));
    }

    [Fact]
    public void Startup_ReportedVersions_SendDataOnlyWhereTheVersionDiffers()
    {
        Edit(Spot, 2f);
        var keys = _sim.State.TerrainVersions.All.ToList();
        var same = keys[0];
        var ahead = keys[1];
        var behind = keys[2];
        _replicator.Handle(Alice, new TerrainCacheReport(Planet, [same, ahead with { Version = ahead.Version + 5 }, behind with { Version = 0 }]));
        _replicator.OnPlayerReady(Alice);

        Pump();

        var sent = _host.Messages<TerrainBlockData>(Alice).Select(d => d.Key).ToHashSet();
        Assert.DoesNotContain(same.Key, sent);
        Assert.Contains(ahead.Key, sent);
        var manifest = _host.Messages<TerrainSync>(Alice).SelectMany(s => s.Entries).ToList();
        Assert.Contains(manifest, e => e.Key == same.Key && e.Version == same.Version);
        // The key without a reported version is found by the viewer only when there is one; here none was sent.
        Assert.DoesNotContain(manifest, e => e.Key == behind.Key);
    }

    [Fact]
    public void Startup_ReportedKeyThatServerNeverEdited_ReplacesItWithVersionZeroData()
    {
        var phantom = new TerrainBlockKey(Planet, 0, 7, 7, 7);
        _replicator.Handle(Alice, new TerrainCacheReport(Planet, [new TerrainBlockVersion(phantom, 9)]));
        _replicator.OnPlayerReady(Alice);

        Pump();

        var entry = Assert.Single(_host.Messages<TerrainSync>(Alice).SelectMany(s => s.Entries));
        Assert.Equal(new TerrainSyncEntry(phantom, 0, TerrainBlockHash.Compute(_source.Blob(phantom))), entry);
        var data = Assert.Single(_host.Messages<TerrainBlockData>(Alice));
        Assert.Equal(0u, data.Version);
    }

    [Fact]
    public void Startup_ReportBeyondTheLimit_KeepsOnlyTheLimit()
    {
        for (var batch = 0; batch < 17; batch++)
        {
            var entries = Enumerable.Range(0, 4096)
                .Select(i => new TerrainBlockVersion(new TerrainBlockKey(Planet, 0, batch, i % 64, i / 64), 1)).ToList();
            _replicator.Handle(Alice, new TerrainCacheReport(Planet, entries));
        }

        Assert.Equal(TerrainReplicationPolicy.MaxReportKeysPerPlanet, _replicator.ReportedKeys(Alice, Planet));
    }

    [Fact]
    public void Live_EditInView_ReachesTheViewerAndNotTheOneWhoIsFarAway()
    {
        Ready(Alice, Spot);
        Ready(Bob, new Vector3(0f, 4000f, 0f));
        _host.Clear();

        Edit(Spot, 1f);
        Pump();

        Assert.NotEmpty(_host.Messages<TerrainBlockData>(Alice));
        Assert.Empty(_host.Messages<TerrainBlockData>(Bob));
        Assert.Empty(_host.Messages<TerrainSync>(Bob));
    }

    [Fact]
    public void Live_LocalPlayerOfAHost_GetsNothingAndStoresNothing()
    {
        Assert.True(_replicator.Handle(Host, new TerrainCacheReport(Planet, [])));
        Assert.True(_replicator.Handle(Host, View(1, Spot)));
        _replicator.OnPlayerReady(Host);
        Edit(Spot, 1f);

        Pump();

        Assert.Empty(_host.All(Host));
        Assert.Equal(0, _replicator.ReadyPlayers);
    }

    [Fact]
    public void Live_AuthorOfAnEdit_GetsTheDataThenTheBarrierWithFinalPairs()
    {
        Ready(Alice, Spot);
        _host.Clear();

        var applied = Edit(Spot, 1f, Alice);
        Pump();

        var data = _host.Messages<TerrainBlockData>(Alice);
        var sync = _host.Messages<TerrainSync>(Alice);
        Assert.Equal(applied.TerrainVersions.Count, data.Count);
        Assert.All(sync, s => Assert.Equal((byte)2, s.Purpose));
        Assert.All(sync, s => Assert.Equal(applied.Id, s.BarrierOpId));
        var entries = sync.SelectMany(s => s.Entries).ToList();
        Assert.Equal(applied.TerrainVersions.Select(v => v.Key), entries.Select(e => e.Key));
        Assert.Equal(applied.TerrainVersions.Select(v => v.Version), entries.Select(e => e.Version));
        Assert.True(sync[^1].IsLastPage);
        Assert.True(_host.IndexOf(Alice, data[^1]) < _host.IndexOf(Alice, sync[0]));
    }

    [Fact]
    public void Queue_RepeatedEditsOfOneBlock_SendTheLatestVersionAndCloseEveryBarrier()
    {
        Ready(Alice, Spot);
        _host.Clear();

        var first = Edit(Spot, 1f, Alice);
        var second = Edit(Spot, 1f, Alice);
        var third = Edit(Spot, 1f, Alice);
        Pump();

        var data = _host.Messages<TerrainBlockData>(Alice);
        Assert.Equal(first.TerrainVersions.Count, data.Count);
        Assert.All(data, d => Assert.Equal(3u, d.Version));
        var barriers = _host.Messages<TerrainSync>(Alice).Where(s => s.IsLastPage).Select(s => s.BarrierOpId).ToList();
        Assert.Equal([first.Id, second.Id, third.Id], barriers);
        var ids = _host.Messages<TerrainSync>(Alice).Where(s => s.IsLastPage && s.Purpose == 2).Select(s => s.TransferId).ToList();
        Assert.Equal(ids.Order().ToList(), ids);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public void Queue_DistantFlood_StaysInsideTheSharedByteBudget()
    {
        _source.Pad = 1000;
        Ready(Bob, Spot);
        _host.Clear();

        for (var i = 0; i < 50; i++)
        {
            Edit(new Vector3(4000f + (i * 20f), 0f, 0f), 2f);
        }

        Pump();

        // One bucket of 65 536 B and the live debt of the blocks next to the viewer; the rest waits for the bucket to refill.
        var bytes = _replicator.BlockBytesSent + _replicator.SyncBytesSent;
        Assert.InRange(bytes, 1, 65536 + 60000);
        var waiting = _replicator.QueuedBlocks(Bob);
        Assert.True(waiting > 0);
        _time.Advance(TimeSpan.FromSeconds(30));
        Pump();
        Assert.True(_replicator.QueuedBlocks(Bob) < waiting);
    }

    [Fact]
    public void Interest_ViewerMovesToEditedGround_SendsOnlyBlocksTheClientLacks()
    {
        Ready(Alice, new Vector3(0f, 4000f, 0f));
        Edit(Spot, 2f);
        _host.Clear();

        Assert.True(_replicator.Handle(Alice, View(2, Spot)));
        Pump();

        var sync = _host.Messages<TerrainSync>(Alice);
        Assert.All(sync, s => Assert.Equal((byte)1, s.Purpose));
        Assert.True(sync[^1].IsLastPage);
        Assert.NotEmpty(_host.Messages<TerrainBlockData>(Alice));

        _host.Clear();
        _replicator.Handle(Alice, View(3, Spot + new Vector3(100f, 0f, 0f)));
        Pump();

        // Everything in range is already known: no transfer, no data.
        Assert.Empty(_host.All(Alice));
    }

    [Fact]
    public void View_FarFromTheCharacterOrOutOfSequence_IsIgnored()
    {
        _host.Feet[Alice] = (Planet, Spot);
        Ready(Alice, Spot);
        Edit(new Vector3(0f, 4000f, 0f), 2f);
        _host.Clear();

        _replicator.Handle(Alice, View(5, new Vector3(0f, 4000f, 0f)));
        Pump();
        Assert.Empty(_host.All(Alice));

        _replicator.Handle(Alice, View(2, Spot));
        _replicator.Handle(Alice, View(2, new Vector3(0f, 4000f, 0f)));
        Pump();
        Assert.Empty(_host.All(Alice));
    }

    [Fact]
    public void View_VisualViewerFarFromTheCharacter_NeedsTheFreeCameraPermission()
    {
        _host.Feet[Alice] = (Planet, Spot);
        Ready(Alice, Spot);
        Edit(new Vector3(0f, 4000f, 0f), 2f);
        _host.Clear();
        var far = new TerrainView(Planet, 9, new Vector3(0f, 4000f, 0f), 2048f, Spot, 64f);

        _replicator.Handle(Alice, far);
        Pump();
        Assert.Empty(_host.All(Alice));

        _host.FreeCamera = true;
        _replicator.Handle(Alice, far with { ViewSeq = 10 });
        Pump();
        Assert.NotEmpty(_host.Messages<TerrainBlockData>(Alice));
    }

    [Fact]
    public void Check_GeneratorMismatch_RepairsWithVersionZeroAndCountsOnce()
    {
        Ready(Alice, Spot);
        var key = new TerrainBlockKey(Planet, 0, 250, 0, 0);
        _host.Clear();

        _replicator.Handle(Alice, new TerrainChecksums(Planet, [new TerrainBlockChecksum(key, 12345)]));
        Pump();

        Assert.Equal(1, _replicator.GeneratorMismatches);
        var data = Assert.Single(_host.Messages<TerrainBlockData>(Alice));
        Assert.Equal((key, 0u), (data.Key, data.Version));

        _time.Advance(TimeSpan.FromSeconds(5));
        _replicator.Handle(Alice, new TerrainChecksums(Planet, [new TerrainBlockChecksum(key, Source.Digest(key))]));
        Pump();
        Assert.Equal(1, _replicator.GeneratorMismatches);
        Assert.Single(_host.Messages<TerrainBlockData>(Alice));
    }

    [Fact]
    public void Check_ServerEditedBlock_DoesNotReportAGeneratorMismatch()
    {
        Ready(Alice, Spot);
        Edit(Spot, 1f);
        Pump();
        var edited = _sim.State.TerrainVersions.All.First();
        _host.Clear();

        _time.Advance(TimeSpan.FromSeconds(5));
        _replicator.Handle(Alice, new TerrainChecksums(Planet, [new TerrainBlockChecksum(edited.Key, 1)]));
        Pump();

        Assert.Equal(0, _replicator.GeneratorMismatches);
        Assert.Empty(_host.Messages<TerrainBlockData>(Alice));
    }

    [Fact]
    public void Check_TooFrequentOrTooFar_IsIgnored()
    {
        Ready(Alice, Spot);
        var near = new TerrainBlockKey(Planet, 0, 250, 0, 0);
        var far = new TerrainBlockKey(Planet, 0, 100, 0, 0);
        _host.Clear();

        _replicator.Handle(Alice, new TerrainChecksums(Planet, [new TerrainBlockChecksum(far, 1)]));
        Pump();
        Assert.Equal(0, _replicator.GeneratorMismatches);

        _time.Advance(TimeSpan.FromSeconds(5));
        _replicator.Handle(Alice, new TerrainChecksums(Planet, [new TerrainBlockChecksum(near, 1)]));
        _replicator.Handle(Alice, new TerrainChecksums(Planet, [new TerrainBlockChecksum(near with { X = 251 }, 1)]));
        Pump();
        Assert.Equal(1, _replicator.GeneratorMismatches);
    }

    [Fact]
    public void Resend_ClientReportsAKeyAsMissing_GetsTheCurrentDataAgain()
    {
        Edit(Spot, 1f);
        var edited = _sim.State.TerrainVersions.All.First();
        Ready(Alice, Spot);
        Pump();
        _host.Clear();

        _replicator.Handle(Alice, new TerrainCacheReport(Planet, [new TerrainBlockVersion(edited.Key, 0)]));
        Pump();

        var data = Assert.Single(_host.Messages<TerrainBlockData>(Alice));
        Assert.Equal((edited.Key, edited.Version), (data.Key, data.Version));
    }

    [Fact]
    public void Leave_ClearsThePlayersWork()
    {
        Ready(Alice, Spot);
        Edit(Spot, 1f);
        _replicator.OnPlayerLeaving(Alice, Horyzont.Core.Protocol.DisconnectCode.Normal);
        _host.Clear();

        Pump();

        Assert.Empty(_host.All(Alice));
        Assert.Equal(0, _replicator.QueuedBlocks(Alice));
    }

    [Fact]
    public void Step_IdleReadyPlayers_AllocatesNothing()
    {
        Ready(Alice, Spot);
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

    private static TerrainView View(uint seq, Vector3 at) => new(Planet, seq, at, 2048f, at, 128f);

    private AppliedOperation Edit(Vector3 center, float radius, PlayerId? actor = null)
    {
        var applied = _sim.ApplyServerOperation(new TerrainEditOp(Planet, TerrainShape.Sphere, center, radius, TerrainEditMode.Remove, 0));
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

        public Dictionary<PlayerId, (PlanetId Planet, Vector3 Feet)> Feet { get; } = [];

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
