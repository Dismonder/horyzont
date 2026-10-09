using GdUnit4;

using Godot;

using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

[TestSuite]
public class PlanetTerrainTests
{
    private const ulong GalaxySeed = 0x484F52595A4F4E54UL;

    private static PlanetParams Planet(float radius = 4000f) =>
        PlanetGenesis.CreatePlanet(GalaxySeed, 0, GeneratorProfiles.Current) with { RadiusMeters = radius };

    [TestCase]
    public void Layout_R4000_LodCount11()
    {
        var planet = Planet();

        AssertThat(PlanetTerrainLayout.DataBlockSize).IsEqual(TerrainMath.BlockSize);
        AssertThat(PlanetTerrainLayout.HalfExtentMeters(planet)).IsEqual(4368);
        AssertThat(PlanetTerrainLayout.OctreeSizeVoxels(planet)).IsEqual(16384);
        AssertThat(PlanetTerrainLayout.BoundsHalfExtentVoxels(planet)).IsEqual(16384);
        AssertThat(PlanetTerrainLayout.LodCount(planet)).IsEqual(11);
        AssertThat(PlanetTerrainLayout.LodCount(Planet(10000f))).IsEqual(12);

        // The coarsest block covers the whole diameter.
        foreach (var radius in new[] { 500f, 4000f, 10000f })
        {
            var p = Planet(radius);
            var coarsest = PlanetTerrainLayout.DataBlockSize << (PlanetTerrainLayout.LodCount(p) - 1);
            AssertThat(coarsest).IsGreaterEqual(2 * PlanetTerrainLayout.HalfExtentMeters(p));
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Factory_ConfiguresTerrain()
    {
        var planet = Planet();
        var half = PlanetTerrainLayout.BoundsHalfExtentVoxels(planet);

        var terrain = PlanetTerrainFactory.Create(planet, PlanetTerrainOptions.Client);

        AssertThat(terrain.Name.ToString()).IsEqual($"Planet_{planet.Id.Value:X16}");
        AssertThat(terrain.GetMesher() is VoxelMesherTransvoxel).IsTrue();
        AssertThat(((VoxelMesherTransvoxel)terrain.GetMesher()).GetTexturingMode()).IsEqual(VoxelMesherTransvoxel.TexturingModeEnum.SingleS4);
        AssertThat(terrain.GetStreamingSystem()).IsEqual(VoxelLodTerrain.StreamingSystemEnum.Clipbox);
        AssertThat(terrain.IsFullLoadModeEnabled()).IsFalse();
        AssertThat(terrain.GetGeneratorUseGpu()).IsFalse();
        AssertThat(terrain.GetDataBlockSize()).IsEqual((uint)TerrainMath.BlockSize);
        AssertThat(terrain.GetMeshBlockSize()).IsEqual((uint)PlanetTerrainLayout.MeshBlockSize);
        AssertThat(terrain.GetLodCount()).IsEqual(PlanetTerrainLayout.LodCount(planet));
        AssertThat(terrain.GetVoxelBounds()).IsEqual(new Aabb(new Vector3(-half, -half, -half), new Vector3(2 * half, 2 * half, 2 * half)));
        AssertThat(terrain.GetGenerateCollisions()).IsTrue();
        AssertThat(terrain.GetCollisionLodCount()).IsEqual(1);
        AssertThat(terrain.GetMaterial() is ShaderMaterial).IsTrue();
        AssertThat(terrain.GetGenerator() is VoxelGeneratorGraph).IsTrue();
        AssertThat(terrain.GetFormat().GetChannelDepth(TerrainChannels.Material)).IsEqual(VoxelBuffer.Depth.Depth8Bit);
        AssertThat(terrain.GetStream() == null).IsTrue();
        AssertThat(terrain.IsInsideTree()).IsFalse();
        AssertThat(terrain.Position).IsEqual(Vector3.Zero);
        terrain.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Factory_ServerOptions_NoMaterialNoShadows()
    {
        var terrain = PlanetTerrainFactory.Create(Planet(), PlanetTerrainOptions.Server);

        AssertThat(terrain.GetMaterial() == null).IsTrue();
        AssertThat(terrain.GetShadowCasting()).IsEqual(GeometryInstance3D.ShadowCastingSetting.Off);
        AssertThat(terrain.GetGenerateCollisions()).IsTrue();
        terrain.Free();

        var preview = PlanetTerrainFactory.Create(Planet(), PlanetTerrainOptions.Preview);
        AssertThat(preview.GetGenerateCollisions()).IsFalse();
        AssertThat(preview.GetCollisionLodCount()).IsEqual(0);
        preview.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Factory_UnknownProfile_Throws()
    {
        var planet = Planet() with { GeneratorProfile = "core:planet/999" };

        AssertThrown(() => PlanetTerrainFactory.Create(planet, PlanetTerrainOptions.Server)).IsInstanceOf<NotSupportedException>();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TerrainMaterial_Create_HasShaderAndLayers()
    {
        var material = PlanetTerrainMaterial.Create();
        var layers = (Texture2DArray)material.GetShaderParameter("u_textures");

        AssertThat(material.Shader != null).IsTrue();
        AssertThat(layers.GetLayers()).IsEqual(TerrainMaterials.All.Count);
        AssertThat(layers.GetWidth()).IsEqual(PlanetTerrainMaterial.TextureSize);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Factory_TerrainBlocksEqualDirectGeneration()
    {
        var planet = Planet();
        await using var scene = EngineTestScene.Create();
        var terrain = PlanetTerrainFactory.Create(planet, PlanetTerrainOptions.Server);
        var reference = GeneratorProfiles.CreateGenerator(planet);

        // Blocks around the surface above +X. The viewer works on the worker threads of Voxel Tools.
        var viewer = new VoxelViewer { Position = new Vector3(4000f, 0f, 0f) };
        viewer.SetViewDistance(128);
        viewer.SetRequiresCollisions(true);
        viewer.SetRequiresVisuals(false);
        scene.Add(terrain);
        scene.Add(viewer);

        var origins = new List<Vector3I>();
        for (var dx = 0; dx < 2; dx++)
        {
            for (var dy = 0; dy < 2; dy++)
            {
                for (var dz = 0; dz < 2; dz++)
                {
                    origins.Add(new Vector3I(3984 + (dx * 16), (dy * 16) - 16, (dz * 16) - 16));
                }
            }
        }

        var tool = terrain.GetVoxelTool();
        var bounds = new Aabb(new Vector3(3984, -16, -16), new Vector3(32, 32, 32));
        var loaded = await scene.WaitFramesUntil(() => tool.IsAreaEditable(bounds), 1200);
        AssertThat(loaded).IsTrue();

        var copy = new VoxelBuffer();
        var direct = new VoxelBuffer();
        using var format = TerrainChannels.CreateFormat();
        foreach (var origin in origins)
        {
            copy.Create(16, 16, 16);
            format.ConfigureBuffer(copy);
            tool.Copy(origin, copy, TerrainChannels.UsedChannelsMask);

            direct.Create(16, 16, 16);
            format.ConfigureBuffer(direct);
            reference.GenerateBlock(direct, new Vector3(origin.X, origin.Y, origin.Z), 0);

            AssertThat(copy.GetChannelAsByteArray(TerrainChannels.Sdf)).IsEqual(direct.GetChannelAsByteArray(TerrainChannels.Sdf));
            AssertThat(copy.GetChannelAsByteArray(TerrainChannels.Material)).IsEqual(direct.GetChannelAsByteArray(TerrainChannels.Material));
        }

        copy.Dispose();
        direct.Dispose();
    }
}
