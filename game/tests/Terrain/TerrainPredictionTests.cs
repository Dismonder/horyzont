using GdUnit4;

using Godot;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Client;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>
/// The prediction of a remote client over a real own terrain (K3-16 step 6): the overlay of an edit stays until its barrier, a rejection
/// or a lost session puts the authoritative base back, and nothing predicted is ever stored (these rigs have no cache at all).
/// </summary>
[TestSuite]
public sealed class TerrainPredictionTests
{
    private static readonly Vector3 _center = TerrainTestWorld.Ground(8, 0);

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Predict_AcceptedReplyBeforeBlocks_RemovesOverlayOnlyAfterBarrier()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        using var rig = await ClientRig.CreateAsync(scene, server);
        var keys = rig.KeysOf(_center, 2);
        var baseline = rig.Digest(keys);

        rig.Prediction.Begin(1, Intent(), Preview(_center, 2));
        AssertThat(rig.Prediction.Count).IsEqual(1);
        AssertThat(rig.Digest(keys)).IsNotEqual(baseline);

        var applied = server.Edit(_center, 2, TerrainEditMode.Remove);
        rig.Prediction.NoteReply(new IntentReply(1, RejectReason.None, applied.Id));
        AssertThat(rig.Prediction.Count).IsEqual(1);

        // The blocks arrive after the reply: they become the base, the overlay stays over them.
        rig.Deliver(applied);
        AssertThat(rig.Prediction.Count).IsEqual(1);

        rig.Prediction.OnBarrier(applied.Id, rig.Barrier(applied));
        AssertThat(rig.Prediction.Count).IsEqual(0);
        AssertThat(rig.Digest(keys)).IsEqual(ServerDigest(server, keys));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Predict_BarrierBeforeReply_StillEndsThePrediction()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        using var rig = await ClientRig.CreateAsync(scene, server);
        var keys = rig.KeysOf(_center, 2);

        rig.Prediction.Begin(1, Intent(), Preview(_center, 2));
        var applied = server.Edit(_center, 2, TerrainEditMode.Remove);
        rig.Deliver(applied);
        rig.Prediction.OnBarrier(applied.Id, rig.Barrier(applied));
        rig.Prediction.NoteReply(new IntentReply(1, RejectReason.None, applied.Id));

        AssertThat(rig.Prediction.Count).IsEqual(0);
        AssertThat(rig.Digest(keys)).IsEqual(ServerDigest(server, keys));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Predict_OverlappingEditsAndRejection_RebuildsFromAuthoritativeBase()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        using var rig = await ClientRig.CreateAsync(scene, server);
        using var reference = await ClientRig.CreateAsync(scene, server);
        var second = _center + new Vector3(1.5f, 0f, 0f);
        var keys = rig.KeysOf(_center, 4);

        rig.Prediction.Begin(1, Intent(), Preview(_center, 2));
        rig.Prediction.Begin(2, Intent(), Preview(second, 2));
        reference.Prediction.Begin(2, Intent(), Preview(second, 2));
        AssertThat(rig.Prediction.Count).IsEqual(2);

        // The server rejects the first edit: only the second one stays, over the base of the server.
        rig.Prediction.Revert(1);
        AssertThat(rig.Prediction.Count).IsEqual(1);
        AssertThat(rig.Digest(keys)).IsEqual(reference.Digest(keys));
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Predict_TimeoutOrDisconnect_RevertsWithoutSavingSpeculation()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        using var rig = await ClientRig.CreateAsync(scene, server);
        var keys = rig.KeysOf(_center, 4);
        var baseline = rig.Digest(keys);

        for (uint seq = 1; seq <= 4; seq++)
        {
            rig.Prediction.Begin(seq, Intent(), Preview(_center + new Vector3(seq * 0.5f, 0f, 0f), 1.5f));
        }

        AssertThat(rig.Prediction.Count).IsEqual(4);
        AssertThat(rig.Digest(keys)).IsNotEqual(baseline);

        // The session ends: every edit is taken back, newest first.
        rig.Prediction.RevertAll();
        AssertThat(rig.Prediction.Count).IsEqual(0);
        AssertThat(rig.Digest(keys)).IsEqual(baseline);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Predict_AcceptedEditWithoutBarrier_ExpiresAndAsksForTheBlocksAgain()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        using var rig = await ClientRig.CreateAsync(scene, server);
        var keys = rig.KeysOf(_center, 2);
        var baseline = rig.Digest(keys);

        rig.Prediction.Begin(1, Intent(), Preview(_center, 2));
        var applied = server.Edit(_center, 2, TerrainEditMode.Remove);
        rig.Prediction.NoteReply(new IntentReply(1, RejectReason.None, applied.Id));
        rig.Time.Advance(RemoteTerrainPrediction.BarrierTimeout + TimeSpan.FromSeconds(1));

