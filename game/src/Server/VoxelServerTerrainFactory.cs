using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Rendering;
using Horyzont.Game.Server.Terrain;
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
public sealed partial class VoxelServerTerrainFactory : IServerTerrainFactory
{
    private readonly Node _parent;
    private readonly PlanetTerrainOptions _options;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggers;
    private readonly TerrainBufferOptions _buffer;
    private Material? _material;
    private readonly List<TerrainBlockStore> _failedStores = [];

    /// <summary>File name of the disposable database of derived mipmaps, next to the world file.</summary>
    public const string DerivedDatabaseName = "terrain-derived.db";

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

    /// <summary>Observed cleanup of the last failed construction; never blocks the scene thread.</summary>
    internal Task FailureCleanup { get; private set; } = Task.CompletedTask;

    /// <summary>Retained stores whose failure cleanup has not drained successfully.</summary>
    internal int FailedStoreCount => _failedStores.Count;

    /// <summary>Test seam for failure during a later planet's construction.</summary>
    internal Func<PlanetParams, PlanetTerrainOptions, Material?, VoxelLodTerrain>? CreatePlanet { get; init; }

    /// <summary>Test seam for stopping an actual store read before construction fails.</summary>
    internal Action<TerrainBlockStore>? StoreCreated { get; init; }

    /// <summary>Test seam for issuing a read of an already built planet.</summary>
    internal Action<PlanetTerrain>? PlanetCreated { get; init; }

    /// <inheritdoc />
    public IServerTerrain Create(string worldPath, WorldState state)
    {
        ArgumentException.ThrowIfNullOrEmpty(worldPath);
        ArgumentNullException.ThrowIfNull(state);
        var store = new TerrainBlockStore(worldPath);
        ServerPlanetTerrains? planets = null;
        VoxelTerrainService? service = null;
        ServerTerrainBlocks? blocks = null;
        DerivedTerrain? derived = null;
        try
        {
            StoreCreated?.Invoke(store);
            blocks = new ServerTerrainBlocks(store, state.TerrainVersions.All, _buffer, _time, _loggers.CreateLogger<ServerTerrainBlocks>());
            service = new VoxelTerrainService(
                blocks, new TerrainBlockCodec(), TerrainMaterialMap.Build(state.Materials), _loggers.CreateLogger<VoxelTerrainService>());
            _material ??= _options.Render ? PlanetTerrainMaterial.Create() : null;
            derived = new DerivedTerrain(new DerivedLodStore(
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldPath))!, DerivedDatabaseName), _time, _loggers.CreateLogger<DerivedLodStore>()));
            // K5-22: generators and mipmap rebuilders once per body; terrain nodes only for active planets (the home planet now).
            var parent = _parent as Node3D ?? throw new InvalidOperationException("The server terrain parent must be a Node3D.");
            var custom = CreatePlanet;
            var derivedTerrain = derived;
            var blockSource = blocks;
            planets = new ServerPlanetTerrains(
                service,
                state,
                (planet, generator) => derivedTerrain.Add(planet, blockSource, generator),
                parent,
                (planet, generator) => custom is not null ? custom(planet, _options, _material) : PlanetTerrainFactory.Create(planet, _options, _material, generator),
                _loggers.CreateLogger<ServerPlanetTerrains>())
            {
                Created = PlanetCreated,
            };
            planets.ActivateHome();

            // After a restart the mipmaps above every edited block are checked against their fingerprint and rebuilt where they differ.
            derived.Invalidate(state.TerrainVersions.All);
            service.EditApplied += derived.Invalidate;
            Terrain = new VoxelServerTerrain(service, blocks, store, planets, _time, derived);
            return Terrain;
        }
        catch
        {
            if (service is not null)
            {
                // Every node the planet terrains made: out of the tree, stream closed, freed.
                foreach (var planet in state.Planets.All)
                {
                    service.RemovePlanet(planet.Id);
                }
            }

            _failedStores.Add(store);
            FailureCleanup = CleanupAsync(service, blocks, store, derived);
            throw;
        }
    }

    private async Task CleanupAsync(VoxelTerrainService? service, ServerTerrainBlocks? blocks, TerrainBlockStore store, DerivedTerrain? derived)
    {
        try
        {
            await TerrainFailureCleanup.RunAsync(
                service is not null ? service.ShutdownAsync : blocks is not null ? blocks.CloseAsync : () => Task.FromResult(true),
                () =>
                {
                    store.Close();
                    _failedStores.Remove(store);
                },
                () => service?.Dispose()).ConfigureAwait(true);

            if (derived is not null)
            {
                // Rebuildable data: a timeout leaves the writer to finish alone and never blocks the cleanup of the world store.
                await TerrainFailureCleanup.DisposeAfterCloseAsync(derived.CloseAsync, derived.Dispose).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            // Preserve the original startup failure and keep the store open if draining failed.
            LogCleanupFailure(_loggers.CreateLogger<VoxelServerTerrainFactory>(), exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to drain terrain after construction failed; the block store remains open.")]
    private static partial void LogCleanupFailure(ILogger logger, Exception exception);
}
