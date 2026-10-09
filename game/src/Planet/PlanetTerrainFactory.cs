using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Rendering;

namespace Horyzont.Game.Planet;

/// <summary>
/// Creates the configured <see cref="VoxelLodTerrain"/> of a planet. The node is returned outside the scene tree and without a
/// stream: the caller (the terrain service of K2-09) attaches the stream before adding the node to the tree, because Voxel Tools
/// starts streaming as soon as the node enters the tree. Every property that affects data or layout is set explicitly.
/// </summary>
public static class PlanetTerrainFactory
{
    /// <summary>Distance (voxels) of the LOD 0 ring; measured in step 9 of K2-08, see the plan.</summary>
    public const float LodDistance = 96f;

    /// <summary>Maximum view distance of the terrain in voxels; viewers ask for less.</summary>
    public const int MaxViewDistance = 16384;

    /// <summary>Creates the terrain node. Main thread.</summary>
    /// <param name="planet">Planet parameters (profile, radius).</param>
    /// <param name="options">Active parts of the node.</param>
    /// <param name="material">Render material shared between planets (create it once with <see cref="PlanetTerrainMaterial.Create"/>); when null and rendering is on, a new one is created.</param>
    /// <exception cref="NotSupportedException">Unknown generator profile or voxel size other than 1.</exception>
    public static VoxelLodTerrain Create(PlanetParams planet, PlanetTerrainOptions options, Material? material = null) =>
        Create(planet, options, material, null);

    /// <summary>Creates the terrain node with a generator made earlier for this planet (K5-22: generators are compiled once at start). Main thread.</summary>
    /// <param name="planet">Planet parameters (profile, radius).</param>
    /// <param name="options">Active parts of the node.</param>
    /// <param name="material">Render material shared between planets; see the other overload.</param>
    /// <param name="generator">Generator of <paramref name="planet"/> from <see cref="GeneratorProfiles.CreateGenerator"/>; made here when null.</param>
    /// <returns>The terrain node, outside the tree and without a stream.</returns>
    /// <exception cref="NotSupportedException">Unknown generator profile or voxel size other than 1.</exception>
    public static VoxelLodTerrain Create(PlanetParams planet, PlanetTerrainOptions options, Material? material, VoxelGenerator? generator)
    {
        ArgumentNullException.ThrowIfNull(planet);
        generator ??= GeneratorProfiles.CreateGenerator(planet);
        var half = PlanetTerrainLayout.BoundsHalfExtentVoxels(planet);

        var mesher = new VoxelMesherTransvoxel();
        mesher.SetTexturingMode(VoxelMesherTransvoxel.TexturingModeEnum.SingleS4);
        mesher.SetTexturesIgnoreAirVoxels(false);
        mesher.SetTransitionsEnabled(true);
        mesher.SetEdgeClampMargin(0.02f);
        mesher.SetMeshOptimizationEnabled(false);

        var terrain = new VoxelLodTerrain { Name = $"Planet_{planet.Id.Value:X16}" };
        terrain.SetFormat(TerrainChannels.CreateFormat());
        terrain.SetGenerator(generator);
        terrain.SetMesher(mesher);
        terrain.SetStreamingSystem(VoxelLodTerrain.StreamingSystemEnum.Clipbox);
        terrain.SetFullLoadModeEnabled(false);
        terrain.SetGeneratorUseGpu(false);
        terrain.SetNormalmapEnabled(false);
        terrain.SetCacheGeneratedBlocks(false);

        // Order matters: Voxel Tools snaps the bounds to the octree size of the lod count and mesh block size set at that moment.
        terrain.SetLodCount(PlanetTerrainLayout.LodCount(planet));
        terrain.SetMeshBlockSize((uint)PlanetTerrainLayout.MeshBlockSize);
        terrain.SetVoxelBounds(new Aabb(new Vector3(-half, -half, -half), new Vector3(2 * half, 2 * half, 2 * half)));
        terrain.SetLodDistance(LodDistance);
        terrain.SetViewDistance(MaxViewDistance);

        terrain.SetGenerateCollisions(options.Collisions);
        terrain.SetCollisionLodCount(options.Collisions ? 1 : 0);

        if (options.Render)
        {
            terrain.SetMaterial(PlanetTerrainMaterial.ForPlanet(material ?? PlanetTerrainMaterial.Create(), planet));
            terrain.SetShadowCasting(GeometryInstance3D.ShadowCastingSetting.On);

            // Visual dressing only (no state, no collisions): the sea, the scattered flora and the glow of the atmosphere.
            terrain.AddChild(PlanetOcean.Create(planet));
            terrain.AddChild(PlanetFlora.Create(planet));
            terrain.AddChild(PlanetAtmosphere.Create(planet));
        }
        else
        {
            terrain.SetShadowCasting(GeometryInstance3D.ShadowCastingSetting.Off);
        }

        return terrain;
    }
}
