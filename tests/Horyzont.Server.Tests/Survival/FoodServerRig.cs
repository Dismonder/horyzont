using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Crafting;
using Horyzont.Server.Survival;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// A simulation with the food content (<see cref="WorldFactory.CreateWithFood"/>, two players online) with the vitals service and the progress replication of K6-33 wired to a list
/// of the messages they send and to a fake source of the motion of the bodies; no network and no runtime. A tick is <see cref="Tick"/>: the vitals service first, then
/// the simulation advances (the order of the server).
/// </summary>
internal sealed class FoodServerRig
{
    /// <summary>The cell of the seat of the rest tests: next to the bed, the lamp and the campfire cell of <see cref="Heat"/>.</summary>
    public static readonly CellPos SeatCell = FunctionalBlocks.FreeCell;

    public static readonly CellPos CampfireCell = new(4, 1, 0);

    public static readonly CellPos RoofCell = new(5, 4, 0);

    public FoodServerRig(VitalsRules? rules = null)
    {
        var world = WorldFactory.CreateWithFood(players: 2);
        Sim = new ServerSimulation(
            world, new FakeTerrainService(), Journal, new GameRules(), NullLogger<ServerSimulation>.Instance, FoodBlocks.ContentFor(world),
            SurvivalContentFactory.CreateTestWithFood(), rules);
        void Send(PlayerId player, Message message) => Sent.Add((player, message));
        Vitals = new VitalsService(() => Sim, Motion, Send, () => false);
        Progress = new ProgressReplication(() => Sim, Send);
    }

    public PlayerId Alice { get; } = new(1);

    public PlayerId Bob { get; } = new(2);

    public InMemoryJournal Journal { get; } = new();

    public ServerSimulation Sim { get; }

    public FakeMotion Motion { get; } = new();

    public VitalsService Vitals { get; }

    public ProgressReplication Progress { get; }

    public List<(PlayerId Player, Message Message)> Sent { get; } = [];

    public static ItemTypeId Berries => WorldFactory.Item(SurvivalContentFactory.BerriesKey);

    public PlayerVitals? VitalsOf(PlayerId player) => Sim.State.Players.TryGet(player, out var state) ? state.Vitals : null;

    public List<PlayerVitalsMessage> MessagesTo(PlayerId player) => [.. Sent.Where(s => s.Player == player).Select(s => s.Message).OfType<PlayerVitalsMessage>()];

    /// <summary>Runs <paramref name="ticks"/> ticks of the server: the vitals service, then the simulation advances.</summary>
    public void Tick(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Vitals.Step();
            Sim.AdvanceTick();
        }
    }

    /// <summary>Both players become ready to receive messages 44.</summary>
    public void ReadyBoth()
    {
        Vitals.OnPlayerReady(Alice);
        Vitals.OnPlayerReady(Bob);
    }

    public void Place(CellPos cell, ushort type) =>
        Sim.ApplyServerOperation(new BlockSetOp(FunctionalBlocks.Construct, cell, BlockState.Create(type, 0)));

    /// <summary>The surroundings of the rest: a roof 2 m above the seat and a campfire (heat) beside it; the bed and the lamp of the world make the comfort 3.</summary>
    public void Heat()
    {
        Place(CampfireCell, FoodBlocks.CampfireType);
        Place(RoofCell, FunctionalBlocks.StoneType);
    }

    /// <summary>Seats a player in <see cref="SeatCell"/> of the construct (its frame, the seat set).</summary>
    public void Seat(PlayerId player)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Place(SeatCell, FunctionalBlocks.StoneType);
        var local = new Vector3(SeatCell.X + 0.5f, SeatCell.Y + 0.5f, SeatCell.Z + 0.5f);
        Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, FrameRef.Of(FunctionalBlocks.Construct), state.ParentSeq + 1, local, Quaternion.Identity, Vector3.Zero, SeatCell));
    }

    /// <summary>Changes the frame counter of a player in place (a teleport or a reparent) without moving it.</summary>
    public void Reparent(PlayerId player)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, state.Frame, state.ParentSeq + 1, state.Position, state.BodyRotation, Vector3.Zero, null));
    }

    /// <summary>Moves a player on the planet by <paramref name="meters"/> along x.</summary>
    public void Move(PlayerId player, float meters)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Assert.True(Sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, state.Position + new Vector3(meters, 0f, 0f), state.BodyRotation, state.Pitch));
    }

    public sealed class FakeMotion : IVitalsMotionSource
    {
        private readonly Dictionary<PlayerId, VitalsMotion> _motion = [];

        public void Set(PlayerId player, MovementMode mode, Vector3 velocity = default, bool attached = false) =>
            _motion[player] = new VitalsMotion(mode, velocity, new Vector3(0f, 4000f, 0f), attached);

        public bool TryGetMotion(PlayerId player, out VitalsMotion motion) => _motion.TryGetValue(player, out motion);
    }
}
