using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// <see cref="ContainerReach"/> (K6-30 D9, DO USTALENIA-8): the distance from the eye of a player to the centre of a block cell in the
/// frame of their planet, the same number for the validator and for the chest subscriptions of the server.
/// </summary>
public sealed class ContainerReachTests
{
    private static readonly Vector3 _constructOrigin = new(0f, 4000f, 0f);

    [Fact]
    public void TryDistanceFromEye_ChestInFrontOfPlayer_IsEyeToCellCentre()
    {
        var f = ForIntents();
        var eye = f.Sim.Eye(Player1);
        var centre = _constructOrigin + new Vector3(0.5f, 1.5f, 0.5f);

        var found = ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, Player1, ConstructId, FunctionalBlocks.ChestCell, out var meters);

        Assert.True(found);
        Assert.Equal(Vector3.Distance(eye, centre), meters, 0.001f);
    }

    [Fact]
    public void TryDistanceFromEye_RotatedConstruct_UsesTheCellCentreInThePlanetFrame()
    {
        var f = ForIntents();
        var origin = new Vector3(10f, 4000f, 10f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var other = f.State.EntityIds.PeekNext();
        f.Apply(new ConstructCreateOp(other, FrameRef.Of(WorldFactory.HomePlanet), origin, rotation, new CellPos(0, 0, 0), BlockState.Create(FunctionalBlocks.StoneType, 0)));
        var cell = new CellPos(2, 1, 0);
        f.Apply(new BlockSetOp(other, cell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        var centre = origin + Vector3.Transform(new Vector3(2.5f, 1.5f, 0.5f), rotation);

        var found = ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, Player1, other, cell, out var meters);

        Assert.True(found);
        Assert.Equal(Vector3.Distance(f.Sim.Eye(Player1), centre), meters, 0.001f);
    }

    [Fact]
    public void TryDistanceFromEye_UnknownPlayerOrConstruct_False()
    {
        var f = ForIntents();
        Assert.True(ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, Player1, ConstructId, FunctionalBlocks.ChestCell, out _));

        Assert.False(ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, new PlayerId(99), ConstructId, FunctionalBlocks.ChestCell, out _));
        Assert.False(ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, Player1, new EntityId(999), FunctionalBlocks.ChestCell, out _));
    }

    [Fact]
    public void TryDistanceFromEye_MatchesValidatorEye()
    {
        // The validator accepts a cell within ReachMeters plus the slack of one cell half-diagonal (0.87 m); the same eye and centre have
        // to decide here. Straddle that line: the cell is removable just inside, refused just outside, and ContainerReach agrees.
        var f = ForIntents();
        var boundary = f.Sim.Rules.ReachMeters + 0.87f;
        var remove = new RemoveBlockIntent(ConstructId, FunctionalBlocks.BedCell);

        ReachPlacement.MoveEyeToDistance(f, Player1, FunctionalBlocks.BedCell, boundary + 0.02f);
        Assert.True(ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, Player1, ConstructId, FunctionalBlocks.BedCell, out var outside));
        Assert.True(outside > boundary);
        f.AssertRejected(Player1, remove, RejectReason.OutOfReach);

        ReachPlacement.MoveEyeToDistance(f, Player1, FunctionalBlocks.BedCell, boundary - 0.02f);
        Assert.True(ContainerReach.TryDistanceFromEye(f.State, f.Sim.Rules, Player1, ConstructId, FunctionalBlocks.BedCell, out var inside));
        Assert.True(inside <= boundary);
        Assert.NotEqual(RejectReason.OutOfReach, f.SubmitAs(Player1, remove).Reason);
    }
}
