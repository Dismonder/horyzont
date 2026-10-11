using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Client.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Terrain;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client;

/// <summary>
/// Terrain of a remote client: one <see cref="PlanetTerrain"/> per planet of the server hello, under the given parent node
/// (<c>/Root/Client/Terrain</c>), from the planet factory. Without cache settings the streams use a
/// <see cref="GeneratorOnlyBlockSource"/>; with them (K3-16) the streams read the persistent <see cref="ClientTerrainCache"/> of the
/// world, in which the edited blocks of the server are kept.
/// </summary>
public sealed class OwnTerrainProvider : IClientTerrainProvider, IDisposable
{
    private readonly Node _parent;
    private readonly PlanetTerrainOptions _options;
    private readonly Material? _material;
    private readonly ILogger _logger;
    private OwnTerrainCacheSettings? _cacheSettings;
    private readonly GeneratorOnlyBlockSource _generatorOnly = new();
    private readonly TerrainBlockCodec _codec = new();
    private readonly SortedDictionary<ulong, PlanetTerrain> _planets = [];
    private readonly ClientTerrainViews _views = new();
    private readonly List<(DerivedLodRebuilder Rebuilder, Action Handler)> _reloads = [];
    private readonly SortedDictionary<ulong, PlanetParams> _known = [];
    private readonly SortedDictionary<ulong, (VoxelGenerator Generator, DerivedLodRebuilder? Rebuilder)> _generators = [];
    private ITerrainBlockSource? _source;
    private string? _cacheDirectory;
    private DerivedTerrain? _derived;

    /// <summary>Creates the provider.</summary>
    /// <param name="parent">Node the planet terrains are added to.</param>
    /// <param name="options">Active parts of the terrain nodes.</param>
    /// <param name="material">Render material shared by the planets; null without rendering.</param>
    /// <param name="logger">Logger of the streams.</param>
    /// <param name="cacheSettings">Where and how the persistent terrain cache lives; null for a cache-less terrain (generator only).</param>
    public OwnTerrainProvider(Node parent, PlanetTerrainOptions options, Material? material, ILogger logger, OwnTerrainCacheSettings? cacheSettings = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(logger);
        _parent = parent;
        _options = options;
        _material = material;
        _logger = logger;
        _cacheSettings = cacheSettings;
    }

    /// <summary>Gets the number of planet terrains this provider holds.</summary>
    public int PlanetCount => _planets.Count;

    /// <summary>Sets where the persistent cache lives; call before the server hello (the composition does, after it created the provider).</summary>
    /// <param name="settings">The cache settings.</param>
    public void ConfigureCache(OwnTerrainCacheSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _cacheSettings = settings;
    }

    /// <summary>Gets the persistent cache of the world; null without cache settings or before the first server hello.</summary>
    public ClientTerrainCache? Cache { get; private set; }

    /// <summary>Gets the persistent mipmaps of the edited blocks (K3-16); null without cache.</summary>
    public DerivedTerrain? Derived => _derived;

    /// <summary>Tells the mipmaps that a block of the cache changed: the blocks above it are stale until rebuilt.</summary>
    /// <param name="key">LOD zero key that was stored in the cache.</param>
    public void OnBlockStored(TerrainBlockKey key)
    {
        if (_derived is not null && key.Lod == 0)
        {
            _derived.Invalidate([new TerrainBlockVersion(key, 0)]);
        }
    }

    /// <summary>Rebuilds a few mipmaps (at most two per planet); call once per frame on the main thread.</summary>
    public void StepDerived() => _derived?.Step();

    /// <summary>Gets the envelope codec of the terrain (its scratch buffers are per thread).</summary>
    public TerrainBlockCodec Codec => _codec;

    /// <summary>Gets the terrain node of a planet (internal: the client-facing API never hands out mutable terrain).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="node">The node when the planet has a terrain.</param>
    /// <returns>Whether the planet has a terrain.</returns>
    internal bool TryGetNode(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? node)
    {
        if (_planets.TryGetValue(planet.Value, out node) && GodotObject.IsInstanceValid(node))
        {
            return true;
        }

        node = null;
        return false;
    }

