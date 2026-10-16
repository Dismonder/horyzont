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

    /// <summary>Marker intents refilled per second (K5-27).</summary>
    public int MarkerIntentsPerSecond { get; init; } = 4;

    /// <summary>Marker intents available at once (K5-27).</summary>
    public int MarkerIntentBurst { get; init; } = 8;
    /// <summary>Item intents (move, drop) refilled per second (K6-29W D19).</summary>
    public int ItemIntentsPerSecond { get; init; } = 20;

    /// <summary>Item intents available at once (K6-29W D19).</summary>
    public int ItemIntentBurst { get; init; } = 40;

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

    /// <summary>Most cells a door switch toggles together along the Y axis of a construct (K6-30 D6).</summary>
    public int MaxDoorColumnCells { get; init; } = 3;

    /// <summary>Largest eye distance to a chest for opening it (subscribing), metres (K6-30 D9).</summary>
    public float ContainerOpenRangeMeters { get; init; } = 6f;

    /// <summary>Largest eye distance at which an open chest stays open and takes item intents, metres (K6-30 D9).</summary>
    public float ContainerKeepRangeMeters { get; init; } = 8f;

    /// <summary>Largest eye distance to a chest that takes part in a quick stack, metres (K6-30 D9).</summary>
    public float QuickStackRangeMeters { get; init; } = 10f;
}
