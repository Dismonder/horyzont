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
    /// <returns>Its body; empty for <see cref="PropKindV1.None"/> and values outside the enum.</returns>
    public static PropShape Get(PropKindV1 kind) => kind switch
    {
        PropKindV1.Pine => new PropShape(9f, 0.35f),
        PropKindV1.PineTall => new PropShape(14f, 0.45f),
        PropKindV1.Broadleaf or PropKindV1.BroadleafAutumn => new PropShape(8f, 0.4f),
        PropKindV1.Bush => new PropShape(1.2f, 0.5f),
        PropKindV1.Rock => new PropShape(0.6f, 0.5f),
        PropKindV1.Boulder => new PropShape(1.4f, 1.2f),
        PropKindV1.Crystals => new PropShape(1.0f, 0.6f),
        _ => default,
    };
}
