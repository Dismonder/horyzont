using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// The rest (K6-33 D14): a player who sits, or lies after sleeping at the own bed, under a roof by a heat source is rested after 1200 ticks of unbroken conditions, for
/// (8 + comfort) minutes, renewed while the conditions hold. The world is that of <see cref="FoodServerRig"/>: the bed (comfort 1), the lamp (1), a campfire (heat,
/// comfort 1) beside the seat and a roof above it, at 60 ticks per second.
/// </summary>
public sealed class RestTrackerTests
{
    private readonly FoodServerRig _rig = new();

    private PlayerVitals Vitals() => _rig.VitalsOf(_rig.Alice) ?? throw new InvalidOperationException("no vitals");

    private PlayerVitalsMessage Last() => _rig.MessagesTo(_rig.Alice)[^1];

    private void Prepare(bool heated = true)
    {
        if (heated)
        {
            _rig.Heat();
        }

        _rig.Sim.ApplyServerOperation(new BlockEntitySetOp(
            FunctionalBlocks.Construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType, new BedBlockState(_rig.Alice)));
        _rig.Vitals.OnPlayerReady(_rig.Alice);
        _rig.Motion.Set(_rig.Alice, MovementMode.Walking);
    }

    private static BlockInteractIntent Sleep() => new(FunctionalBlocks.Construct, FunctionalBlocks.BedCell, BlockInteractAction.Sleep);

    private void StandByTheBed()
    {
        _rig.Place(new Horyzont.Core.World.Constructs.CellPos(1, 4, 0), FunctionalBlocks.StoneType);
        Assert.True(_rig.Sim.State.Players.TryGet(_rig.Alice, out var state));
        var feet = new Vector3(1.5f, 4002f, 0.5f);
        Assert.True(_rig.Sim.UpdatePlayerKinematics(_rig.Alice, state.Frame, state.ParentSeq, feet, state.BodyRotation, 0f));
    }

    [Fact]
    public void Sleep_NearOwnBed_StartsLying()
    {
        Prepare();
        StandByTheBed();
        _rig.Tick(40);
        Assert.False(Last().RestConditionsMet);

        _rig.Vitals.OnIntentAccepted(_rig.Alice, Sleep());
        _rig.Tick(40);

        Assert.True(Last().RestConditionsMet);
    }

    [Fact]
    public void Sleep_FarFromBed_NoLying()
    {
        Prepare();
        StandByTheBed();
        _rig.Move(_rig.Alice, 20f);

        _rig.Vitals.OnIntentAccepted(_rig.Alice, Sleep());
        _rig.Tick(40);

        Assert.False(Last().RestConditionsMet);
    }

    [Fact]
    public void Lying_MovedOverHalfMetre_Ends()
    {
        Prepare();
        StandByTheBed();
        _rig.Vitals.OnIntentAccepted(_rig.Alice, Sleep());
        _rig.Tick(40);
        Assert.True(Last().RestConditionsMet);

        _rig.Move(_rig.Alice, 0.4f);
        _rig.Tick(40);
        Assert.True(Last().RestConditionsMet);

        _rig.Move(_rig.Alice, 0.4f);
        _rig.Tick(40);

        Assert.False(Last().RestConditionsMet);
    }

    [Fact]
    public void Seated_RoofAndHeat_After1200Ticks_Rested()
    {
        Prepare();
        _rig.Seat(_rig.Alice);

        _rig.Tick(1100);
        Assert.True(Last().RestConditionsMet);
        Assert.False(Last().Rested);
        Assert.InRange(Last().RestWarmupTicks, 1000, 1200);

        _rig.Tick(200);

        Assert.True(Last().Rested);
        Assert.True(Vitals().RestedUntil.Value > _rig.Sim.State.CurrentTick.Value);
    }

    [Fact]
    public void Seated_WithoutHeat_NeverRested()
    {
        Prepare(heated: false);
        _rig.Place(FoodServerRig.RoofCell, FunctionalBlocks.StoneType);
        _rig.Seat(_rig.Alice);

        _rig.Tick(1500);

        Assert.False(Last().RestConditionsMet);
        Assert.False(Last().Rested);
    }

    [Fact]
    public void Warmup_Interrupted_Restarts()
    {
        Prepare();
        _rig.Seat(_rig.Alice);
        _rig.Tick(900);
        Assert.False(Last().Rested);

        // Without the roof for 60 ticks the warm-up is lost: 900 more ticks under it are not enough, 1300 are.
        _rig.Sim.ApplyServerOperation(new BlockClearOp(FunctionalBlocks.Construct, FoodServerRig.RoofCell));
        _rig.Tick(60);
        Assert.False(Last().RestConditionsMet);
        _rig.Place(FoodServerRig.RoofCell, FunctionalBlocks.StoneType);
        _rig.Tick(900);
        Assert.False(Last().Rested);

        _rig.Tick(400);

        Assert.True(Last().Rested);
    }

    [Fact]
    public void Rested_RefreshedWhileConditionsHold()
    {
        Prepare();
        _rig.Seat(_rig.Alice);
        _rig.Tick(1300);
        var first = Vitals().RestedUntil.Value;
        Assert.True(first > 0);

        _rig.Tick(600);

        Assert.True(Vitals().RestedUntil.Value > first);
    }

