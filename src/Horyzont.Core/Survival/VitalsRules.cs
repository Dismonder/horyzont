namespace Horyzont.Core.Survival;

/// <summary>
/// Constants of the character vitals (K6-33, D13 to D18). Vitals are outside the journal (the applier never reads them), so the values are plain
/// <c>init</c> properties like <c>MovementRules</c>; client and server use the same ones because the build hash in the handshake forces the same build.
/// </summary>
public sealed record VitalsRules
{
    /// <summary>Gets the default rules.</summary>
    public static VitalsRules Default { get; } = new();

    /// <summary>Health without food.</summary>
    public float BaseHealth { get; init; } = 100f;

    /// <summary>Stamina without food.</summary>
    public float BaseStamina { get; init; } = 100f;

    /// <summary>Health regeneration per second without food.</summary>
    public float BaseHealthRegenPerSecond { get; init; } = 0.5f;

    /// <summary>Stamina regeneration per second.</summary>
    public float StaminaRegenPerSecond { get; init; } = 15f;

    /// <summary>Ticks without a stamina cost before stamina regenerates.</summary>
    public int StaminaRegenDelayTicks { get; init; } = 60;

    /// <summary>Stamina cost of sprinting per second.</summary>
    public float SprintStaminaPerSecond { get; init; } = 8f;

    /// <summary>Stamina cost of one jump.</summary>
    public float JumpStamina { get; init; } = 10f;

    /// <summary>Stamina cost of one use of a tool.</summary>
    public float ToolUseStamina { get; init; } = 4f;

    /// <summary>The exhausted player recovers when stamina reaches this fraction of the maximum.</summary>
    public float ExhaustedRecoverFraction { get; init; } = 0.2f;

    /// <summary>Health stays at least this high until death exists (K6-35).</summary>
    public float MinHealthWithoutDeath { get; init; } = 1f;

    /// <summary>The same food can be eaten again when no more than this fraction of its time remains.</summary>
    public double FoodRepeatFraction { get; init; } = 0.2;

    /// <summary>Exponent of the food curve: strength = remaining fraction to this power.</summary>
    public double FoodCurveExponent { get; init; } = 0.3;

    /// <summary>Ticks of uninterrupted resting conditions before the rested bonus starts.</summary>
    public int RestWarmupTicks { get; init; } = 1200;

    /// <summary>Minutes the rested bonus lasts at comfort 0.</summary>
    public int RestBaseMinutes { get; init; } = 8;

    /// <summary>Extra minutes of the rested bonus per comfort level.</summary>
    public int RestMinutesPerComfort { get; init; } = 1;

    /// <summary>Extra stamina regeneration while rested, as a fraction.</summary>
    public float RestStaminaRegenBonus { get; init; } = 0.5f;

    /// <summary>Highest comfort level.</summary>
    public int MaxComfort { get; init; } = 10;

    /// <summary>Radius in metres in which blocks give comfort.</summary>
    public float RestComfortRangeMeters { get; init; } = 10f;

    /// <summary>Radius in metres in which a heat source counts.</summary>
    public float RestHeatRangeMeters { get; init; } = 5f;

    /// <summary>Length in metres of the roof ray.</summary>
    public float RestRoofRayMeters { get; init; } = 16f;

    /// <summary>Largest distance in metres from the eye to the bed cell for lying down.</summary>
    public float RestBedRangeMeters { get; init; } = 3f;

    /// <summary>Largest distance in metres a lying player may move.</summary>
    public float RestStillnessMeters { get; init; } = 0.5f;

    /// <summary>Ticks between two evaluations of the resting conditions.</summary>
    public int RestEvaluateEveryTicks { get; init; } = 30;

    /// <summary>Impact speed in m/s up to which a fall does no damage.</summary>
    public float FallSafeSpeed { get; init; } = 11f;

    /// <summary>Damage per m/s of impact speed above <see cref="FallSafeSpeed"/>.</summary>
    public float FallDamagePerMetrePerSecond { get; init; } = 8f;

    /// <summary>Ticks between two <c>PlayerVitals</c> messages (10 Hz at 60 ticks per second).</summary>
    public int VitalsSendEveryTicks { get; init; } = 6;
}
