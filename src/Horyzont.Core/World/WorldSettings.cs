namespace Horyzont.Core.World;

/// <summary>
/// The settings of a world that change the rules of play (K6-35 D4; spec 03 "Ustawienia świata"). One record in format version 1, so the packages that read
/// a field (K6-36, K6-37, K6-39) never bump the format one by one. K6-35 itself reads only <see cref="DeathPenalty"/>.
/// </summary>
public sealed record WorldSettings
{
    /// <summary>Lowest <see cref="ResourceMultiplierPercent"/>.</summary>
    public const ushort MinResourcePercent = 50;

    /// <summary>Highest <see cref="ResourceMultiplierPercent"/>.</summary>
    public const ushort MaxResourcePercent = 300;

    /// <summary>Lowest <see cref="StackMultiplierPercent"/>.</summary>
    public const ushort MinStackPercent = 100;

    /// <summary>Highest <see cref="StackMultiplierPercent"/>.</summary>
    public const ushort MaxStackPercent = 300;

    /// <summary>Lowest <see cref="HazardIntensityPercent"/>.</summary>
    public const ushort MinHazardPercent = 50;

    /// <summary>Highest <see cref="HazardIntensityPercent"/>.</summary>
    public const ushort MaxHazardPercent = 200;

    /// <summary>Gets the settings of a new world: every value at its default.</summary>
    public static WorldSettings Default { get; } = new();

    /// <summary>Gets a value indicating whether every teleport is allowed (K6-37).</summary>
    public bool TeleportAll { get; init; }

    /// <summary>Gets a value indicating whether ships burn no fuel (K6-37).</summary>
    public bool FreeFuel { get; init; }

    /// <summary>Gets a value indicating whether building costs nothing (K6-36).</summary>
    public bool FreeBuilding { get; init; }

    /// <summary>Gets the multiplier of the mining yield in percent, 50 to 300.</summary>
    public ushort ResourceMultiplierPercent { get; init; } = 100;

    /// <summary>Gets the multiplier of the stack sizes in percent, 100 to 300.</summary>
    public ushort StackMultiplierPercent { get; init; } = 100;

    /// <summary>Gets the intensity of the hazards in percent, 50 to 200.</summary>
    public ushort HazardIntensityPercent { get; init; } = 100;

    /// <summary>Gets the strictness of the building integrity.</summary>
    public BuildingIntegrityMode BuildingIntegrity { get; init; } = BuildingIntegrityMode.On;

    /// <summary>Gets the penalty of a death.</summary>
    public DeathPenalty DeathPenalty { get; init; } = DeathPenalty.Normal;

    /// <summary>Gets a value indicating whether every value is within its range and every enum is defined.</summary>
    public bool IsValid =>
        ResourceMultiplierPercent is >= MinResourcePercent and <= MaxResourcePercent
        && StackMultiplierPercent is >= MinStackPercent and <= MaxStackPercent
        && HazardIntensityPercent is >= MinHazardPercent and <= MaxHazardPercent
        && Enum.IsDefined(BuildingIntegrity) && Enum.IsDefined(DeathPenalty);

    /// <summary>Gets a value indicating whether the settings equal <see cref="Default"/>; such settings are neither hashed nor stored.</summary>
    public bool IsDefault => this == Default;
}