    [Fact]
    public void Rested_ComfortThree_ElevenMinutes()
    {
        Prepare();
        _rig.Seat(_rig.Alice);
        var before = 0UL;
        var setAt = 0UL;
        for (var i = 0; i < 1500 && before == 0; i++)
        {
            setAt = _rig.Sim.State.CurrentTick.Value;
            _rig.Tick(1);
            before = Vitals().RestedUntil.Value;
        }

        // The bed, the lamp and the campfire are three different types: comfort 3, (8 + 3) minutes of 3600 ticks.
        Assert.Equal(3, Last().Comfort);
        Assert.Equal(setAt + (11UL * 3600UL), before);
    }

    [Fact]
    public void Seated_UnderOpenSky_NotRoofed()
    {
        // The twin of the roof tests above: the same seat and fire, no roof. A tilted construct (the B6 note for C3) needs a construct that rotates in the world and is left to the game.
        Prepare(heated: false);
        _rig.Place(FoodServerRig.CampfireCell, FoodBlocks.CampfireType);
        _rig.Seat(_rig.Alice);

        _rig.Tick(100);

        Assert.False(Last().RestConditionsMet);
    }

    [Fact]
    public void InFlyingShip_NoRest()
    {
        // The twin of Seated_RoofAndHeat_After1200Ticks_Rested: the same seat, roof and fire, but the construct is in the frame of the star system (a ship in flight).
        Prepare();
        _rig.Seat(_rig.Alice);
        _rig.Tick(100);
        Assert.True(Last().RestConditionsMet);
        Assert.True(_rig.Sim.State.Constructs.TryGet(FunctionalBlocks.Construct, out var ship));
        _rig.Sim.ApplyServerOperation(new EntityReparentOp(
            FunctionalBlocks.Construct, new FrameRef(FrameKind.System, 1), ship.ParentSeq + 1, new Vector3(0f, 9000f, 0f), Quaternion.Identity, Vector3.Zero, null));

        _rig.Tick(1500);

        Assert.False(Last().RestConditionsMet);
        Assert.False(Last().Rested);
    }

    [Fact]
    public void Seated_InTiltedConstruct_RoofNotAboveTheSeat()
    {
        // The roof is straight above the seat in the frame of the construct; tilted 45 degrees it is 2 m to the side of the vertical ray of the planet (the B6 note for C3).
        // The point of the evaluation must also leave the cell of the seat: a tilted cube reaches 0.87 m from its centre, so a point 0.5 m up would be inside the seat itself.
        Prepare();
        _rig.Seat(_rig.Alice);
        _rig.Tick(100);
        Assert.True(Last().RestConditionsMet);
        Assert.True(_rig.Sim.State.Constructs.TryGet(FunctionalBlocks.Construct, out var construct));
        _rig.Sim.ApplyServerOperation(new EntityReparentOp(
            FunctionalBlocks.Construct, construct.Parent, construct.ParentSeq + 1, construct.Origin,
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f), Vector3.Zero, null));

        _rig.Tick(100);

        Assert.False(Last().RestConditionsMet);
    }

    [Fact]
    public void Seated_CeilingDirectlyAboveSeat_Roofed()
    {
        // The roof in the cell right above the seat: the point of the evaluation (0.9 m above the centre of the seat) starts inside it and must still count as roofed.
        Prepare(heated: false);
        _rig.Place(FoodServerRig.CampfireCell, FoodBlocks.CampfireType);
        _rig.Place(new Horyzont.Core.World.Constructs.CellPos(FoodServerRig.SeatCell.X, FoodServerRig.SeatCell.Y + 1, FoodServerRig.SeatCell.Z), FunctionalBlocks.StoneType);
        _rig.Seat(_rig.Alice);

        _rig.Tick(100);

        Assert.True(Last().RestConditionsMet);
    }

    [Fact]
    public void Seated_InTiltedConstruct_RoofStandingStraightAboveThePoint_Roofed()
    {
        // The twin of Seated_InTiltedConstruct_RoofNotAboveTheSeat: the same 45 degree tilt, but a block of another (upright) construct stands in the world right above the
        // point of the evaluation, so the vertical ray of the planet hits it.
        Prepare();
        _rig.Seat(_rig.Alice);
        Assert.True(_rig.Sim.State.Constructs.TryGet(FunctionalBlocks.Construct, out var construct));
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f);
        _rig.Sim.ApplyServerOperation(new EntityReparentOp(
            FunctionalBlocks.Construct, construct.Parent, construct.ParentSeq + 1, construct.Origin, tilt, Vector3.Zero, null));
        var seat = Vector3.Transform(
            new Vector3(FoodServerRig.SeatCell.X + 0.5f, FoodServerRig.SeatCell.Y + 0.5f, FoodServerRig.SeatCell.Z + 0.5f), tilt) + construct.Origin;
        _rig.Sim.ApplyServerOperation(new ConstructCreateOp(
            new EntityId(901), construct.Parent, seat + new Vector3(-0.5f, 1.5f, -0.5f), Quaternion.Identity,
            new Horyzont.Core.World.Constructs.CellPos(0, 0, 0), Horyzont.Core.World.Constructs.BlockState.Create(FunctionalBlocks.StoneType, 0)));

        _rig.Tick(100);

        Assert.True(Last().RestConditionsMet);
    }
}
