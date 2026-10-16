using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Server.Survival;

/// <summary>
/// The rest of the players on the server (K6-33 D14): the posture (a seat, or lying after an accepted <see cref="BlockInteractAction.Sleep"/> close to the own bed
/// and still), the evaluation of the surroundings every <see cref="VitalsRules.RestEvaluateEveryTicks"/> ticks with <see cref="RestEnvironment"/>, the warm-up of
/// <see cref="VitalsRules.RestWarmupTicks"/> ticks of unbroken conditions and the renewal of the end of the rest. Main thread only; nothing here changes the world.
/// </summary>
internal static class RestTracker
{
    /// <summary>Starts the lying of a player when the bed is the own bed of the player and the eye is within <see cref="VitalsRules.RestBedRangeMeters"/> of its centre.</summary>
    /// <param name="sim">The simulation.</param>
    /// <param name="player">The player.</param>
    /// <param name="intent">The accepted sleep intent.</param>
    /// <param name="rest">The rest of the player in memory.</param>
    /// <returns>Whether the player lies now.</returns>
    public static bool TryBeginLying(ServerSimulation sim, PlayerState player, BlockInteractIntent intent, RestState rest)
    {
        var state = sim.State;
        if (!state.BlockEntities.TryGet(new BlockRef(intent.Construct, intent.Cell), out var entity) || entity is not BedBlockState bed || bed.Owner != player.Player)
        {
            return false;
        }

        if (!ContainerReach.TryCellCenter(state, intent.Construct, intent.Cell, out var planet, out var centre) || player.Frame != FrameRef.Of(planet))
        {
            return false;
        }

        if (Vector3.Distance(player.EyePosition(sim.Rules), centre) > sim.VitalsRules.RestBedRangeMeters)
        {
            return false;
        }

        rest.Lying = true;
        rest.LyingConstruct = intent.Construct;
        rest.LyingCell = intent.Cell;
        rest.LyingFeet = player.Position;
        rest.LyingParentSeq = player.ParentSeq;
        rest.Clear();
        return true;
    }

    /// <summary>Follows the rest of a player for one tick.</summary>
    /// <param name="sim">The simulation.</param>
    /// <param name="player">The player.</param>
    /// <param name="rest">The rest of the player in memory.</param>
    /// <param name="now">The current tick.</param>
    /// <param name="restedUntil">The end of the rest so far.</param>
    /// <returns>The end of the rest after this tick; it only grows.</returns>
    public static Tick Update(ServerSimulation sim, PlayerState player, RestState rest, Tick now, Tick restedUntil)
    {
        var rules = sim.VitalsRules;
        if (rest.Lying && !StillLying(sim, player, rest, rules))
        {
            rest.Lying = false;
        }

        EntityId construct;
        CellPos cell;
        if (rest.Lying)
        {
            construct = rest.LyingConstruct;
            cell = rest.LyingCell;
        }
        else if (player.Seat is { } seat && player.Frame.Kind == FrameKind.Construct)
        {
            construct = new EntityId(player.Frame.Id);
            cell = seat;
        }
        else
        {
            rest.Clear();
            return restedUntil;
        }

        if (!rest.Active)
        {
            rest.Active = true;
            rest.WasMet = false;
            rest.WarmupTicks = 0;
            rest.NextEvaluation = now;
        }

        if (now < rest.NextEvaluation)
        {
            return restedUntil;
        }

        var met = false;
        byte comfort = 0;
        if (ContainerReach.TryCellCenter(sim.State, construct, cell, out var planet, out var centre))
        {
            var point = centre + (TerrainMath.UpAt(centre) * 0.5f);
            var conditions = RestEnvironment.Evaluate(sim.State, sim.Content, sim.Terrain, planet, point, rules);
            met = conditions.Roofed && conditions.Heated;
            comfort = (byte)Math.Min((int)conditions.Comfort, ProtocolConstants.MaxRestComfort);
        }

        var elapsed = rest.WasMet && rest.LastEvaluation.Value <= now.Value ? now.Value - rest.LastEvaluation.Value : 0UL;
        rest.WarmupTicks = met && rest.WasMet ? rest.WarmupTicks + elapsed : 0UL;
        rest.WasMet = met;
        rest.ConditionsMet = met;
        rest.Comfort = comfort;
        rest.LastEvaluation = now;
        rest.NextEvaluation = new Tick(now.Value + (ulong)Math.Max(rules.RestEvaluateEveryTicks, 1));

        if (met && rest.WarmupTicks >= (ulong)rules.RestWarmupTicks)
        {
            var until = now.Value + RestRules.DurationTicks(comfort, rules, sim.Rules.TickRate);
            if (until > restedUntil.Value)
            {
                return new Tick(until);
            }
        }

        return restedUntil;
    }

    private static bool StillLying(ServerSimulation sim, PlayerState player, RestState rest, VitalsRules rules)
    {
        if (player.ParentSeq != rest.LyingParentSeq || Vector3.Distance(player.Position, rest.LyingFeet) > rules.RestStillnessMeters)
        {
            return false;
        }

        return sim.State.BlockEntities.TryGet(new BlockRef(rest.LyingConstruct, rest.LyingCell), out var entity) && entity is BedBlockState bed && bed.Owner == player.Player;
    }
}
