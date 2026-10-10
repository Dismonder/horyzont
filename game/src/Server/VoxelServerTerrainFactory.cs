using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Terrain;
using Horyzont.Persistence.Terrain;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Server;

/// <summary>
/// Makes the server terrain from the loaded world (decision D7): opens the block store of the world file, the buffer of edited
/// blocks with the versions of the last checkpoint, the terrain service with the material map of the world and one planet node per
/// planet, added under <c>parent</c> (the scene tree, so Voxel Tools starts streaming).
/// </summary>
public sealed class VoxelServerTerrainFactory : IServerTerrainFactory
{
    private readonly Node _parent;
    private readonly PlanetTerrainOptions _options;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly TerrainBufferOptions _buffer;
    private Material? _material;

    /// <summary>Creates the factory.</summary>
    /// <param name="parent">Node the planet terrains are added to (<c>/Root/Server/Terrain</c>); in the scene tree.</param>
    /// <param name="options">Active parts of the terrain nodes.</param>
    /// <param name="time">Clock of the buffer and the replay.</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="buffer">Settings of the buffer of edited blocks; defaults when null.</param>
    public VoxelServerTerrainFactory(Node parent, PlanetTerrainOptions options, TimeProvider time, ILoggerFactory loggers, TerrainBufferOptions? buffer = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        _parent = parent;
        _options = options;
        _time = time;
        _loggers = loggers;
        _buffer = buffer ?? new TerrainBufferOptions();
    }

    /// <inheritdoc />
    public ushort SemanticsVersion => TerrainEditSemantics.SemanticsVersion;

    /// <inheritdoc />
    public string VoxelRef => TerrainEditSemantics.VoxelRef;

    /// <summary>Gets the terrain this factory made last (null before the first call).</summary>
    public VoxelServerTerrain? Terrain { get; private set; }

    /// <inheritdoc />
    public IServerTerrain Create(string worldPath, WorldState state)
    {
        ArgumentException.ThrowIfNullOrEmpty(worldPath);
        ArgumentNullException.ThrowIfNull(state);
        var store = new TerrainBlockStore(worldPath);
        var nodes = new List<PlanetTerrain>();
        VoxelTerrainService? service = null;
        try
        {
            var blocks = new ServerTerrainBlocks(store, state.TerrainVersions.All, _buffer, _time, _loggers.CreateLogger<ServerTerrainBlocks>());
            service = new VoxelTerrainService(
                blocks, new TerrainBlockCodec(), TerrainMaterialMap.Build(state.Materials), _loggers.CreateLogger<VoxelTerrainService>());
            _material ??= _options.Render ? PlanetTerrainMaterial.Create() : null;
            foreach (var planet in state.Planets.All)
            {
                var node = service.AddPlanet(planet, PlanetTerrainFactory.Create(planet, _options, _material));
                nodes.Add(node);
                _parent.AddChild(node);
            }

            Terrain = new VoxelServerTerrain(service, blocks, store, nodes, _time);
            return Terrain;
        }
        catch
        {
            // Nothing streamed yet from a terrain that failed to build: free what exists so the world file is not left locked, then rethrow.
            foreach (var node in nodes)
            {
                node.GetParent()?.RemoveChild(node);
                node.Stream.Close();
                node.QueueFree();
            }

            service?.Dispose();
            store.Close();
            throw;
        }
    }
}
