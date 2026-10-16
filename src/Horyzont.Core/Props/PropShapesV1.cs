namespace Horyzont.Core.Props;

/// <summary>The stand-in body of a prop for hits and picking (K6-31 DO USTALENIA-13): a capsule along "up" from the anchor.</summary>
/// <param name="HeightMeters">Length of the segment from the anchor along "up" at scale 1, metres.</param>
/// <param name="RadiusMeters">Radius of the capsule at scale 1, metres.</param>
public readonly record struct PropShape(float HeightMeters, float RadiusMeters);

/// <summary>The stand-in bodies of the kinds of layout V1. Shared by the validator and the client picker, so both measure alike.</summary>
public static class PropShapesV1
{
    /// <summary>The body of a kind at scale 1.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Its body; the skeleton returns an empty one.</returns>
#pragma warning disable CA1801, IDE0060 // K6-31 B4: the skeleton does not read the kind yet.
    public static PropShape Get(PropKindV1 kind) => default; // K6-31 B4.
#pragma warning restore CA1801, IDE0060
}
