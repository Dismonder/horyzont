using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;

namespace Horyzont.Testing;

/// <summary>
/// Message factories for tests that do not care about the K6-35 epoch and respawn tails (review B, P2-2). The production constructors that defaulted these
/// fields were removed so that a server path cannot send a neutral epoch or <see cref="PlayerRespawnState.None"/> by accident.
/// </summary>
public static class TestMessages
{
    /// <summary>A vitals message with tick 0 and counter 0 (the neutral epoch).</summary>
    public static PlayerVitalsMessage Vitals(
        float health, float healthMax, float stamina, float staminaMax, bool exhausted, bool restConditionsMet, bool rested, ushort restWarmupTicks, uint restRemainingTicks, byte comfort,
        VitalsEnvironment environment, SuitResources suit, SuitResources capacity) =>
        new(health, healthMax, stamina, staminaMax, exhausted, restConditionsMet, rested, restWarmupTicks, restRemainingTicks, comfort, environment, suit, capacity, default, 0);

    /// <summary>A vitals message without hazard content: no environment, the suit and the capacities at <see cref="PlayerVitalsMessage.DefaultSuit"/>.</summary>
    public static PlayerVitalsMessage Vitals(
        float health, float healthMax, float stamina, float staminaMax, bool exhausted, bool restConditionsMet, bool rested, ushort restWarmupTicks, uint restRemainingTicks, byte comfort) =>
        Vitals(
            health, healthMax, stamina, staminaMax, exhausted, restConditionsMet, rested, restWarmupTicks, restRemainingTicks, comfort,
            VitalsEnvironment.None, PlayerVitalsMessage.DefaultSuit, PlayerVitalsMessage.DefaultSuit);

    /// <summary>A progress state with no bed, no tombstones and no transition.</summary>
    public static PlayerProgressState Progress(ItemTypeBitSet discovered, IReadOnlyList<string> worldProgressKeys, FoodSlots food) =>
        new(discovered, worldProgressKeys, food, PlayerRespawnState.None);
}
