using GdUnit4;

using Godot;

using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Tests.Support;

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
}
