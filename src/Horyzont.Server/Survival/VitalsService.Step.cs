using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Server.Survival;

public sealed partial class VitalsService
{
    /// <summary>Advances the vitals of every player online with a body by one tick and sends the messages that are due.</summary>
    public void Step()
    {
        if (_simulation() is not { Survival: not null } sim || _isReadOnly())
        {
            return;
        }

        if (!ReferenceEquals(_tableFor, sim))
        {
            _table = FoodTable.Build(sim.Survival, sim.State.ItemTypes);
            _modules = sim.Survival.HasHazards ? SuitModuleTable.Build(sim.Survival, sim.State.ItemTypes) : null;
            _hazards = sim.Survival.HasHazards ? new HazardTracker(sim) : null;
            if (_hazards is { IntensityAdjusted: true })
            {
                _logger.HazardIntensityAdjusted(_hazards.RawIntensity, _hazards.Intensity);
            }

            _tableFor = sim;
        }

        var rules = sim.VitalsRules;
        var tickRate = sim.Rules.TickRate;
        var now = sim.State.CurrentTick;
        var sendNow = now.Value % (ulong)Math.Max(rules.VitalsSendEveryTicks, 1) == 0;
        var players = sim.State.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!player.IsOnline || !_motion.TryGetMotion(player.Player, out var motion))
            {
                continue;
            }

            var data = Data(player.Player);
            StepPlayer(sim, player, data, motion, rules, tickRate, now);
            if (sendNow && data.Ready && player.Vitals is { } vitals)
            {
                _send(player.Player, Build(vitals, data, now));
                MessagesSent++;
            }
        }
    }

    private static PlayerVitalsMessage Build(PlayerVitals vitals, PlayerData data, Tick now)
    {
        var remaining = vitals.RestedUntil > now ? Math.Min(vitals.RestedUntil.Value - now.Value, (ulong)ProtocolConstants.MaxRestRemainingTicks) : 0UL;
        var met = data.Rest.ConditionsMet;
        var warmup = met ? (ushort)Math.Min(data.Rest.WarmupTicks, (ulong)ProtocolConstants.MaxRestWarmupTicks) : (ushort)0;
        var comfort = (byte)Math.Min((int)data.Rest.Comfort, ProtocolConstants.MaxRestComfort);

        // The suit and its capacity come from the same step, and the value never exceeds the capacity of that step: the codec refuses a value above its capacity (review B5, P2-1).
        return data.HasSuit
            ? new PlayerVitalsMessage(
                vitals.Health, data.HealthMax, vitals.Stamina, data.StaminaMax, vitals.Exhausted, met, remaining > 0, warmup, (uint)remaining, comfort,
                data.Environment, Clamp(data.Suit, data.Capacity), data.Capacity)
            : new PlayerVitalsMessage(vitals.Health, data.HealthMax, vitals.Stamina, data.StaminaMax, vitals.Exhausted, met, remaining > 0, warmup, (uint)remaining, comfort);
    }

    private bool IsOnline(PlayerId player) =>
        _simulation() is { } sim && sim.State.Players.TryGet(player, out var state) && state.IsOnline;

    private PlayerData Data(PlayerId player)
    {
        if (!_data.TryGetValue(player, out var data))
        {
            data = new PlayerData();
            _data[player] = data;
        }

        return data;
    }

    private sealed class PlayerData
    {
        public bool Ready { get; set; }

        public bool HasPrevious { get; set; }

        public MovementMode PreviousMode { get; set; }

        public Vector3 PreviousVelocity { get; set; }

        /// <summary>The largest downward speed of the current fall (m/s), 0 on the ground; the engine zeroes the velocity in the tick of the contact, so the impact is read from here.</summary>
        public float PeakFallSpeed { get; set; }

        public uint PreviousParentSeq { get; set; }

        public float PendingDamage { get; set; }

        public float PendingCost { get; set; }

        public Tick LastStaminaCost { get; set; }

        public float HealthMax { get; set; }

        public float StaminaMax { get; set; }

        /// <summary>Gets or sets a value indicating whether the suit was stepped (the world has hazard content), so the fields below belong to the last step.</summary>
        public bool HasSuit { get; set; }

        public SuitResources Suit { get; set; }

        public SuitResources Capacity { get; set; }

        public VitalsEnvironment Environment { get; set; }

        public RestState Rest { get; } = new();
    }
}
