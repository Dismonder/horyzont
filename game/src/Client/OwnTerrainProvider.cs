using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Planet;
using Horyzont.Game.Client.Terrain;
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

    /// <summary>Gets the envelope codec of the terrain (its scratch buffers are per thread).</summary>
    public TerrainBlockCodec Codec => _codec;

    /// <summary>Gets the terrain node of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="node">The node when the planet has a terrain.</param>
    /// <returns>Whether the planet has a terrain.</returns>
    public bool TryGetNode(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? node)
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
        foreach (var planet in hello.Planets)
        {
            var terrain = PlanetTerrainFactory.Create(planet, _options, _material);
            var stream = new TerrainStream(planet.Id, source, _codec, new DerivedLodCache(), _logger);
            terrain.Stream = stream;
            var node = new PlanetTerrain(planet.Id, terrain, stream);
            _planets.Add(planet.Id.Value, node);
            _parent.AddChild(node);
        }
    }

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out IClientTerrainView? view)
    {
        _planets.TryGetValue(planet.Value, out var terrain);
        return _views.TryViewOf(planet, terrain, out view);
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
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new ClientTerrainCache(path, FingerprintOf(hello, settings.BuildHash), settings.Time, settings.Loggers.CreateLogger<ClientTerrainCache>());
    }

    private void Clear()
    {
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
        if (Cache is { } cache)
        {
            // The writer saves what is queued and the file closes only when nothing reads it any more (five seconds at most).
            Cache = null;
            _ = CloseCacheAsync(cache);
        }
    }

    private async Task CloseCacheAsync(ClientTerrainCache cache)
    {
        try
        {
            if (!await cache.CloseAsync().ConfigureAwait(true))
            {
                ClientTerrainLog.CacheCloseTimedOut(_logger);
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
