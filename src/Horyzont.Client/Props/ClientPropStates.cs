#pragma warning disable CA1822 // Skeleton of step D1 (K6-31); removed with the implementation of D2.
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;

namespace Horyzont.Client.Props;

/// <summary>
/// The harvested props of a remote client (K6-31 D17, D18). Skeleton of step D1: the behaviour comes with D2.
/// </summary>
public sealed class ClientPropStates : IPropStateView
{
    /// <summary>Distance from the body beyond which a region is forgotten, in metres; always larger than the leaving distance of the server.</summary>
    public const float DropMeters = 768f;

    /// <inheritdoc />
    public event Action<PlanetId, PropRegionKey>? RegionChanged;

    /// <summary>Gets the number of regions the client holds a state for.</summary>
    public int RegionCount => 0;

    /// <inheritdoc />
    public bool IsHarvested(PlanetId planet, PropId prop) => false;

    /// <summary>Replaces the state of every region of a message.</summary>
    /// <param name="message">Message 45.</param>
    public void Apply(PropStates message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _ = RegionChanged;
    }

    /// <summary>Forgets the regions farther than <see cref="DropMeters"/> from the body.</summary>
    /// <param name="planet">The planet the body stands on.</param>
    /// <param name="feetInPlanet">The feet in the frame of the planet.</param>
    public void Forget(PlanetParams planet, Vector3 feetInPlanet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        _ = feetInPlanet;
    }

    /// <summary>Forgets everything (disconnect).</summary>
    public void Clear()
    {
    }
}
