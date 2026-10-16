using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Server.Survival;

/// <summary>
/// The surroundings of the players for the suit (K6-34 D17): the profile, the height, the time of day, the shelter, the generator and the seat, evaluated for a player every
/// <see cref="HazardRules.HazardEvaluateEveryTicks"/> ticks (spread over the players by their ids, so two players are rarely due in the same tick) and at once when there is no sample yet or the frame,
/// the parent counter or the seat changed. The terrain is the one of the simulation (<see cref="ServerSimulation.Terrain"/>, like the rest tracker of K6-33): main thread only. After the first sample of a
/// player nothing here allocates.
/// </summary>
internal sealed class HazardTracker
{
    private const float DefaultIntensity = 1f;
    private const float MinIntensity = 0.5f;
    private const float MaxIntensity = 2f;

    private readonly ServerSimulation _sim;
    private readonly HazardContent _content;
    private readonly HazardRules _rules;
    private readonly ItemTypeId _fuel;
    private readonly ulong _ticksPerCell;
    private readonly Dictionary<PlayerId, Entry> _entries = [];

    /// <summary>Initializes a new instance of the <see cref="HazardTracker"/> class.</summary>
    /// <param name="simulation">The simulation, with the hazard content.</param>
    /// <exception cref="ArgumentException">The simulation has no hazard content.</exception>
    public HazardTracker(ServerSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _sim = simulation;
        _content = simulation.Survival?.Hazards ?? throw new ArgumentException("The simulation has no hazard content.", nameof(simulation));
        _rules = simulation.HazardRules;
        _ticksPerCell = _content.Generator.TicksPerCell(simulation.Rules.TickRate);
        _fuel = simulation.State.ItemTypes.TryGetId(_content.Generator.Fuel, out var fuel) ? fuel : default;
        RawIntensity = simulation.Rules.HazardIntensity;
        Intensity = Sanitize(RawIntensity);
    }

    /// <summary>Gets the number of evaluations done so far (for the tests of the schedule).</summary>
    public int Evaluations { get; private set; }

    /// <summary>Gets the intensity of the hazards as the world rules gave it.</summary>
    public float RawIntensity { get; }

    /// <summary>Gets the intensity used: <see cref="RawIntensity"/> between 0.5 and 2, and 1 when it is not a number.</summary>
    public float Intensity { get; }

    /// <summary>Gets a value indicating whether <see cref="RawIntensity"/> was changed to get <see cref="Intensity"/>.</summary>
    public bool IntensityAdjusted => !(RawIntensity == Intensity);

    /// <summary>The sample of a player at a tick: the kept one, or a new one when it is due.</summary>
    /// <param name="player">The player.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The sample.</returns>
    public HazardSample Sample(PlayerState player, Tick now)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!_entries.TryGetValue(player.Player, out var entry))
        {
            entry = new Entry();
            _entries[player.Player] = entry;
        }

        var interval = (ulong)Math.Max(_rules.HazardEvaluateEveryTicks, 1);
        var due = !entry.HasSample
            || entry.Frame != player.Frame || entry.ParentSeq != player.ParentSeq || entry.Seat != player.Seat
            || (now.Value + player.Player.Value) % interval == 0
            || now.Value >= entry.Tick.Value + interval;
        if (due)
        {
            entry.Sample = Evaluate(player, now);
            entry.HasSample = true;
            entry.Tick = now;
            entry.Frame = player.Frame;
            entry.ParentSeq = player.ParentSeq;
            entry.Seat = player.Seat;
            Evaluations++;
        }

        return entry.Sample;
    }

    /// <summary>Forgets a player (leaving), so the next sample is taken at once.</summary>
    /// <param name="player">The player.</param>
    public void Forget(PlayerId player) => _entries.Remove(player);

    private static float Sanitize(float intensity) =>
        float.IsFinite(intensity) ? Math.Clamp(intensity, MinIntensity, MaxIntensity) : DefaultIntensity;

    private HazardSample Evaluate(PlayerState player, Tick now)
    {
        var state = _sim.State;
        var lifeSupport = HasLifeSupport(state, player);
        if (!ShelterEvaluator.TryGetOrigin(state, player, _rules, out var planet, out var origin) || !state.Planets.TryGet(planet, out var planetParams))
        {
            // Outside any planet (the frame of the star system): the profile of space, no rays, no generator (D9, D12).
            var space = HazardProfileResolver.Resolve(state, _content, null, 0f);
            return new HazardSample(HazardField.Rates(space, 0f, 1f, Intensity), false, 0, false, lifeSupport);
        }

        // The height of the feet above the radius; the origin is the feet plus the origin height along the radial up.
        var height = origin.Length() - planetParams.RadiusMeters - _rules.ShelterOriginHeightMeters;
        var profile = HazardProfileResolver.Resolve(state, _content, planet, height);
        var cycle = state.StarSystem?.SunCycleSeconds ?? planetParams.DayLengthSeconds;
        var sun = StarSystemMath.SunDirection(now.Value, _sim.Rules.TickRate, cycle);
        var daylight = HazardField.Daylight(TerrainMath.UpAt(origin), new Vector3((float)sun.X, (float)sun.Y, (float)sun.Z), _rules.TwilightBand);
        var present = HazardField.Rates(profile, height, daylight, Intensity);
        var shelter = ShelterEvaluator.Evaluate(state, _sim.Terrain, planet, origin, _rules);
        var generator = OxygenCoverage.IsCovered(state, planet, origin, _fuel, _ticksPerCell, now, _rules);
        return new HazardSample(present, shelter.Sheltered, shelter.Hits, generator, lifeSupport);
    }

    // A seat in a construct in ship mode supports life fully (D14).
    private static bool HasLifeSupport(WorldState state, PlayerState player) =>
        player.Seat is not null && player.Frame.Kind == FrameKind.Construct
        && state.Constructs.TryGet(new EntityId(player.Frame.Id), out var construct) && construct.Mode == ConstructMode.Ship;

    private sealed class Entry
    {
        public bool HasSample { get; set; }

        public HazardSample Sample { get; set; }

        public Tick Tick { get; set; }

        public FrameRef Frame { get; set; }

        public uint ParentSeq { get; set; }

        public CellPos? Seat { get; set; }
    }
}
