using Horyzont.Core.Ids;

namespace Horyzont.Server.Replication;

/// <summary>
/// Interest distances by region and kind (K5-26 decision 1, spec 02:169): in a planet region every entity is seen from
/// <see cref="Planet"/> (500 m, ships too); in the system region ships are seen from <see cref="SystemShip"/> (10 km). Other entities
/// are never in the system frame (K5-20 P6) and keep the planet distance. Distances count from the bounding sphere surface.
/// </summary>
public sealed class ReplicationRanges
{
    /// <summary>Distance in a planet region, in metres.</summary>
    public const float PlanetMeters = 500f;

    /// <summary>Distance of ships in the system region, in metres.</summary>
    public const float SystemShipMeters = 10_000f;

    /// <summary>Initializes a new instance of the <see cref="ReplicationRanges"/> class.</summary>
    /// <param name="planet">Distance of every entity in a planet region.</param>
    /// <param name="systemShip">Distance of ships in the system region.</param>
    public ReplicationRanges(float planet, float systemShip)
    {
        if (!float.IsFinite(planet) || planet < 0 || !float.IsFinite(systemShip) || systemShip < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(planet), "Ranges must be finite and not negative.");
        }

        Planet = planet;
        SystemShip = systemShip;
    }

    /// <summary>Gets the ranges of the specification: 500 m on planets, 10 km for ships in the system.</summary>
    public static ReplicationRanges Default { get; } = new(PlanetMeters, SystemShipMeters);

    /// <summary>Gets the distance of every entity in a planet region.</summary>
    public float Planet { get; }

    /// <summary>Gets the distance of ships in the system region.</summary>
    public float SystemShip { get; }

    /// <summary>One distance for everything (the K3-15 behaviour, tests).</summary>
    /// <param name="range">Distance in metres.</param>
    /// <returns>The ranges.</returns>
    public static ReplicationRanges Uniform(float range) => new(range, range);

    /// <summary>The distance of an entity in a region.</summary>
    /// <param name="region">The region of the entity (a planet or a system frame).</param>
    /// <param name="isShip">Whether the entity is a ship-mode construct.</param>
    /// <returns>The distance in metres.</returns>
    public float For(FrameRef region, bool isShip) => region.Kind == FrameKind.System && isShip ? SystemShip : Planet;
}
