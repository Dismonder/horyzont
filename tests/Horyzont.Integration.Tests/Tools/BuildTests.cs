using System.Numerics;

using FsCheck.Xunit;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using static Horyzont.Integration.Tests.Tools.ToolTestHelpers;

namespace Horyzont.Integration.Tests.Tools;

/// <summary>Block tool through the full path, and the ghost of the preview against the construct the server creates.</summary>
public sealed class BuildTests
{
    private static Vector3 Diagonal => new(-MathF.Sqrt(0.5f), 0f, -MathF.Sqrt(0.5f));

    /// <summary>Sends intents straight into the simulation, so a property can run hundreds of cases without a network.</summary>
    private sealed class DirectSender(ServerSimulation sim, PlayerId player) : IIntentSender
    {
        private uint _seq;

        public bool CanSend => true;

        public IntentResult LastResult { get; private set; }

        public uint Submit(Intent intent)
        {
            LastResult = sim.Submit(player, intent);
            return ++_seq;
        }
    }

    [Property(MaxTest = 200)]
    public bool Build_OnTerrain_CreatesConstructMatchingGhost(sbyte xi, sbyte zi, byte yaw)
    {
        var state = WorldFactory.CreateDefault();
        var terrain = new FakeTerrainService();
        var sim = new ServerSimulation(state, terrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var op = sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 1), "p");
        sim.ApplyServerOperation(op);
        state.Players.TryGet(op.Player, out var player);
        var view = new StateToolView(state, terrain, op.Player);
        var sender = new DirectSender(sim, op.Player);
        var tracker = new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance);
        var controller = new ToolController(
            sim.Rules, view, sender, tracker, BlockPalette.FromHello(Hello(state)), MaterialPalette.FromHello(Hello(state)),
            NullLogger<ToolController>.Instance);
        controller.Update(Input(select: ToolKind.Builder), default, 0);

        var x = xi / 25f;
        var z = zi / 25f;
        var r = terrain.SurfaceRadius;
        var target = new Vector3(x, MathF.Sqrt((r * r) - (x * x) - (z * z)), z);
        var angle = yaw * MathF.Tau / 256f;
        var look = new Vector3(MathF.Sin(angle), 0f, -MathF.Cos(angle));
        var eye = AimRayBuilder.EyeFromFeet(player!.Position, sim.Rules);
        if (!AimRayBuilder.TryBuild(WorldFactory.HomePlanet, player.Position, eye, target - eye, look, view, sim.Rules, out var aim))
        {
            return true;
        }

        controller.Update(Input(secondaryPressed: true), aim, 1);
        var preview = controller.Preview;
        if (sender.LastResult.Applied is null)
        {
            // Close to the player the server refuses with IntersectsPlayer, and the hint says so too; nothing else can refuse here.
            return sender.LastResult.Reason == RejectReason.IntersectsPlayer && preview.Hint == RejectReason.IntersectsPlayer;
        }

        var create = (ConstructCreateOp)sender.LastResult.Applied!.Operation;
        return preview.Kind == ToolPreviewKind.NewConstructCell && preview.Hint == RejectReason.None && create.Origin == preview.Origin
            && create.Rotation == preview.Rotation;
    }

    private static Core.Protocol.Messages.ServerHello Hello(Core.World.WorldState state) =>
        new(state.Id, state.CurrentTick, 60, new PlayerId(1), new EntityId(1), state.Planets.All, state.BlockTypes.Entries, state.Materials.Entries);

    [Fact]
    public void Build_OnTerrain_ThroughControllerCreatesConstructAtGhost()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(3f));
        alice.Update(Input(select: ToolKind.Builder), aim);

        alice.Update(Input(secondaryPressed: true), aim);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));

        var create = Assert.IsType<ConstructCreateOp>(rig.Journal.Entries[^1].Operation);
        Assert.Equal(RejectReason.None, alice.Replies[0].Reason);
        Assert.Equal(create.Origin, alice.Controller.Preview.Origin);
        Assert.Equal(create.Rotation, alice.Controller.Preview.Rotation);
    }

    [Fact]
    public void Build_OnFace_ExtendsConstruct()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var first = alice.AimAt(rig.Ground(3f));
        alice.Update(Input(select: ToolKind.Builder), first);
        alice.Update(Input(secondaryPressed: true), first);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));
        var construct = ((ConstructCreateOp)rig.Journal.Entries[^1].Operation).Construct;
        rig.Sim.State.Constructs.TryGet(construct, out var built);
        var versionBefore = built!.Version;

        // The side of the new block facing the player: x = 2.5, half a metre above the ground.
        var aim = alice.AimAt(rig.Ground(2.5f) + new Vector3(0f, 0.5f, 0f));
        rig.Pump(5);
        alice.Update(Input(secondaryPressed: true), aim);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 2));

        Assert.Equal(RejectReason.None, alice.Replies[1].Reason);
        var set = Assert.IsType<BlockSetOp>(rig.Journal.Entries[^1].Operation);
        Assert.Equal(construct, set.Construct);
        Assert.Equal(new CellPos(-1, 0, 0), set.Cell);
        Assert.Equal(versionBefore + 1, built.Version);
        Assert.Equal(ToolPreviewKind.ConstructCell, alice.Controller.Preview.Kind);
    }

    [Fact]
    public void Remove_LastBlock_RemovesConstruct()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(3f));
        alice.Update(Input(select: ToolKind.Builder), aim);
        alice.Update(Input(secondaryPressed: true), aim);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));
        var construct = ((ConstructCreateOp)rig.Journal.Entries[^1].Operation).Construct;
        rig.Pump(5);

        alice.Update(Input(primaryPressed: true), alice.AimAt(rig.Ground(2.5f) + new Vector3(0f, 0.5f, 0f)));
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 2));

        Assert.Equal(RejectReason.None, alice.Replies[1].Reason);
        var clear = Assert.IsType<BlockClearOp>(rig.Journal.Entries[^1].Operation);
        Assert.Equal(construct, clear.Construct);
        var applied = rig.Hooks.Results[^1].Result.Applied!;
        Assert.Contains(applied.ConstructChanges, c => c.Construct == construct && c.Removed);
        Assert.False(rig.Sim.State.Constructs.TryGet(construct, out _));
    }

    [Fact]
    public void Build_BeyondReach_RejectedOutOfReach()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var far = AddConstruct(rig, new Vector3(9f, 4001f, 0f));

        var reason = Raw(rig, alice, new PlaceBlockIntent(new ConstructFaceTarget(far, new CellPos(0, 0, 0), Face.PosX), 1, 0));

        Assert.Equal(RejectReason.OutOfReach, reason);
    }

    [Fact]
    public void Build_IntoOwnOrOtherCapsule_RejectedIntersectsPlayer()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var bob = rig.AddClient("bob", 2);
        var block = AddConstruct(rig, new Vector3(2f, 4000.05f, -0.5f));
        var place = new PlaceBlockIntent(new ConstructFaceTarget(block, new CellPos(0, 0, 0), Face.NegX), 1, 0);

        // The cell x in [1, 2] is free: stand in it with Alice, then with Bob.
        var cellFeet = new Vector3(1.5f, 4000.05f, 0f);
        MoveServerSide(rig, alice, cellFeet);
        MoveServerSide(rig, bob, rig.Ground(-4f));
        Assert.Equal(RejectReason.IntersectsPlayer, Raw(rig, alice, place));

        MoveServerSide(rig, alice, rig.Ground(-4f) + new Vector3(0f, 0f, 3f));
        MoveServerSide(rig, bob, cellFeet);
        Assert.Equal(RejectReason.IntersectsPlayer, Raw(rig, alice, place));
    }

    [Fact]
    public void Build_TouchingOtherConstructByFace_Accepted()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var a = AddConstruct(rig, new Vector3(2f, 4002f, 0f));
        AddConstruct(rig, new Vector3(3f, 4003f, 0f));

        // The new block (x in [2, 3], y in [4003, 4004]) touches the construct at x = 3 with a face only.
        var reason = Raw(rig, alice, new PlaceBlockIntent(new ConstructFaceTarget(a, new CellPos(0, 0, 0), Face.PosY), 1, 0));

        Assert.Equal(RejectReason.None, reason);
    }

    [Fact]
    public void Build_OverlappingOtherConstruct_RejectedCellOccupied()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var b = AddConstruct(rig, new Vector3(-6f, 4002f, 0f));
        AddConstruct(rig, new Vector3(-5.25f, 4003f, 0f));

        // The new block (x in [-6, -5], y in [4003, 4004]) overlaps the other construct by 0.25 m along x.
        var reason = Raw(rig, alice, new PlaceBlockIntent(new ConstructFaceTarget(b, new CellPos(0, 0, 0), Face.PosY), 1, 0));

        Assert.Equal(RejectReason.CellOccupied, reason);
        Assert.Equal(2, rig.Sim.State.Constructs.Count);
    }

    [Theory]
    [InlineData(3.15f, RejectReason.CellOccupied)]
    [InlineData(3.25f, RejectReason.None)]
    public void Build_Rotated45Offset115_RejectedCellOccupied(float x, RejectReason expected)
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        Assert.Equal(RejectReason.None, Raw(rig, alice, PlaceOnTerrain(rig, alice, rig.Ground(2f))));
        MoveServerSide(rig, alice, rig.Ground(5f) + new Vector3(0f, 0.1f, 0f));

        // Unit cubes 1.15 m apart, the second turned 45 degrees: no sample point of the second lies in the first, yet they intersect.
        var reason = Raw(rig, alice, PlaceOnTerrain(rig, alice, rig.Ground(x), Diagonal));

        Assert.Equal(expected, reason);
    }

    [Fact]
    public void Build_PlacementThroughControllerOnOverlap_ReportsCellOccupiedAndHint()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        Assert.Equal(RejectReason.None, Raw(rig, alice, PlaceOnTerrain(rig, alice, rig.Ground(2f))));
        MoveServerSide(rig, alice, rig.Ground(5f) + new Vector3(0f, 0.1f, 0f));
        var aim = alice.AimAt(rig.Ground(3.15f)) with { LookForward = Diagonal };
        alice.Update(Input(select: ToolKind.Builder), aim);

        alice.Update(Input(secondaryPressed: true), aim);
        var hint = alice.Controller.Preview.Hint;
        Assert.True(rig.RunUntil(() => alice.Tracker.Count == 0));

        Assert.Equal(RejectReason.CellOccupied, hint);
        Assert.Equal(RejectReason.CellOccupied, alice.Tracker.LastRejection);
    }

    private static void MoveServerSide(ToolRig rig, ToolRig.ToolClient client, Vector3 feet)
    {
        var state = client.State;
        Assert.True(rig.Sim.UpdatePlayerKinematics(client.Player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }
}
