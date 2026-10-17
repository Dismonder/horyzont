using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Server.Survival;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// <see cref="HazardTracker"/> (K6-34 D9, D14, D17): the surroundings of a player are evaluated every 10 ticks, spread over the players by their ids, and at once when there is no sample yet or the frame, the
/// parent counter or the seat changed. The rates come from the profile of the body, the height and the time of day (the temperate profile of the rig has cold at night and nothing by day), the shelter from the
/// 26 rays, the generator from the distance to the origin of the player (the feet plus 1 m up), and the life support from a seat in a ship.
/// </summary>
public sealed class HazardTrackerTests
{
    private readonly HazardServerRig _rig = new();
    private readonly HazardTracker _tracker;

    public HazardTrackerTests() => _tracker = new HazardTracker(_rig.Sim);

    private PlayerState State(PlayerId player)
    {
        Assert.True(_rig.Sim.State.Players.TryGet(player, out var state));
        return state;
    }

    private HazardSample Sample(PlayerId player, ulong tick) => _tracker.Sample(State(player), new Tick(tick));

    private List<ulong> EvaluationTicks(HazardTracker tracker, PlayerId player, ulong from, ulong to)
    {
        var ticks = new List<ulong>();
        for (var tick = from; tick <= to; tick++)
        {
            var before = tracker.Evaluations;
            tracker.Sample(State(player), new Tick(tick));
            if (tracker.Evaluations > before)
            {
                ticks.Add(tick);
            }
        }

        return ticks;
    }

    [Fact]
    public void Evaluate_Every10Ticks_Staggered()
    {
        // The first sample is taken at once; then a player is due at the ticks where tick + id is a multiple of 10, so two players never share the work of one tick.
        Assert.Equal([100UL, 109UL, 119UL, 129UL], EvaluationTicks(_tracker, _rig.Alice, 100, 130));

        var other = new HazardTracker(_rig.Sim);
        Assert.Equal([100UL, 108UL, 118UL, 128UL], EvaluationTicks(other, _rig.Bob, 100, 130));
    }

    [Fact]
    public void Sample_BetweenEvaluations_IsTheKeptOne()
    {
        var first = Sample(_rig.Alice, 100);
        var evaluations = _tracker.Evaluations;

        Assert.Equal(first, Sample(_rig.Alice, 101));
        Assert.Equal(evaluations, _tracker.Evaluations);
        Assert.False(first.Sheltered);
        Assert.False(first.Generator);
        Assert.False(first.LifeSupport);
    }

    [Fact]
    public void FrameChange_ReevaluatesImmediately()
    {
        Sample(_rig.Alice, 101);
        Sample(_rig.Alice, 102);
        var evaluations = _tracker.Evaluations;
        Assert.Equal(1, evaluations);

        // The player leaves the planet at tick 102; at 103 (not due: 103 + 1 is not a multiple of 10) the sample is already the one of space.
        _rig.LeavePlanet(_rig.Alice);
        var sample = Sample(_rig.Alice, 103);

        Assert.Equal(evaluations + 1, _tracker.Evaluations);
        Assert.Equal(0.5f, sample.Present.Vacuum, 0.0001f);
    }

    [Fact]
    public void OffPlanet_SpaceProfileNoShelter()
    {
        var onPlanet = Sample(_rig.Alice, 100);
        Assert.Equal(0f, onPlanet.Present.Vacuum);

        _rig.LeavePlanet(_rig.Alice);
        var inSpace = Sample(_rig.Alice, 101);

        // The profile of space (D24): cold 0.3, radiation 0.3 and vacuum 0.5, day and night; no planet, so no rays, no shelter and no generator.
        Assert.Equal(new HazardRates(0.3f, 0f, 0f, 0.3f, 0.5f), inSpace.Present);
        Assert.False(inSpace.Sheltered);
        Assert.Equal(0, inSpace.ShelterHits);
        Assert.False(inSpace.Generator);
        Assert.False(inSpace.LifeSupport);
    }

    [Fact]
    public void AboveTheAtmosphere_SpaceProfile()
    {
        // 1000 m of atmosphere over the radius of 4000 m: the profile of the body applies at the top (inclusive) and the profile of space above it.
        _rig.StandAt(_rig.Alice, new Vector3(0f, 5000f, 0f));
        Assert.Equal(0f, Sample(_rig.Alice, 100).Present.Vacuum);

        _rig.StandAt(_rig.Alice, new Vector3(0f, 5100f, 0f));
        var sample = Sample(_rig.Alice, 109);

        Assert.Equal(0.5f, sample.Present.Vacuum, 0.0001f);
        Assert.False(sample.Sheltered);
    }

