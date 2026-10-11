namespace Horyzont.Core.Simulation;

/// <summary>
/// Gameplay limits. From the specification: 60 Hz, 8 m reach, 0.5–3 m tool radius, 5 terrain edits per second.
/// Assumptions to confirm: block rate, ray origin tolerance, capsule size and eye height (package 11), edit margin
/// (package 9). Constants of the operation semantics (edit margin, edit radius bound) are in <see cref="TerrainRules"/>, not here.
/// </summary>
public sealed record GameRules
{
    /// <summary>Simulation ticks per second.</summary>
    public int TickRate { get; init; } = 60;

    /// <summary>Reach of tools and block placement in metres.</summary>
    public float ReachMeters { get; init; } = 8f;

    /// <summary>Smallest terrain tool radius.</summary>
    public float MinToolRadius { get; init; } = 0.5f;

    /// <summary>Largest terrain tool radius.</summary>
    public float MaxToolRadius { get; init; } = 3f;

    /// <summary>Terrain edits refilled per second.</summary>
    public int TerrainEditsPerSecond { get; init; } = 5;

    /// <summary>Terrain edits available at once.</summary>
    public int TerrainEditBurst { get; init; } = 5;

    /// <summary>Block edits refilled per second.</summary>
    public int BlockEditsPerSecond { get; init; } = 10;

    /// <summary>Block edits available at once.</summary>
    public int BlockEditBurst { get; init; } = 10;

    /// <summary>Largest distance between an intent's ray origin and the player's eye.</summary>
    public float MaxRayOriginErrorMeters { get; init; } = 2f;

    /// <summary>Radius of the player capsule.</summary>
    public float PlayerCapsuleRadius { get; init; } = 0.4f;

    /// <summary>Height of the player capsule.</summary>
    public float PlayerCapsuleHeight { get; init; } = 1.8f;

    /// <summary>Eye height above the feet.</summary>
    public float EyeHeight { get; init; } = 1.62f;

    /// <summary>
    /// Highest terrain above the planet radius, metres. Core does not know the generator; the game sets this from the planet
    /// generator (K5-23). Ship blocks farther from the planet centre than radius + this + 2 m cannot be embedded in terrain.
    /// </summary>
    public float TerrainMaxReliefMeters { get; init; } = 300f;

    /// <summary>Largest distance from the centre of a refuel station cell to the bounding sphere of the ship, metres (K5-24 P3).</summary>
    public float RefuelRangeMeters { get; init; } = 15f;

    /// <summary>Seat, ejection, launch guard and pilot input rules (K5-25).</summary>
    public Ships.SeatRules Seats { get; init; } = Ships.SeatRules.Default;
}
