using Horyzont.Core.Ids;

namespace Horyzont.Client.Navigation;

/// <summary>What a navigation target is (K5-27 decision 10).</summary>
public enum NavigationTargetKind : byte
{
    /// <summary>A map marker.</summary>
    Marker = 1,

    /// <summary>The centre of a planet or moon.</summary>
    Body = 2,

    /// <summary>A replicated entity: a player or a ship.</summary>
    Entity = 3,
}

/// <summary>The target of the navigation guidance: a marker, a body or an entity.</summary>
/// <param name="Kind">What the target is.</param>
/// <param name="Id">Marker, planet or entity id, by <paramref name="Kind"/>.</param>
public readonly record struct NavigationTarget(NavigationTargetKind Kind, ulong Id)
{
    /// <summary>Creates a marker target.</summary>
    /// <param name="marker">The marker.</param>
    /// <returns>The target.</returns>
    public static NavigationTarget ForMarker(MarkerId marker) => new(NavigationTargetKind.Marker, marker.Value);

    /// <summary>Creates a target at the centre of a body.</summary>
    /// <param name="planet">The planet or moon.</param>
    /// <returns>The target.</returns>
    public static NavigationTarget ForBody(PlanetId planet) => new(NavigationTargetKind.Body, planet.Value);

    /// <summary>Creates an entity target.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The target.</returns>
    public static NavigationTarget ForEntity(EntityId entity) => new(NavigationTargetKind.Entity, entity.Value);
}
