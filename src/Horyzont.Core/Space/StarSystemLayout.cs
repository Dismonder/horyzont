namespace Horyzont.Core.Space;

/// <summary>
/// A frozen version of the star system generation (K5-21 decision 3). The migration M0002 backfills old worlds with
/// <see cref="V1"/>, so a version never changes once merged: a different system is a new version next to it.
/// </summary>
public sealed class StarSystemLayout
{
    private StarSystemLayout(byte version, double maxElevationDegrees, IReadOnlyList<BodyLayout> bodies)
    {
        Version = version;
        MaxElevationDegrees = maxElevationDegrees;
        Bodies = bodies;
    }

    /// <summary>Gets layout version 1: moon, planet B and planet C around the home planet (spec 02:43–48).</summary>
    public static StarSystemLayout V1 { get; } = new(1, 5, [
        new BodyLayout(1400, 1600, 162, 162, 600, 150, 17_500, 18_500),
        new BodyLayout(3000, 3500, 750, 900, 1000, 300, 48_000, 52_000),
        new BodyLayout(4500, 5000, 1050, 1200, 1500, 500, 108_000, 110_000),
    ]);

    /// <summary>Gets the version written to <see cref="StarSystemParams.LayoutVersion"/>.</summary>
    public byte Version { get; }

    /// <summary>Gets the largest elevation of a body above or below the XZ plane, in degrees.</summary>
    public double MaxElevationDegrees { get; }

    /// <summary>Gets the generated bodies 1..n (body 0 is the given home planet).</summary>
    public IReadOnlyList<BodyLayout> Bodies { get; }
}
