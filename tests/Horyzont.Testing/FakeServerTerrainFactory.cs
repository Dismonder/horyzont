using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Server;

namespace Horyzont.Testing;

/// <summary>Creates <see cref="FakeServerTerrain"/> and records how the runtime called it.</summary>
public sealed class FakeServerTerrainFactory(TimeProvider time) : IServerTerrainFactory
{
    /// <inheritdoc />
    public ushort SemanticsVersion { get; set; } = 1;

    /// <inheritdoc />
    public string VoxelRef { get; set; } = "test-voxel";

    /// <summary>Gets the terrains created, in order.</summary>
    public List<FakeServerTerrain> Created { get; } = [];

    /// <summary>Gets the call log: world path, last operation and terrain versions of the state at the time of the call.</summary>
    public List<(string Path, OpId LastOpId, int TerrainVersionCount, bool FileExisted)> Calls { get; } = [];

    /// <summary>Gets or sets a callback run for each terrain just created.</summary>
    public Action<FakeServerTerrain>? OnCreated { get; set; }

    /// <summary>Gets the last terrain created.</summary>
    public FakeServerTerrain Last => Created[^1];

    /// <inheritdoc />
    public IServerTerrain Create(string worldPath, WorldState state)
    {
        Calls.Add((worldPath, state.LastOpId, state.TerrainVersions.Count, File.Exists(worldPath)));
        var terrain = new FakeServerTerrain(worldPath, state, time);
        Created.Add(terrain);
        OnCreated?.Invoke(terrain);
        return terrain;
    }
}
