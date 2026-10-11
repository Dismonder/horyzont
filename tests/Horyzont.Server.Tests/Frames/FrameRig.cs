using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Server.Frames;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Frames;

/// <summary>A simulation over a <see cref="TestShips"/> world with the frame graph, the server frame history and the transitions.</summary>
internal sealed class FrameRig
{
    public FrameRig(WorldState? state = null)
    {
        State = state ?? TestShips.CreateDefault();
        Journal = new InMemoryJournal();
        Sim = new ServerSimulation(State, new FakeTerrainService(), Journal, new GameRules(), NullLogger<ServerSimulation>.Instance);
        Graph = new FrameGraph(new WorldStateFramePoses(State, new WorldStatePlanetPlacement(State)));
        History = new ServerFrameHistory(State, Graph);
        Transitions = new FrameTransitions(Sim, History);
    }

    public WorldState State { get; }

    public InMemoryJournal Journal { get; }

    public ServerSimulation Sim { get; }

    public FrameGraph Graph { get; }

    public ServerFrameHistory History { get; }

    public FrameTransitions Transitions { get; }

    public ShipCircleScript Script { get; } = new(new Vector3(0f, 4010f, 0f), 100f);

    public double TickSeconds => 1.0 / Sim.Rules.TickRate;

    /// <summary>Ticks like the server: the ship moves (phase Frames), then the history records (phase Compose), then the tick advances.</summary>
    public void Tick(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            if (State.Constructs.TryGet(TestShips.Moving, out _))
            {
                Script.Step(Sim, TestShips.Moving);
            }

            History.Step(State.CurrentTick);
            Sim.AdvanceTick();
        }
    }

    public PlayerState Join(ulong identity = 1)
    {
        var op = Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), "player");
        Sim.ApplyServerOperation(op);
        return State.Players.TryGet(op.Player, out var player) ? player : throw new InvalidOperationException();
    }

    public PoseD WorldPose(PlayerState player) =>
        Graph.TryGetWorldPose(player.Frame, out var frame)
            ? FrameMath.Compose(frame, FrameMath.FromFloat(player.Position, player.BodyRotation))
            : throw new InvalidOperationException("no frame pose");

    public PoseD WorldPose(EntityId construct) =>
        Graph.TryGetWorldPose(FrameRef.Of(construct), out var pose) ? pose : throw new InvalidOperationException("no frame pose");

    /// <summary>World velocity of a player at rest in its frame (the velocity of the frame point it occupies).</summary>
    public Vector3D RestVelocity(PlayerState player) =>
        History.PointVelocity(player.Frame, Vector3D.FromFloat(player.Position), TickSeconds);
}
