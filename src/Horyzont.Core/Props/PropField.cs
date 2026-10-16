using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Props;

/// <summary>
/// The props of the planets: the frozen layout of the generator profile placed on the generated surface (K6-31 D7). Used by the
/// validator, the resolvers and the regrowth schedule, never by the applier. Main thread only.
/// </summary>
public sealed class PropField
{
    /// <summary>Creates the field.</summary>
    /// <param name="state">World state (planets, seeds).</param>
    /// <param name="terrain">Terrain service that samples the generated surface.</param>
    public PropField(WorldState state, ITerrainService terrain)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
    }

#pragma warning disable CA1822 // K6-31 B4: the skeleton does not read the instance yet.
    /// <summary>Looks up one prop.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="id">The prop.</param>
    /// <param name="instance">The prop when the layout has it, its height band accepts it and the surface can be sampled.</param>
    /// <returns>True when the prop exists.</returns>
    public bool TryGetProp(PlanetId planet, PropId id, out PropInstance instance)
    {
        instance = default; // K6-31 B4.
        return false;
    }

    /// <summary>Appends the props whose anchor lies in a sphere, ascending by <see cref="PropId"/>.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="center">Centre of the sphere in the planet frame.</param>
    /// <param name="radius">Radius in metres.</param>
    /// <param name="result">The list to append to.</param>
    public void CollectInSphere(PlanetId planet, Vector3 center, float radius, List<PropInstance> result)
    {
        // K6-31 B4.
    }
#pragma warning restore CA1822
}
