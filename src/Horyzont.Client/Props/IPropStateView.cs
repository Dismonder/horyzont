using Horyzont.Core.Ids;

namespace Horyzont.Client.Props;

/// <summary>
/// Read-only view of the harvested props the local player may look at (K6-31 D18). A remote client reads <see cref="ClientPropStates"/>, a
/// host and a solo player read the world of the server in the same process; both raise <see cref="RegionChanged"/> for the drawing.
/// </summary>
public interface IPropStateView
{
    /// <summary>Raised after the state of a region changed (an object was harvested or regrew).</summary>
    event Action<PlanetId, PropRegionKey>? RegionChanged;

    /// <summary>Tells whether a prop is harvested.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="prop">The prop.</param>
    /// <returns>True when the prop is known to be harvested.</returns>
    bool IsHarvested(PlanetId planet, PropId prop);
}
