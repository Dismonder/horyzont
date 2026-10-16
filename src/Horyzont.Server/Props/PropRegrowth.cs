using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Props;

/// <summary>
/// Regrowth of harvested flora as operations of the server (K6-31 D14): every <see cref="GameRules.PropRegrowScanEveryTicks"/> ticks it
/// looks at up to <see cref="GameRules.PropRegrowScanBatch"/> harvested props from a cursor (in the order of the store, wrapping) and
/// regrows those that are due: the content gives a regrow time, the time is up, the LOD 0 terrain block of the anchor was never edited, and
/// no online player and no block of a construct is within <see cref="GameRules.PropRegrowClearanceMeters"/> of the anchor. The props due in
/// one scan regrow in operations of at most <see cref="GameRules.MaxRegrowPerOperation"/>; the schedule only creates the operations, the
/// replay and the checkpoint see their effects (ZASADY 4). Main thread only.
/// </summary>
public sealed class PropRegrowth : IServerTickParticipant
{
    private const float ChunkHalfDiagonal = 13.856406f;

    private static readonly Comparison<HarvestedPropKey> _byKey = static (a, b) =>
    {
        var planet = a.Planet.Value.CompareTo(b.Planet.Value);
        return planet != 0 ? planet : a.Prop.Value.CompareTo(b.Prop.Value);
    };

    private readonly Func<ServerSimulation> _simulation;
    private readonly Func<ITerrainService> _terrain;
    private readonly Func<bool> _isReadOnly;
    private readonly ILogger<PropRegrowth> _logger;
    private readonly List<HarvestedPropKey> _due = [];
    private readonly List<TerrainBlockKey> _blocks = [];
    private readonly HashSet<HarvestedPropKey> _reportedUnknown = [];
    private PropField? _field;
    private ulong _cursorPlanet;
    private ulong _cursorProp;
    private bool _scanned;
    private ulong _lastScanTick;

    /// <summary>Initializes a new instance of the <see cref="PropRegrowth"/> class.</summary>
    /// <param name="simulation">The simulation of the running world.</param>
    /// <param name="terrain">The terrain service the simulation samples; asked at the first prop that is due.</param>
    /// <param name="isReadOnly">True when the server must not change the world (safe mode, shutdown).</param>
    /// <param name="logger">Logger.</param>
    public PropRegrowth(Func<ServerSimulation> simulation, Func<ITerrainService> terrain, Func<bool> isReadOnly, ILogger<PropRegrowth> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _terrain = terrain;
        _isReadOnly = isReadOnly;
        _logger = logger;
    }

    /// <summary>Creates the schedule for a server and registers it as a tick participant; call before the world starts.</summary>
    /// <param name="runtime">The server.</param>
    /// <param name="terrain">The terrain service of the simulation.</param>
    /// <param name="loggers">Loggers.</param>
    /// <returns>The schedule.</returns>
    public static PropRegrowth Register(ServerRuntime runtime, Func<ITerrainService> terrain, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(loggers);
        var regrowth = new PropRegrowth(
            () => runtime.Simulation, terrain, () => runtime.RuntimeState != ServerRuntimeState.Running, loggers.CreateLogger<PropRegrowth>());
        runtime.AddTickParticipant(regrowth);
        return regrowth;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Tick();
    }

    /// <summary>One tick of the schedule; scans on every <see cref="GameRules.PropRegrowScanEveryTicks"/>th tick.</summary>
    public void Tick()
    {
        var sim = _simulation();
        var tick = sim.State.CurrentTick.Value;
        var every = (ulong)Math.Max(1, sim.Rules.PropRegrowScanEveryTicks);
        if (tick % every != 0 || (_scanned && _lastScanTick == tick) || sim.Survival is not { Props.Count: > 0 } content
            || sim.State.HarvestedProps.Count == 0 || sim.IsFaulted || _isReadOnly())
        {
            return;
        }

        _scanned = true;
        _lastScanTick = tick;
        Scan(sim, content);
    }