        var asked = new List<TerrainBlockKey>();
        rig.Prediction.Expire([], batch => asked.AddRange(batch));
        AssertThat(rig.Prediction.Count).IsEqual(0);
        AssertThat(asked.Count).IsGreater(0);
        AssertThat(rig.Digest(keys)).IsEqual(baseline);
    }

    private static ulong ServerDigest(TerrainTestWorld server, IReadOnlyList<TerrainBlockKey> keys)
    {
        var entries = new List<(TerrainBlockKey, uint, ulong)>();
        using var buffer = new VoxelBuffer();
        foreach (var key in keys)
        {
            AssertThat(server.Service.TryCopyBlock(key, buffer)).IsTrue();
            entries.Add((key, 0, server.Codec.Digest(buffer)));
        }

        return TerrainDigest.Compute(entries);
    }

    private static TerrainToolIntent Intent() =>
        new(WorldFactory.HomePlanet, _center + new Vector3(0f, 6f, 0f), -Vector3.UnitY, 2f, TerrainEditMode.Remove, 0);

    private static ToolPreview Preview(Vector3 center, float radius) =>
        new(ToolPreviewKind.Sphere, center, radius, center, System.Numerics.Quaternion.Identity, default, default, 0, RejectReason.None);

    /// <summary>An own terrain of a remote client with a ready sync state and a prediction, over the same planet as the server.</summary>
    private sealed class ClientRig : IDisposable
    {
        private readonly OwnTerrainProvider _terrain;
        private readonly PlanetTerrain _node;
        private readonly ServerHello _hello;

        private ClientRig(OwnTerrainProvider terrain, PlanetTerrain node, RemoteTerrainPrediction prediction, ServerHello hello, FakeTimeProvider time)
        {
            _terrain = terrain;
            _node = node;
            Prediction = prediction;
            _hello = hello;
            Time = time;
        }

        public RemoteTerrainPrediction Prediction { get; }

        public FakeTimeProvider Time { get; }

        public static async Task<ClientRig> CreateAsync(EngineTestScene scene, TerrainTestWorld server)
        {
            var hello = new ServerHello(
                server.State.Id, server.State.CurrentTick, 60, new PlayerId(1), new EntityId(1), server.State.Planets.All, [],
                [.. server.State.Materials.Entries]);
            var terrain = new OwnTerrainProvider(scene.Root, PlanetTerrainOptions.Server, null, NullLogger.Instance);
            terrain.Prepare(hello);
            terrain.TryGetNode(WorldFactory.HomePlanet, out var node);
            node!.AddChild(new VoxelViewer
            {
                Position = TerrainVectors.ToGodot(_center),
                ViewDistance = 64,
                RequiresVisuals = false,
                RequiresCollisions = false,
            });
            var box = new Aabb(new Godot.Vector3(-16, TerrainTestWorld.Surface - 16, -32), new Godot.Vector3(64, 32, 64));
            if (!await scene.WaitFramesUntil(() => node.Tool.IsAreaEditable(box), 3000))
            {
                throw new InvalidOperationException("The client terrain did not load.");
            }

            var time = new FakeTimeProvider();
            var sync = new Horyzont.Client.Terrain.ClientTerrainSyncState([WorldFactory.HomePlanet]);
            sync.OnSync(new TerrainSync(1, 0, WorldFactory.HomePlanet, default, 0, true, []));
            var prediction = new RemoteTerrainPrediction(terrain, time, NullLogger<RemoteTerrainPrediction>.Instance);
            prediction.StartSession(sync, hello);
            return new ClientRig(terrain, node, prediction, hello, time)
            {
                ServerSource = key =>
                {
                    server.Blocks.TryLoad(key, out _, out var blob);
                    return blob;
                },
            };
        }

        public List<TerrainBlockKey> KeysOf(Vector3 center, float radius)
        {
            var keys = new List<TerrainBlockKey>();
            TerrainMath.AffectedBlocks(WorldFactory.HomePlanet, center, radius, 1f, TerrainRules.EditMarginVoxels, keys);
            return keys;
        }

        /// <summary>Digest of the blocks as the client terrain holds them now.</summary>
        public ulong Digest(IReadOnlyList<TerrainBlockKey> keys)
        {
            var entries = new List<(TerrainBlockKey, uint, ulong)>();
            using var buffer = new VoxelBuffer();
            foreach (var key in keys)
            {
                buffer.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
                _node.Tool.Copy(
                    new Vector3I(key.X * TerrainMath.BlockSize, key.Y * TerrainMath.BlockSize, key.Z * TerrainMath.BlockSize),
                    buffer, TerrainChannels.UsedChannelsMask);
                entries.Add((key, 0, _terrain.Codec.Digest(buffer)));
            }

            return TerrainDigest.Compute(entries);
        }

        /// <summary>Hands the final blocks of an operation to the prediction as the replicator does.</summary>
        public void Deliver(AppliedOperation applied)
        {
            foreach (var version in applied.TerrainVersions)
            {
                var server = ServerBlocks(version.Key);
                using var buffer = new VoxelBuffer();
                _terrain.Codec.Decode(server, buffer);
                AssertThat(Prediction.TryApplyAuthoritative(_node, version.Key, buffer)).IsTrue();
            }
        }

        public IReadOnlyList<TerrainSyncEntry> Barrier(AppliedOperation applied) =>
            [.. applied.TerrainVersions.OrderBy(v => v.Key).Select(v => new TerrainSyncEntry(v.Key, v.Version, TerrainBlockHash.Compute(ServerBlocks(v.Key))))];

        public void Dispose() => _terrain.Dispose();

        private byte[] ServerBlocks(TerrainBlockKey key) => ServerSource?.Invoke(key) ?? throw new InvalidOperationException("No server source.");

        public Func<TerrainBlockKey, byte[]>? ServerSource { get; set; }
    }
}
