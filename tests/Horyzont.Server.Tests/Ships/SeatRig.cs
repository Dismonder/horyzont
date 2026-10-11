using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>Players, seats and buffered pilot inputs on top of <see cref="FlightRig"/> (K5-25 server tests).</summary>
internal sealed class SeatRig
{
    public static readonly CellPos Cockpit = new(2, 2, 0);
    public static readonly CellPos SeatA = new(1, 2, 1);
    public static readonly CellPos SeatB = new(3, 2, 1);
    public static readonly CellPos Nav = new(2, 2, 1);

    public SeatRig(double heightOverGround = 0, IShipControlSource? control = null, bool guard = true, IShipTerrainGate? gate = null)
    {
        Flight = new FlightRig(heightOverGround, gate: gate, control: control);
        Inputs = new FakeShipInputs();
        Pilot = new PilotControlSource(Flight.Sim, Flight.Stats, Inputs, Flight.Logger);
        if (control is null)
        {
            Flight.Flight.Control = Pilot;
        }

        Guard = new ShipLaunchGuard(Flight.Sim, Flight.Stats, Flight.Logger);
        if (guard)
        {
            Flight.Flight.LaunchGuard = Guard;
        }

        Assert.True(Flight.Flight.SetFuel(FlightRig.Ship, 2000f));
    }

    public FlightRig Flight { get; }

    public FakeShipInputs Inputs { get; }

    public PilotControlSource Pilot { get; }

    public ShipLaunchGuard Guard { get; }

    public PlayerState Player(PlayerId id) => Flight.Sim.State.Players.TryGet(id, out var p) ? p : throw new InvalidOperationException();

    public PlayerId Join(ulong identity)
    {
        var op = Flight.Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), $"p{identity}");
        Flight.Sim.ApplyServerOperation(op);
        return op.Player;
    }

    public void Sit(PlayerId id, CellPos cell)
    {
        var player = Player(id);
        var ship = Flight.Construct();
        var orientation = ship.GetBlock(cell).Orientation;
        Flight.Sim.ApplyServerOperation(new EntityReparentOp(
            player.Entity, FrameRef.Of(FlightRig.Ship), player.ParentSeq + 1, SeatPose.Feet(cell, orientation, SeatRules.Default),
            SeatPose.Rotation(orientation), Vector3.Zero, cell));
    }

    /// <summary>Puts a standing player at a point given in ship space (the ship stands in the planet frame).</summary>
    public void StandAt(PlayerId id, Vector3 shipLocal)
    {
        var player = Player(id);
        var feet = Flight.Construct().LocalToParent(shipLocal);
        Flight.Sim.ApplyServerOperation(new EntityReparentOp(
            player.Entity, Flight.Construct().Parent, player.ParentSeq + 1, feet, Quaternion.Identity, Vector3.Zero, null));
    }

    public void Leave(PlayerId id)
    {
        var p = Player(id);
        Flight.Sim.ApplyServerOperation(new PlayerLeaveOp(id, p.Frame, p.ParentSeq, p.Position, p.BodyRotation, 0f));
    }

    public void Ticks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Flight.Sim.AdvanceTick();
            Flight.Flight.Step();
        }
    }

    public static ShipInput Input(uint seq, ShipControl control) => new(seq, control);
}

/// <summary>Pilot inputs per player with the slot rules of the session buffer (K5-25 R1); allocation-free after warm-up.</summary>
internal sealed class FakeShipInputs : IShipInputSource
{
    private readonly Dictionary<PlayerId, Buffer> _buffers = [];

    public void Add(PlayerId player, uint seq, ShipControl control) => Of(player).Add(seq, new ShipInput(seq, control));

    public int Count(PlayerId player) => Of(player).Inputs.Count;

    public bool TryTakeShipInput(PlayerId player, uint slot, out ShipInput input) => Of(player).TakeSlot(slot, out input);

    public bool TryPeekNewestShipSeq(PlayerId player, out uint seq) => Of(player).PeekNewest(out seq);

    public int ShipInputLateCount(PlayerId player) => Of(player).Late;

    public void ClearShipInputs(PlayerId player) => Of(player).Clear();

    private Buffer Of(PlayerId player)
    {
        if (!_buffers.TryGetValue(player, out var buffer))
        {
            buffer = new Buffer();
            _buffers.Add(player, buffer);
        }

        return buffer;
    }

    private sealed class Buffer
    {
        public Dictionary<uint, ShipInput> Inputs { get; } = new(4096);

        public bool HasTaken { get; private set; }

        public uint LastTaken { get; private set; }

        public int Late { get; private set; }

        public void Add(uint seq, ShipInput input)
        {
            if (HasTaken && seq <= LastTaken)
            {
                Late++;
                return;
            }

            Inputs[seq] = input;
        }

        public bool TakeSlot(uint slot, out ShipInput input)
        {
            var found = Inputs.Remove(slot, out input);
            foreach (var key in Inputs.Keys)
            {
                if (key < slot)
                {
                    Inputs.Remove(key);
                }
            }

            if (!HasTaken || slot > LastTaken)
            {
                HasTaken = true;
                LastTaken = slot;
            }

            return found;
        }

        public bool PeekNewest(out uint seq)
        {
            seq = 0;
            foreach (var key in Inputs.Keys)
            {
                seq = Math.Max(seq, key);
            }

            return Inputs.Count > 0;
        }

        public void Clear()
        {
            Inputs.Clear();
            HasTaken = false;
            LastTaken = 0;
            Late = 0;
        }
    }
}