    private static int LowerBound(IReadOnlyList<HarvestedPropRecord> sorted, ulong planet, ulong prop)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var record = sorted[middle];
            if (record.Planet.Value < planet || (record.Planet.Value == planet && record.Prop.Value < prop))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static bool PlayerNear(WorldState state, PlanetId planet, Vector3 anchor, float meters)
    {
        var limit = meters * meters;
        var players = state.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.IsOnline && state.TryToPlanetFrame(player.Frame, player.Position, out var onPlanet, out var feet) && onPlanet == planet
                && Vector3.DistanceSquared(feet, anchor) <= limit)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConstructNear(WorldState state, PlanetId planet, Vector3 anchor, float meters)
    {
        var constructs = state.Constructs.All;
        for (var i = 0; i < constructs.Count; i++)
        {
            var construct = constructs[i];
            if (construct.BlockCount == 0)
            {
                continue;
            }

            // Coarse first: the bounds of the construct as a sphere in the planet frame.
            var min = construct.BoundsMin;
            var max = construct.BoundsMax;
            var middle = new Vector3(min.X + max.X + 1, min.Y + max.Y + 1, min.Z + max.Z + 1) * 0.5f;
            var half = new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1).Length() * 0.5f;
            if (!state.TryToPlanetFrame(FrameRef.Of(construct.Id), middle, out var onPlanet, out var middleInPlanet) || onPlanet != planet
                || Vector3.Distance(middleInPlanet, anchor) > meters + half)
            {
                continue;
            }

            if (ChunksNear(state, construct, anchor, meters))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ChunksNear(WorldState state, Construct construct, Vector3 anchor, float meters)
    {
        foreach (var (position, chunk) in construct.Chunks)
        {
            var centre = new Vector3((position.X * 16) + 8, (position.Y * 16) + 8, (position.Z * 16) + 8);
            if (!state.TryToPlanetFrame(FrameRef.Of(construct.Id), centre, out _, out var centreInPlanet)
                || Vector3.Distance(centreInPlanet, anchor) > meters + ChunkHalfDiagonal)
            {
                continue;
            }

            var blocks = chunk.Blocks;
            for (var index = 0; index < blocks.Length; index++)
            {
                if (new BlockState(blocks[index]).IsEmpty)
                {
                    continue;
                }

                var cell = CellPos.FromChunk(position, index);
                if (state.TryToPlanetFrame(FrameRef.Of(construct.Id), new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), out _, out var inPlanet)
                    && Vector3.Distance(inPlanet, anchor) <= meters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Scan(ServerSimulation sim, SurvivalContent content)
    {
        var state = sim.State;
        var rules = sim.Rules;
        var all = state.HarvestedProps.All;
        var count = all.Count;
        var start = LowerBound(all, _cursorPlanet, _cursorProp);
        if (start >= count)
        {
            start = 0;
        }

        var visit = Math.Min(Math.Max(1, rules.PropRegrowScanBatch), count);
        _due.Clear();
        for (var i = 0; i < visit; i++)
        {
            var record = all[(start + i) % count];
            if (IsDue(sim, content, record))
            {
                _due.Add(new HarvestedPropKey(record.Planet, record.Prop));
            }
        }

        // The next scan goes on right after the last entry looked at.
        var last = all[(start + visit - 1) % count];
        _cursorPlanet = last.Planet.Value;
        _cursorProp = last.Prop.Value + 1;
        if (_due.Count > 0)
        {
            Regrow(sim, rules);
        }
    }

    private bool IsDue(ServerSimulation sim, SurvivalContent content, HarvestedPropRecord record)
    {
        var state = sim.State;
        if (!state.Planets.TryGet(record.Planet, out var planet) || !PropLayouts.TryGet(planet.GeneratorProfile, out var layout)
            || !layout.TryGetCandidate(planet.Seed, record.Prop, out var candidate))
        {
            ReportUnknown(record);
            return false;
        }

        // Rocks, boulders and crystals never regrow; the time is read from the content by the kind alone, without sampling the surface.
        if (!content.TryGetProp(candidate.Kind, out var def) || def.RegrowSeconds == 0)
        {
            return false;
        }

        var wait = (ulong)def.RegrowSeconds * (ulong)Math.Max(1, sim.Rules.TickRate);
        if (record.Tick.Value > ulong.MaxValue - wait || state.CurrentTick.Value < record.Tick.Value + wait)
        {
            return false;
        }

        _field ??= new PropField(state, _terrain(), sim.Rules);
        if (!_field.TryGetProp(record.Planet, record.Prop, out var instance))
        {
            ReportUnknown(record);
            return false;
        }

        // Flora does not grow over a hole: the block of the anchor was edited by someone (also by a dig that removed the prop).
        TerrainMath.AffectedBlocks(record.Planet, instance.Anchor, 0f, planet.VoxelSizeMeters, 0, _blocks);
        if (_blocks.Count > 0 && state.TerrainVersions.Get(_blocks[0]) != 0)
        {
            return false;
        }

        return !PlayerNear(state, record.Planet, instance.Anchor, sim.Rules.PropRegrowClearanceMeters)
            && !ConstructNear(state, record.Planet, instance.Anchor, sim.Rules.PropRegrowClearanceMeters);
    }

    private void ReportUnknown(HarvestedPropRecord record)
    {
        // Content or layout changed under a harvested prop: it stays harvested, and the log says so once per prop.
        if (_reportedUnknown.Add(new HarvestedPropKey(record.Planet, record.Prop)))
        {
            _logger.UnknownHarvestedProp(record.Prop.Value, record.Planet.Value);
        }
    }

    private void Regrow(ServerSimulation sim, GameRules rules)
    {
        _due.Sort(_byKey);
        var most = Math.Max(1, rules.MaxRegrowPerOperation);
        for (var offset = 0; offset < _due.Count; offset += most)
        {
            var size = Math.Min(most, _due.Count - offset);
            Operation operation;
            if (size == 1)
            {
                operation = new PropRegrowOp(_due[offset].Planet, _due[offset].Prop);
            }
            else
            {
                var parts = new Operation[size];
                for (var i = 0; i < size; i++)
                {
                    parts[i] = new PropRegrowOp(_due[offset + i].Planet, _due[offset + i].Prop);
                }

                operation = new CompoundOp(parts);
            }

            try
            {
                var applied = sim.ApplyServerOperation(operation);
                _logger.PropsRegrown(size, applied.Id.Value);
            }
            catch (InvalidOperationException e)
            {
                // A precondition failed although the schedule checked the state: not fatal for the server, and the next scan tries again.
                _logger.RegrowFailed(size, e);
                return;
            }
        }
    }
}