    [Fact]
    public void Seated_InShip_LifeSupport()
    {
        Assert.False(Sample(_rig.Alice, 101).LifeSupport);

        _rig.Seat(_rig.Alice, ship: true);

        // A seat in a construct in ship mode: the life support is on at once, although tick 102 is not due for Alice.
        Assert.True(Sample(_rig.Alice, 102).LifeSupport);
    }

    [Fact]
    public void Seated_InStaticConstruct_NoLifeSupport()
    {
        _rig.Seat(_rig.Alice, ship: false);
        Assert.False(Sample(_rig.Alice, 102).LifeSupport);

        // The twin: the same seat in a ship gives the life support (so the seat alone is not what is tested).
        var ship = new HazardServerRig();
        ship.Seat(ship.Alice, ship: true);
        Assert.True(ship.Sim.State.Players.TryGet(ship.Alice, out var state));
        Assert.True(new HazardTracker(ship.Sim).Sample(state, new Tick(102)).LifeSupport);
    }

    [Fact]
    public void GeneratorRange_MeasuredFromOrigin_NotFeet()
    {
        _rig.PlaceGenerator(_rig.Sim.State.CurrentTick);

        // The cell centre of the generator is (5.5, 4001.5, 0.5). The feet are 9.95 m along x from it and 1.5 m below it; the origin (the feet plus 1 m up) is 9.96 m away, the feet 10.06 m: the generator covers the
        // player only when the distance is taken from the origin (D12, review B1b P2-3).
        _rig.StandAt(_rig.Alice, new Vector3(-4.45f, 4000f, 0.5f));
        Assert.True(Sample(_rig.Alice, 100).Generator);

        // Four tenths of a metre further the origin is out of range as well.
        _rig.StandAt(_rig.Alice, new Vector3(-4.75f, 4000f, 0.5f));
        Assert.False(Sample(_rig.Alice, 109).Generator);
    }

    [Fact]
    public void Generator_CoversExactlyUntilTheLastCellEnds_NoSettlementNeeded()
    {
        var started = _rig.Sim.State.CurrentTick;
        _rig.PlaceGenerator(started);
        _rig.StandAt(_rig.Alice, new Vector3(0f, 4000.1f, 0f));
        var per = _rig.TicksPerCell;

        // The input is empty: the cell that burns when the generator starts is the only one. No settlement runs here; the protection ends at the tick the cell ends all the same (D12).
        Assert.True(Fresh(started.Value + per - 1).Generator);
        Assert.False(Fresh(started.Value + per).Generator);
    }

    private HazardSample Fresh(ulong tick) => new HazardTracker(_rig.Sim).Sample(State(_rig.Alice), new Tick(tick));

    [Fact]
    public void PlayerLeaveRejoin_SampleReevaluatedImmediately()
    {
        Sample(_rig.Alice, 101);
        var evaluations = _tracker.Evaluations;

        _tracker.Forget(_rig.Alice);
        Sample(_rig.Alice, 102);

        // Without the forgetting tick 102 would return the sample kept from before the leaving (not due until 109).
        Assert.Equal(evaluations + 1, _tracker.Evaluations);
    }

    [Fact]
    public void SeatChange_ReevaluatesImmediately()
    {
        _rig.Seat(_rig.Alice, ship: true);
        Assert.True(Sample(_rig.Alice, 102).LifeSupport);
        var evaluations = _tracker.Evaluations;

        // The player stands up (same frame, a new parent counter, no seat): the life support ends at once.
        Assert.True(_rig.Sim.State.Players.TryGet(_rig.Alice, out var state));
        _rig.Sim.ApplyServerOperation(new Horyzont.Core.Operations.EntityReparentOp(
            state.Entity, state.Frame, state.ParentSeq + 1, state.Position, state.BodyRotation, Vector3.Zero, null));

        Assert.False(Sample(_rig.Alice, 103).LifeSupport);
        Assert.Equal(evaluations + 1, _tracker.Evaluations);
    }

    [Fact]
    public void ParentSeqChange_ResetsSample()
    {
        Sample(_rig.Alice, 101);
        var evaluations = _tracker.Evaluations;

        _rig.Reparent(_rig.Alice);
        Sample(_rig.Alice, 102);

        Assert.Equal(evaluations + 1, _tracker.Evaluations);
    }
}
