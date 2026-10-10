using Godot;

using Horyzont.Core.Ids;

namespace Horyzont.Game.Terrain;

/// <summary>
/// The terrain node of one planet: a <see cref="Node3D"/> at the planet centre with a unit basis (planets do not rotate) holding the
/// configured <see cref="VoxelLodTerrain"/>, its <see cref="TerrainStream"/> and the voxel tool of the terrain service. Voxel
/// coordinates are planet frame positions in metres. In host mode one instance serves the server and the local client.
/// </summary>
public sealed partial class PlanetTerrain : Node3D
{
    private readonly VoxelLodTerrain? _terrain;
    private readonly TerrainStream? _stream;
    private readonly VoxelTool? _tool;

    internal PlanetTerrain(PlanetId planet, VoxelLodTerrain terrain, TerrainStream stream)
    {
        Planet = planet;
        _terrain = terrain;
        _stream = stream;
        Name = $"PlanetTerrain_{planet.Value:X16}";
        AddChild(terrain);
        _tool = terrain.GetVoxelTool();
    }

    /// <summary>Godot needs a parameterless constructor; such a node has no terrain.</summary>
    public PlanetTerrain()
    {
    }

    /// <summary>Planet of the terrain.</summary>
    public PlanetId Planet { get; }

    /// <summary>The voxel terrain node.</summary>
    public VoxelLodTerrain Terrain => _terrain ?? throw new InvalidOperationException("This planet terrain node has no terrain.");

    /// <summary>The stream of the terrain.</summary>
    public TerrainStream Stream => _stream ?? throw new InvalidOperationException("This planet terrain node has no terrain.");

    /// <summary>The voxel tool used for edits, copies and reads (main thread).</summary>
    internal VoxelTool Tool => _tool ?? throw new InvalidOperationException("This planet terrain node has no terrain.");
}
