using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Terrain;

/// <summary>
/// <see cref="ITerrainService"/> over Voxel Tools (K2-09). Every block an edit may change is copied right after the edit, encoded
/// and recorded with the version Core just bumped, so the data of an edited block always has the version <c>WorldState</c> knows.
/// All <see cref="ITerrainService"/> members run on the Godot main thread only (another thread gets
/// <see cref="InvalidOperationException"/>); the streams of the planets run on Voxel Tools worker threads and read only
/// <see cref="ServerTerrainBlocks"/>.
/// </summary>
public sealed class VoxelTerrainService : ITerrainService, IDisposable
{
    private readonly ServerTerrainBlocks _blocks;
    private readonly TerrainBlockCodec _codec;
    private readonly TerrainMaterialMap _materials;
    private readonly ILogger<VoxelTerrainService> _logger;
    private readonly SortedDictionary<ulong, Entry> _planets = [];
    private readonly List<TerrainBlockKey> _keys = [];
    private readonly List<TerrainBlockKey> _corner = new(1);
    private readonly VoxelBuffer _copy = new();
    private bool _shutDown;

    /// <summary>Creates the service. Main thread.</summary>
    /// <param name="blocks">Edited blocks of the world (buffer and store).</param>
    /// <param name="codec">Block codec.</param>
    /// <param name="materials">Material map of the world (K2-08); applied to add edits.</param>
    /// <param name="logger">Logger.</param>
    public VoxelTerrainService(ServerTerrainBlocks blocks, TerrainBlockCodec codec, TerrainMaterialMap materials, ILogger<VoxelTerrainService> logger)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(logger);
        _blocks = blocks;
        _codec = codec;
        _materials = materials;
        _logger = logger;
        _copy.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
    }

    /// <summary>The first block a stream could not load, over all planets; null while none failed. Main thread.</summary>
    public TerrainBlockKey? FirstStreamError
    {
        get
        {
            foreach (var entry in _planets.Values)
            {
                if (entry.Node.Stream.FirstError is { } key)
                {
                    return key;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The first block whose data could not be served, over all planets: <see cref="FirstStreamError"/>, else
    /// <see cref="ServerTerrainBlocks.FirstCorruptKey"/>; null while none. Once set, Voxel Tools may hold an empty editable placeholder
    /// for the block (S1), so <see cref="ApplyEdit"/> and <see cref="LoadAreaBlocking"/> throw and <see cref="IsAreaLoaded"/> is false
    /// for the rest of the session. Main thread.
    /// </summary>
    public TerrainBlockKey? FirstFault => FirstStreamError ?? _blocks.FirstCorruptKey;

    /// <summary>
    /// Wraps a terrain node made by <see cref="PlanetTerrainFactory.Create"/> and attaches the stream; the caller adds the returned
    /// node to the scene tree (Voxel Tools starts streaming when the terrain enters the tree). Main thread.
    /// </summary>
    /// <param name="planet">The planet.</param>
    /// <param name="configured">Its terrain node, outside the tree and without a stream.</param>
    /// <param name="derived">Persistent mipmaps of the edited blocks of the planet; null for session mipmaps only.</param>
    /// <returns>The planet terrain node.</returns>
    /// <exception cref="NotSupportedException">Voxel size other than 1 m or data block size other than <see cref="TerrainMath.BlockSize"/>.</exception>
    /// <exception cref="InvalidOperationException">Full load mode, a stream already set, a node in the tree, a known planet or a shut down service.</exception>
    public PlanetTerrain AddPlanet(PlanetParams planet, VoxelLodTerrain configured, DerivedLodRebuilder? derived = null)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(configured);
        if (planet.VoxelSizeMeters != 1f)
        {
            throw new NotSupportedException($"Planet {planet.Id} has voxels of {planet.VoxelSizeMeters} m; the terrain adapter supports 1 m.");
        }

        if (configured.GetDataBlockSize() != TerrainMath.BlockSize)
        {
            throw new NotSupportedException($"Terrain data blocks of {configured.GetDataBlockSize()} voxels; {TerrainMath.BlockSize} expected.");
        }

        if (configured.IsFullLoadModeEnabled())
        {
            throw new InvalidOperationException("The terrain must stream; full load mode is enabled.");
        }

        if (configured.Stream is not null || configured.IsInsideTree())
        {
            throw new InvalidOperationException("The terrain node must come from the factory: outside the tree and without a stream.");
        }

        if (_shutDown || _planets.ContainsKey(planet.Id.Value))
        {
            throw new InvalidOperationException($"Planet {planet.Id} cannot be added (known planet or service shut down).");
        }

        var stream = new TerrainStream(planet.Id, _blocks, _codec, new DerivedLodCache(), _logger, derived);
        configured.Stream = stream;
        var node = new PlanetTerrain(planet.Id, configured, stream);
        _planets.Add(planet.Id.Value, new Entry(planet, node));
        return node;
    }

    /// <summary>The terrain node of a planet. Main thread.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="terrain">The node when known.</param>
    /// <returns>Whether the planet is known.</returns>
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? terrain)
    {
        EnsureMainThread();
        if (_planets.TryGetValue(planet.Value, out var entry))
        {
            terrain = entry.Node;
            return true;
        }

        terrain = null;
        return false;
    }

    /// <inheritdoc />
    public bool IsAreaLoaded(PlanetId planet, Vector3 center, float radius)
    {
        EnsureMainThread();
        if (_shutDown || !_planets.TryGetValue(planet.Value, out var entry) || _blocks.IsSaturated)
        {
            return false;
        }

        if (FirstFault is { } error)
        {
            if (!entry.FaultLogged)
            {
                entry.FaultLogged = true;
                TerrainGameLog.StreamFaultObserved(_logger, planet.Value, error);
            }

            return false;
        }

        return IsBlockAreaEditable(entry, center, radius);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Voxel Tools does not load synchronously: the area must have been loaded before (<see cref="TerrainAreaLoader"/>, as
    /// <see cref="TerrainReplay"/> does); otherwise this throws.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Unknown planet, a terrain fault (<see cref="FirstFault"/>) or area not preloaded.</exception>
    public void LoadAreaBlocking(PlanetId planet, Vector3 center, float radius)
    {
        EnsureMainThread();
        if (!_planets.TryGetValue(planet.Value, out var entry))
        {
            throw new InvalidOperationException($"Planet {planet} has no terrain.");
        }

        ThrowIfFaulted();

        if (!IsBlockAreaEditable(entry, center, radius))
        {
            throw new InvalidOperationException($"Terrain area of planet {planet} around {center} (radius {radius} m) not preloaded.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A ray march over the SDF with trilinear samples, stepping by the sampled distance (sphere tracing, scaled by
    /// <see cref="TerrainEditSemantics.RaycastSdfStepFactor"/>, at least <see cref="TerrainEditSemantics.RaycastMinStepMeters"/>, at
    /// most <see cref="TerrainEditSemantics.RaycastMaxSteps"/> steps), a binary search after the sign change and the normal from the
    /// SDF gradient. Allocates no managed memory.
    /// </remarks>
    public bool Raycast(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        EnsureMainThread();
        hit = default;
        return _planets.TryGetValue(planet.Value, out var entry) && TerrainRaycaster.Raycast(entry.Node.Tool, origin, direction, maxDistance, out hit);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Unknown planet or shape, a terrain fault (<see cref="FirstFault"/>), versions other than the affected blocks, or an area that
    /// is not editable; nothing changed.
    /// </exception>
    public void ApplyEdit(TerrainEditOp edit, IReadOnlyList<TerrainBlockVersion> versions)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(versions);
        if (_shutDown || !_planets.TryGetValue(edit.Planet.Value, out var entry))
        {
            throw new InvalidOperationException($"Planet {edit.Planet} has no terrain.");
        }

        ThrowIfFaulted();
        if (edit.Shape != TerrainShape.Sphere || (edit.Mode != TerrainEditMode.Add && edit.Mode != TerrainEditMode.Remove))
        {
            throw new InvalidOperationException($"Unsupported terrain edit {edit.Shape} {edit.Mode}.");
        }

        // Everything that can fail is checked before the first voxel changes.
        var texture = edit.Mode == TerrainEditMode.Add ? _materials.ToTextureIndex(edit.MaterialId) : (byte)0;
        TerrainMath.AffectedBlocks(
            edit.Planet, edit.Center, edit.Radius, entry.Planet.VoxelSizeMeters, TerrainRules.EditMarginVoxels, _keys);
        if (versions.Count != _keys.Count)
        {
            throw new InvalidOperationException($"Terrain edit got {versions.Count} block versions for {_keys.Count} affected blocks.");
        }

        for (var i = 0; i < _keys.Count; i++)
        {
            if (versions[i].Key != _keys[i])
            {
                throw new InvalidOperationException($"Terrain edit got version of block {versions[i].Key} where {_keys[i]} was expected.");
            }
        }

        var tool = entry.Node.Tool;
        if (!tool.IsAreaEditable(BlockBox(_keys[0], _keys[^1])))
        {
            throw new InvalidOperationException($"Terrain edit at {edit.Center} on planet {edit.Planet}: its blocks are not editable; nothing changed.");
        }

        TerrainSphereEdit.Apply(tool, edit.Mode, TerrainVectors.ToGodot(edit.Center), edit.Radius, texture);

        for (var i = 0; i < versions.Count; i++)
        {
            var key = versions[i].Key;
            tool.Copy(BlockOrigin(key), _copy, TerrainChannels.UsedChannelsMask);
            _blocks.Record(key, versions[i].Version, _codec.Encode(_copy));
        }
    }

    /// <summary>
    /// Stops the streams of all planets, then closes <see cref="ServerTerrainBlocks"/> and waits for its reads. Only when this
    /// returns true may the caller close the <see cref="TerrainBlockStore"/> (K2-10, shutdown order); on false the store stays open
    /// until the process ends. Main thread.
    /// </summary>
    /// <returns>True when no block read runs any more.</returns>
    public async Task<bool> ShutdownAsync()
    {
        EnsureMainThread();
        _shutDown = true;
        foreach (var entry in _planets.Values)
        {
            entry.Node.Stream.Close();
        }

        var drained = await _blocks.CloseAsync().ConfigureAwait(true);
        TerrainGameLog.ShutDown(_logger, drained);
        return drained;
    }

    /// <summary>Copies a LOD 0 block of a planet as Voxel Tools holds it (tests and diagnostics). Main thread.</summary>
    /// <param name="key">LOD 0 block.</param>
    /// <param name="into">Receives the block.</param>
    /// <returns>False when the planet is unknown or the block is not loaded.</returns>
    public bool TryCopyBlock(TerrainBlockKey key, VoxelBuffer into)
    {
        EnsureMainThread();
        ArgumentNullException.ThrowIfNull(into);
        if (key.Lod != 0 || !_planets.TryGetValue(key.Planet.Value, out var entry) || !entry.Node.Tool.IsAreaEditable(BlockBox(key, key)))
        {
            return false;
        }

        into.Create(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        entry.Node.Tool.Copy(BlockOrigin(key), into, TerrainChannels.UsedChannelsMask);
        return true;
    }

    /// <summary>
    /// Whether every LOD 0 block of the box <c>[center - extent, center + extent]</c> is loaded (the same blocks
    /// <see cref="TerrainMath.AffectedBlocks"/> gives for that extent). Main thread.
    /// </summary>
    internal bool IsAreaEditable(PlanetId planet, Vector3 center, float extent)
    {
        EnsureMainThread();
        return _planets.TryGetValue(planet.Value, out var entry) && IsBlockAreaEditable(entry, center, extent);
    }

    /// <inheritdoc />
    public void Dispose() => _copy.Dispose();

    private static void EnsureMainThread()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
        {
            throw new InvalidOperationException("ITerrainService runs on the Godot main thread only.");
        }
    }

    /// <summary>Throws when a block could not be served (<see cref="FirstFault"/>). Main thread.</summary>
    /// <exception cref="InvalidOperationException">A terrain fault.</exception>
    internal void ThrowIfFaulted()
    {
        if (FirstFault is { } key)
        {
            throw new InvalidOperationException(
                $"Terrain block {key} could not be loaded (missing or corrupt data); terrain edits are refused for this session so the "
                + "empty block Voxel Tools put in its place is never recorded.");
        }
    }

    private static Vector3I BlockOrigin(TerrainBlockKey key) =>
        new(key.X * TerrainMath.BlockSize, key.Y * TerrainMath.BlockSize, key.Z * TerrainMath.BlockSize);

    private static Aabb BlockBox(TerrainBlockKey min, TerrainBlockKey max)
    {
        var origin = BlockOrigin(min);
        var end = BlockOrigin(max) + new Vector3I(TerrainMath.BlockSize, TerrainMath.BlockSize, TerrainMath.BlockSize);
        return new Aabb(origin, end - origin);
    }

    private bool IsBlockAreaEditable(Entry entry, Vector3 center, float extent)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z) || !float.IsFinite(extent) || extent < 0f)
        {
            return false;
        }

        // The corner blocks through TerrainMath (floor, the same float operations as AffectedBlocks with this extent).
        var size = new Vector3(extent);
        var voxel = entry.Planet.VoxelSizeMeters;
        TerrainMath.AffectedBlocks(entry.Planet.Id, center - size, 0f, voxel, 0, _corner);
        var min = _corner[0];
        TerrainMath.AffectedBlocks(entry.Planet.Id, center + size, 0f, voxel, 0, _corner);
        var max = _corner[0];
        return entry.Node.Tool.IsAreaEditable(BlockBox(min, max));
    }

    private sealed class Entry(PlanetParams planet, PlanetTerrain node)
    {
        public PlanetParams Planet { get; } = planet;

        public PlanetTerrain Node { get; } = node;

        public bool FaultLogged { get; set; }
    }
}
