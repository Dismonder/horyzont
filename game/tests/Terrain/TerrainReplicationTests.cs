using GdUnit4;

using Godot;

using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public sealed class TerrainReplicationTests
{
    // First engine acceptance slice: independent native terrains, real envelope decode and paste.
    // Mesh/collision timing is a separate CI measurement; copy equality alone cannot certify 200 ms.
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Replicate_TwoRemoteClients_TerrainDigestsMatchAfterPaste()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        await using var first = await TerrainTestWorld.CreateAsync(scene);
        await using var second = await TerrainTestWorld.CreateAsync(scene);
        var center = TerrainTestWorld.Ground(8, 0);
        foreach (var world in new[] { server, first, second })
        {
            world.AddViewer(center);
            AssertThat(await world.WaitLoaded(center, 8)).IsTrue();
        }

        server.Edit(center, 1, TerrainEditMode.Remove);
        foreach (var version in server.State.TerrainVersions.All)
        {
            server.Blocks.TryLoad(version.Key, out var v, out var blob);
            var data = new TerrainBlockData(version.Key, v, blob);
            ClientTerrainReplicator.PasteValidated(first.Node.Terrain, data, first.Codec);
            ClientTerrainReplicator.PasteValidated(second.Node.Terrain, data, second.Codec);
            using var expected = new VoxelBuffer();
            using var actual = new VoxelBuffer();
            AssertThat(server.Service.TryCopyBlock(version.Key, expected)).IsTrue();
            foreach (var client in new[] { first, second })
            {
                AssertThat(client.Service.TryCopyBlock(version.Key, actual)).IsTrue();
                AssertThat(client.Codec.Digest(actual)).IsEqual(server.Codec.Digest(expected));
            }
        }
    }

    // Delivery over the network is simulated: 50 ms one way plus a jitter of 0-20 ms (seeded), RTT 100 ms. The engine part (decode,
    // paste, collision) is real time. The render mesh is not measured here (the test scene has no visuals): that stage is NOT RUN here,
    // it belongs to the CI/manual measurement of the package (K3-16 step 5, X11).
    [TestCase(Timeout = 60000)]
    [TestCategory("Slow")]
    [RequireGodotRuntime]
    public async Task Replicate_TwoRemoteClients_TerrainDigestsMatchWithin200ms()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        await using var first = await TerrainTestWorld.CreateAsync(scene);
        await using var second = await TerrainTestWorld.CreateAsync(scene);
        var center = TerrainTestWorld.Ground(8, 0);
        foreach (var world in new[] { server, first, second })
        {
            world.AddViewer(center);
            AssertThat(await world.WaitLoaded(center, 8)).IsTrue();
        }

        var jitterMs = new Random(16).Next(0, 21);
        var delivery = TimeSpan.FromMilliseconds(50 + jitterMs);
        var above = center + new System.Numerics.Vector3(0f, 4f, 0f);
        AssertThat(first.Service.Raycast(WorldFactory.HomePlanet, above, -System.Numerics.Vector3.UnitY, 16f, out var before)).IsTrue();
        server.Edit(center, 1, TerrainEditMode.Remove);

        var started = Time.GetTicksUsec();
        foreach (var version in server.State.TerrainVersions.All)
        {
            server.Blocks.TryLoad(version.Key, out var v, out var blob);
            var data = new TerrainBlockData(version.Key, v, blob);
            ClientTerrainReplicator.PasteValidated(first.Node.Terrain, data, first.Codec);
            ClientTerrainReplicator.PasteValidated(second.Node.Terrain, data, second.Codec);
        }

        var changed = first.Service.Raycast(WorldFactory.HomePlanet, above, -System.Numerics.Vector3.UnitY, 16f, out var now) && now.Distance > before.Distance + 0.5f;
        var engineMs = (Time.GetTicksUsec() - started) / 1000.0;
        GD.Print($"Replicate_Within200ms delivery={delivery.TotalMilliseconds:F0} ms engine={engineMs:F1} ms (decode, paste, collision; mesh not measured)");
        AssertThat(changed).IsTrue();
        AssertThat(delivery.TotalMilliseconds + engineMs).IsLess(200.0);
        AssertThat(first.LiveDigest()).IsEqual(server.LiveDigest());
        AssertThat(second.LiveDigest()).IsEqual(server.LiveDigest());
    }

    [TestCase(Timeout = 600000)]
    [TestCategory("Slow")]
    [RequireGodotRuntime]
    public async Task Replicate_TwoClientsRandomEdits_FinalDigestsMatch()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene, new TerrainBufferOptions { EarlyCheckpointBlocks = 100000, MaxPendingBlocks = 100000 });
        await using var first = await TerrainTestWorld.CreateAsync(scene);
        await using var second = await TerrainTestWorld.CreateAsync(scene);
        var center = TerrainTestWorld.Ground(0, 0);
        foreach (var world in new[] { server, first, second })
        {
            world.AddViewer(center, 96);
            AssertThat(await world.WaitLoaded(center, 40)).IsTrue();
        }

        // 600 deterministic edits in virtual time; the clients get only the final blob of every block (the queue coalesces them).
        var random = new Random(1600);
        for (var i = 0; i < 600; i++)
        {
            var at = new System.Numerics.Vector3(
                (float)((random.NextDouble() * 40) - 20), TerrainTestWorld.Surface + (float)((random.NextDouble() * 4) - 2), (float)((random.NextDouble() * 40) - 20));
            server.Edit(at, 1f + (float)(random.NextDouble() * 2), random.Next(2) == 0 ? TerrainEditMode.Remove : TerrainEditMode.Add);
        }

        foreach (var version in server.State.TerrainVersions.All)
        {
            server.Blocks.TryLoad(version.Key, out var v, out var blob);
            var data = new TerrainBlockData(version.Key, v, blob);
            ClientTerrainReplicator.PasteValidated(first.Node.Terrain, data, first.Codec);
            ClientTerrainReplicator.PasteValidated(second.Node.Terrain, data, second.Codec);
        }

        var expected = server.LiveDigest();
        AssertThat(first.LiveDigest()).IsEqual(expected);
        AssertThat(second.LiveDigest()).IsEqual(expected);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Receive_CorruptOrOversizedBlob_PreservesLastGoodCacheAndClosesPeer()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        await using var client = await TerrainTestWorld.CreateAsync(scene);
        var center = TerrainTestWorld.Ground(8, 0);
        foreach (var world in new[] { server, client })
        {
            world.AddViewer(center);
            AssertThat(await world.WaitLoaded(center, 8)).IsTrue();
        }

        server.Edit(center, 1, TerrainEditMode.Remove);
        var key = server.State.TerrainVersions.All.First().Key;
        using var before = new VoxelBuffer();
        AssertThat(client.Service.TryCopyBlock(key, before)).IsTrue();
        var digest = client.Codec.Digest(before);

        server.Blocks.TryLoad(key, out var version, out var good);
        var garbage = new byte[good.Length];
        new Random(5).NextBytes(garbage);
        foreach (var bad in new[] { good[..(good.Length / 2)], garbage, new byte[(64 * 1024) + 1] })
        {
            // The caller closes the peer on a bad envelope; the scene must stay untouched either way.
            try
            {
                ClientTerrainReplicator.PasteValidated(client.Node.Terrain, new TerrainBlockData(key, version, bad), client.Codec);
            }
            catch (Exception exception) when (exception is ProtocolViolationException or InvalidDataException or ArgumentException)
            {
                // expected
            }
        }

        using var after = new VoxelBuffer();
        AssertThat(client.Service.TryCopyBlock(key, after)).IsTrue();
        AssertThat(client.Codec.Digest(after)).IsEqual(digest);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Receive_UnloadedBlock_IsPastedAfterLoadWithoutStaleRead()
    {
        await using var scene = EngineTestScene.Create();
        await using var server = await TerrainTestWorld.CreateAsync(scene);
        await using var client = await TerrainTestWorld.CreateAsync(scene);
        // Negative coordinates: the blocks around (-8, ground, -8) have negative X and Z.
        var center = TerrainTestWorld.Ground(-8, -8);
        server.AddViewer(center);
        AssertThat(await server.WaitLoaded(center, 8)).IsTrue();
        server.Edit(center, 1, TerrainEditMode.Remove);
        var keys = server.State.TerrainVersions.All.Select(v => v.Key).ToList();

        // The client has not loaded the area: nothing is pasted, the cache keeps the block for the stream.
        foreach (var key in keys)
        {
            server.Blocks.TryLoad(key, out var v, out var blob);
            AssertThat(ClientTerrainReplicator.PasteValidated(client.Node.Terrain, new TerrainBlockData(key, v, blob), client.Codec)).IsFalse();
        }

        client.AddViewer(center);
        AssertThat(await client.WaitLoaded(center, 8)).IsTrue();
        foreach (var key in keys)
        {
            server.Blocks.TryLoad(key, out var v, out var blob);
            AssertThat(ClientTerrainReplicator.PasteValidated(client.Node.Terrain, new TerrainBlockData(key, v, blob), client.Codec)).IsTrue();
        }

        AssertThat(client.LiveDigest()).IsEqual(server.LiveDigest());
    }
}
