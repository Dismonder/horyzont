using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Server.Survival;

public sealed partial class VitalsService
{
    /// <summary>
    /// The suit of one player for one tick (K6-34 D17): the capacity from the modules in the equipment, the surroundings from the tracker, the protection, <see cref="SuitStep"/>. The suit is read from
    /// <c>player.Vitals</c> here, in the call that goes on to write it with <see cref="ServerSimulation.UpdatePlayerVitals"/>: an operation 42 accepted between two steps is part of the value, never overwritten
    /// by one computed earlier (review B5, P2-2). What the message of this tick shows (the suit, its capacity, the environment) is kept from the same step (P2-1).
    /// </summary>
    /// <returns>The suit after the tick, or null when the world has no hazard content.</returns>
    private SuitResources? StepSuit(ServerSimulation sim, PlayerState player, PlayerData data, int tickRate, Tick now, ref float damage)
    {
        if (_hazards is null || _modules is null)
        {
            return null;
        }

        var rules = sim.HazardRules;
        var equipment = sim.State.Containers.TryGetOfPlayer(player.Player, ContainerKind.Equipment, out var worn) ? worn.Slots : default;
        var stats = SuitLoadout.Compute(equipment, _modules, rules);
        var current = player.Vitals?.Suit ?? stats.Capacity;
        var sample = _hazards.Sample(player, now);
        var effective = HazardProtection.Apply(sample.Present, sample.Sheltered, sample.Generator, sample.LifeSupport);
        var result = SuitStep.Advance(current, effective, stats, sample.Sheltered || sample.Generator || sample.LifeSupport, rules, tickRate);
        damage += result.Damage;

        data.HasSuit = true;
        data.Suit = result.Next;
        data.Capacity = stats.Capacity;
        data.Environment = new VitalsEnvironment(
            sample.Sheltered, sample.Generator, sample.LifeSupport, Mask(sample.Present), Mask(effective), result.ExposedMask, sample.ShelterHits);
        return result.Next;
    }

    // Bit n - 1 is set when the hazard n (cold, heat, toxins, radiation, vacuum) has a rate above zero.
    private static byte Mask(in HazardRates rates) =>
        (byte)((rates.Cold > 0f ? 1 : 0) | (rates.Heat > 0f ? 2 : 0) | (rates.Toxins > 0f ? 4 : 0) | (rates.Radiation > 0f ? 8 : 0) | (rates.Vacuum > 0f ? 16 : 0));

    private static SuitResources Clamp(in SuitResources value, in SuitResources capacity) => new(
        MathF.Min(value.Heating, capacity.Heating), MathF.Min(value.Cooling, capacity.Cooling), MathF.Min(value.Filter, capacity.Filter),
        MathF.Min(value.Shield, capacity.Shield), MathF.Min(value.Oxygen, capacity.Oxygen));
}
