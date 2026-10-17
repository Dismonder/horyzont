using Horyzont.Core.Survival;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// The health and stamina of the receiving player and the state of the rest (message 44, K6-33 D22); unreliable, server to the owner only, about 10 times a
/// second. Format 1: four <c>f32</c> (health, health maximum, stamina, stamina maximum; finite, 0 to <c>ProtocolConstants.MaxVitalValue</c>), a flag byte
/// (bit 0 exhausted, bit 1 the conditions of rest hold, bit 2 rested; other bits are an error), a <c>u16</c> progress of the warm-up in ticks (0 to 1200),
/// a varuint of the remaining ticks of the rest (0 to 64 800) and a comfort byte (0 to 10). Two invariants hold in both directions: <c>Rested</c> is true exactly when <c>RestRemainingTicks</c> is above 0, and a
/// warm-up above 0 needs <c>RestConditionsMet</c> (the warm-up counts only while the conditions hold without a break).
/// </summary>
/// <param name="Health">Current health.</param>
/// <param name="HealthMax">Maximum health.</param>
/// <param name="Stamina">Current stamina.</param>
/// <param name="StaminaMax">Maximum stamina.</param>
/// <param name="Exhausted">Whether the player is exhausted (cannot sprint or jump).</param>
/// <param name="RestConditionsMet">Whether the player is resting under a roof by a heat source (the warm-up is running).</param>
/// <param name="Rested">Whether the player is rested (the stamina regenerates faster).</param>
/// <param name="RestWarmupTicks">Progress of the warm-up in ticks, 0 to 1200.</param>
/// <param name="RestRemainingTicks">Remaining ticks of the rest, 0 to 64 800.</param>
/// <param name="Comfort">Comfort level of the surroundings, 0 to 10.</param>
/// <param name="Environment">The protections and the hazards around the player (K6-34 tail, D18).</param>
/// <param name="Suit">The resources of the suit, each 0 to its capacity (K6-34 tail).</param>
/// <param name="Capacity">The capacities of the resources, each 1 to <c>ProtocolConstants.MaxSuitValue</c> (K6-34 tail).</param>
public sealed record PlayerVitalsMessage(
    float Health,
    float HealthMax,
    float Stamina,
    float StaminaMax,
    bool Exhausted,
    bool RestConditionsMet,
    bool Rested,
    ushort RestWarmupTicks,
    uint RestRemainingTicks,
    byte Comfort,
    VitalsEnvironment Environment,
    SuitResources Suit,
    SuitResources Capacity) : Message
{
    /// <summary>The suit of a server without hazard content: every resource full at the base capacity (D18).</summary>
    public static readonly SuitResources DefaultSuit = new(100f, 100f, 100f, 100f, 100f);

    /// <summary>Creates the message of a server without hazard content: no environment, the suit and the capacities at <see cref="DefaultSuit"/>.</summary>
    public PlayerVitalsMessage(
        float health, float healthMax, float stamina, float staminaMax, bool exhausted, bool restConditionsMet, bool rested, ushort restWarmupTicks, uint restRemainingTicks, byte comfort)
        : this(health, healthMax, stamina, staminaMax, exhausted, restConditionsMet, rested, restWarmupTicks, restRemainingTicks, comfort, VitalsEnvironment.None, DefaultSuit, DefaultSuit)
    {
    }

    /// <inheritdoc />
    public override MessageType Type => MessageType.PlayerVitals;
}