    /// <inheritdoc />
    public void Prepare(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        Clear();
        Cache = OpenCache(hello);
        ITerrainBlockSource source = Cache is not null ? Cache : _generatorOnly;
        if (Cache is not null && _cacheSettings is { } settings && _cacheDirectory is not null)
        {
            _derived = new DerivedTerrain(new DerivedLodStore(
                Path.Combine(_cacheDirectory, "terrain-derived.db"), settings.Time, settings.Loggers.CreateLogger<DerivedLodStore>()));
        }

        // K5-22 decision 10: nodes are created on activation (ClientPlanetTerrains). The home planet (element 0 of the hello, K5-21
        // decision 10) is activated at once, so the first body never waits for a selection.
        _source = source;
        foreach (var planet in hello.Planets)
        {
            _known[planet.Id.Value] = planet;
        }

        if (hello.Planets.Count > 0)
        {
            Activate(hello.Planets[0].Id);
        }

        if (_derived is not null && Cache is not null)
        {
            // After a restart the mipmaps above every cached block are checked against their fingerprint and rebuilt where they differ.
            var versions = new TerrainBlockVersion[ProtocolConstants.MaxTerrainCacheEntries];
            foreach (var planet in hello.Planets)
            {
                int read;
                var offset = 0;
                do
                {
                    read = Cache.CopyVersions(planet.Id, versions, offset);
                    _derived.Invalidate(versions.Take(read));
                    offset += read;
                }
                while (read == versions.Length);
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out IClientTerrainView? view)
    {
        _planets.TryGetValue(planet.Value, out var terrain);
        return _views.TryViewOf(planet, terrain, out view);
    }

    /// <summary>Gets or sets the client frame nodes (K5-20); a terrain node goes under <c>Planet_&lt;id&gt;</c>, else under the terrain parent.</summary>
    public Horyzont.Game.Frames.FrameNodes? FrameNodes { get; set; }

    /// <inheritdoc />
    public bool Activate(PlanetId planet)
    {
        if (TryGetNode(planet, out _))
        {
            return true;
        }

        if (_source is null || !_known.TryGetValue(planet.Value, out var parameters))
        {
            return false;
        }

        // The generator (graph compilation) and the mipmap rebuilder are made once per body and kept across activations.
        if (!_generators.TryGetValue(planet.Value, out var made))
        {
            var generator = GeneratorProfiles.CreateGenerator(parameters);
            made = (generator, _derived is not null && Cache is not null ? _derived.Add(parameters, Cache, generator) : null);
            _generators.Add(planet.Value, made);
        }

        var terrain = PlanetTerrainFactory.Create(parameters, _options, _material, made.Generator);
        var stream = new TerrainStream(planet, _source, _codec, new DerivedLodCache(), _logger, made.Rebuilder);
        terrain.Stream = stream;
        var node = new PlanetTerrain(planet, terrain, stream);
        _planets[planet.Value] = node;
        if (FrameNodes is not { } frames || !frames.Attach(node, FrameRef.Of(planet)))
        {
            _parent.AddChild(node);
        }

        if (made.Rebuilder is { } rebuilder)
        {
            // Without it the engine keeps placeholders for the mipmaps it asked for while they were stale (as in the server terrain).
            rebuilder.ReloadRequested += node.ReloadFromStream;
            _reloads.Add((rebuilder, node.ReloadFromStream));
        }

        return true;
    }

    /// <inheritdoc />
    /// <remarks>The disk cache of the blocks (K3-16) stays, so coming back is fast.</remarks>
    public void Release(PlanetId planet)
    {
        if (!_planets.Remove(planet.Value, out var node))
        {
            return;
        }

        for (var i = _reloads.Count - 1; i >= 0; i--)
        {
            if (_reloads[i].Handler.Target == node)
            {
                _reloads[i].Rebuilder.ReloadRequested -= _reloads[i].Handler;
                _reloads.RemoveAt(i);
            }
        }

        if (GodotObject.IsInstanceValid(node))
        {
            node.Stream.Close();
            node.GetParent()?.RemoveChild(node);
            node.QueueFree();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Clear();

    /// <summary>
    /// The fingerprint of a cache: everything that makes a cached block mean something else when it changes. A cache with another
    /// fingerprint is dropped on open (the world of the server is never touched).
    /// </summary>
    /// <param name="hello">The server hello.</param>
    /// <param name="buildHash">Hash of the build (generator profile components).</param>
    /// <returns>The fingerprint.</returns>
    internal static string FingerprintOf(ServerHello hello, ulong buildHash)
    {
        var text = new System.Text.StringBuilder();
        text.Append("schema=1|world=").Append(hello.World.Value.ToString("N", System.Globalization.CultureInfo.InvariantCulture))
            .Append("|build=").Append(buildHash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture))
            .Append("|semantics=").Append(TerrainEditSemantics.Current);
        foreach (var planet in hello.Planets)
        {
            text.Append("|planet=").Append(planet);
        }

        return text.ToString();
    }

    private ClientTerrainCache? OpenCache(ServerHello hello)
    {
        if (_cacheSettings is not { } settings)
        {
            return null;
        }

        var path = Path.Combine(settings.Root, hello.World.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture), "terrain.db");
        _cacheDirectory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(_cacheDirectory);
        return new ClientTerrainCache(path, FingerprintOf(hello, settings.BuildHash), settings.Time, settings.Loggers.CreateLogger<ClientTerrainCache>());
    }

    private void Clear()
    {
        foreach (var (rebuilder, handler) in _reloads)
        {
            rebuilder.ReloadRequested -= handler;
        }

        _reloads.Clear();
        foreach (var node in _planets.Values)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.Stream.Close();
                node.GetParent()?.RemoveChild(node);
                node.QueueFree();
            }
        }

        _planets.Clear();
        _views.Clear();
        _known.Clear();
        _generators.Clear();
        _source = null;
        var derived = _derived;
        _derived = null;
        if (Cache is { } cache)
        {
            // The writer saves what is queued and the file closes only when nothing reads it any more (five seconds at most).
            Cache = null;
            _ = CloseCacheAsync(cache, derived);
        }
        else if (derived is not null)
        {
            _ = CloseCacheAsync(null, derived);
        }
    }

    private async Task CloseCacheAsync(ClientTerrainCache? cache, DerivedTerrain? derived)
    {
        try
        {
            if (cache is not null && !await cache.CloseAsync().ConfigureAwait(true))
            {
                ClientTerrainLog.CacheCloseTimedOut(_logger);
            }

            if (derived is not null)
            {
                // Rebuildable data: a timeout leaves the writer to finish alone.
                await Horyzont.Game.Server.TerrainFailureCleanup.DisposeAfterCloseAsync(derived.CloseAsync, derived.Dispose).ConfigureAwait(true);
            }
        }
#pragma warning disable CA1031 // The cache is advisory; a failure to close it is logged and does not stop the shutdown.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            ClientTerrainLog.CacheCloseFailed(_logger, exception);
        }
    }
}
