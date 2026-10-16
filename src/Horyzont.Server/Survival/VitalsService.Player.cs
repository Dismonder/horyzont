using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Server.Survival;

public sealed partial class VitalsService
{
    /// <summary>
    /// One tick of one player: the fall (the landing of the previous tick into walking, from the velocity of the tick before; forgotten after a change of frame, a seat, frozen or
    /// flying bodies), the costs, the food bonus, <see cref="VitalsStep"/> and the rest.
    /// </summary>
    private void StepPlayer(ServerSimulation sim, PlayerState player, PlayerData data, VitalsMotion motion, VitalsRules rules, int tickRate, Tick now)
    {
        var attached = motion.Attached || player.Seat is not null;
        var steady = !attached && motion.Mode is not (MovementMode.Frozen or MovementMode.DevFlying);
        var continuous = steady && data.HasPrevious && data.PreviousParentSeq == player.ParentSeq;
        var sprinting = !attached && motion.Mode == MovementMode.Sprinting;
        var jumped = continuous && motion.Mode == MovementMode.Jumping && data.PreviousMode != MovementMode.Jumping;
        var damage = data.PendingDamage;
        if (continuous && FallImpact.TryDetect(data.PreviousMode, data.PreviousVelocity, TerrainMath.UpAt(motion.Feet), motion.Mode, out var speed))
        {
            var fall = FallDamage.Compute(speed, rules);
            damage += fall;
            if (fall > 0f)
            {
                _logger.FallDamage(player.Player.Value, fall, speed);
            }
        }

        var extra = data.PendingCost;
        data.PendingDamage = 0f;
        data.PendingCost = 0f;
        data.HasPrevious = steady;
        data.PreviousMode = motion.Mode;
        data.PreviousVelocity = motion.Velocity;
        data.PreviousParentSeq = player.ParentSeq;

        var food = sim.State.Progress.TryGet(player.Player, out var progress) ? progress.Food : default;
        var bonus = FoodBonus.Compute(food, _table!, now, tickRate, rules);
        var current = player.Vitals ?? new PlayerVitals(float.NaN, float.NaN, default, false);
        var input = new VitalsTickInput(sprinting, jumped, extra, damage, data.LastStaminaCost);
        var result = VitalsStep.Advance(current, input, bonus, rules, tickRate, now);
        if (sprinting || jumped || extra > 0f)
        {
            data.LastStaminaCost = now;
        }

        var restedUntil = RestTracker.Update(sim, player, data.Rest, now, current.RestedUntil);
        data.HealthMax = result.HealthMax;
        data.StaminaMax = result.StaminaMax;
        if (!sim.UpdatePlayerVitals(player.Player, result.Vitals with { RestedUntil = restedUntil }))
        {
            _logger.VitalsRefused(player.Player.Value);
        }
    }
}
