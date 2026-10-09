using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client;

/// <summary>
/// Terrain of a remote client: one <see cref="PlanetTerrain"/> per planet of the server hello, under the given parent node
/// (<c>/Root/Client/Terrain</c>), from the planet factory with a <see cref="GeneratorOnlyBlockSource"/> stream. Edited terrain
/// arrives with K3-16.
/// </summary>
public sealed class OwnTerrainProvider : IClientTerrainProvider, IDisposable
{
    private readonly Node _parent;
    private readonly PlanetTerrainOptions _options;
    private readonly Material? _material;
    private readonly ILogger _logger;
    private readonly GeneratorOnlyBlockSource _source = new();
    private readonly TerrainBlockCodec _codec = new();
    private readonly SortedDictionary<ulong, PlanetTerrain> _planets = [];

    /// <summary>Creates the provider.</summary>
    /// <param name="parent">Node the planet terrains are added to.</param>
    /// <param name="options">Active parts of the terrain nodes.</param>
    /// <param name="material">Render material shared by the planets; null without rendering.</param>
    /// <param name="logger">Logger of the streams.</param>
    public OwnTerrainProvider(Node parent, PlanetTerrainOptions options, Material? material, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(logger);
        _parent = parent;
        _options = options;
        _material = material;
        _logger = logger;
    }

    /// <summary>Gets the number of planet terrains this provider holds.</summary>
    public int PlanetCount => _planets.Count;

    /// <inheritdoc />
    public void Prepare(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        Clear();
        foreach (var planet in hello.Planets)
        {
            var terrain = PlanetTerrainFactory.Create(planet, _options, _material);
            var stream = new TerrainStream(planet.Id, _source, _codec, new DerivedLodCache(), _logger);
            terrain.Stream = stream;
            var node = new PlanetTerrain(planet.Id, terrain, stream);
            _planets.Add(planet.Id.Value, node);
            _parent.AddChild(node);
        }
    }

    /// <inheritdoc />
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? terrain) => _planets.TryGetValue(planet.Value, out terrain);

    /// <inheritdoc />
    public void Dispose() => Clear();

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
    }
}
