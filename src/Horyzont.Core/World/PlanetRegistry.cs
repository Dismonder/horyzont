using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World;

/// <summary>Planets of a world; fixed after the world is created or loaded.</summary>
public sealed class PlanetRegistry
{
    private readonly Dictionary<PlanetId, PlanetParams> _byId = [];
    private readonly List<PlanetParams> _sorted = [];

    internal PlanetRegistry()
    {
    }

    /// <summary>All planets in <see cref="PlanetId"/> order.</summary>
    public IReadOnlyList<PlanetParams> All => _sorted;

    /// <summary>Looks up a planet.</summary>
    /// <param name="id">Planet id.</param>
    /// <param name="planet">Its parameters when found.</param>
    /// <returns>True when the planet exists.</returns>
    public bool TryGet(PlanetId id, [NotNullWhen(true)] out PlanetParams? planet) => _byId.TryGetValue(id, out planet);

    internal void Add(PlanetParams planet)
    {
        planet.Validate();
        if (!_byId.TryAdd(planet.Id, planet))
        {
            throw new InvalidDataException($"Duplicate planet {planet.Id.Value}.");
        }

        var index = _sorted.Count;
        while (index > 0 && _sorted[index - 1].Id.Value > planet.Id.Value)
        {
            index--;
        }

        _sorted.Insert(index, planet);
    }
}
