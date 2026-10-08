namespace Horyzont.Core.Ids;

/// <summary>Reference to the parent frame of an entity.</summary>
/// <param name="Kind">Frame kind.</param>
/// <param name="Id">Value of the <see cref="SystemId"/>, <see cref="PlanetId"/> or construct <see cref="EntityId"/>.</param>
public readonly record struct FrameRef(FrameKind Kind, ulong Id)
{
    /// <summary>Frame of a star system.</summary>
    /// <param name="system">The system.</param>
    /// <returns>The frame reference.</returns>
    public static FrameRef Of(SystemId system) => new(FrameKind.System, system.Value);

    /// <summary>Frame of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The frame reference.</returns>
    public static FrameRef Of(PlanetId planet) => new(FrameKind.Planet, planet.Value);

    /// <summary>Frame of a construct.</summary>
    /// <param name="construct">The construct entity.</param>
    /// <returns>The frame reference.</returns>
    public static FrameRef Of(EntityId construct) => new(FrameKind.Construct, construct.Value);
}
