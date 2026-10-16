using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Props;

/// <summary>
/// A <see cref="SimFixture"/> over <see cref="WorldFactory.CreateWithPlayerContainers"/> with helpers for the prop operations of K6-31.
/// Identifiers are packed here by hand (<see cref="Raw"/>), following the table of D4, so the operation tests do not depend on <c>PropId.Create</c>.
/// </summary>
internal sealed class PropFixture
{
    public PropFixture(int players = 1, FakeTerrainService? terrain = null, WorldState? state = null)
    {
        Sim = new SimFixture(terrain: terrain, state: state ?? WorldFactory.CreateWithPlayerContainers(players));
    }

    public static PlanetId Planet => WorldFactory.HomePlanet;

    public SimFixture Sim { get; }

    public WorldState State => Sim.State;

    /// <summary>
    /// Packs an identifier by the table of D4: face in bits 60-58, region U 57-41, region V 40-24, cell U in the region 23-21, cell V 20-18, slot 17-16.
    /// </summary>
    public static PropId Raw(int face, int cellU, int cellV, int slot) => new(
        ((ulong)face << 58) | ((ulong)(cellU >> 3) << 41) | ((ulong)(cellV >> 3) << 24) | ((ulong)(cellU & 7) << 21) | ((ulong)(cellV & 7) << 18)
        | ((ulong)slot << 16));

    /// <summary>The n-th tree slot of a row of cells in one region of face 0 (slot 0).</summary>
    public static PropId Tree(int n) => Raw(0, 16 + (n % 8), 16 + (n / 8), 0);

    /// <summary>The n-th rock slot of a row of cells in one region of face 0 (slot 1).</summary>
    public static PropId Rock(int n) => Raw(0, 16 + (n % 8), 16 + (n / 8), 1);

    public static PropHarvestOp Harvest(PropId prop, ulong tick = 100) => new(Planet, prop, new Tick(tick));

    public static PropRegrowOp Regrow(PropId prop) => new(Planet, prop);

    public AppliedOperation Apply(Operation op) => Sim.Sim.ApplyServerOperation(op);

    public bool IsHarvested(PropId prop) => State.HarvestedProps.Contains(Planet, prop);

    /// <summary>The operation is refused: it throws, the hash, LastOpId and journal do not change and the simulation is not faulted.</summary>
    public void AssertFails(Operation op)
    {
        var hash = StateHasher.Hash(State);
        var last = State.LastOpId;
        var journaled = Sim.Journal.Entries.Count;
        var harvested = State.HarvestedProps.All.ToArray();

        Assert.Throws<InvalidOperationException>(() => Sim.Sim.ApplyServerOperation(op));

        Assert.Equal(hash, StateHasher.Hash(State));
        Assert.Equal(last, State.LastOpId);
        Assert.Equal(journaled, Sim.Journal.Entries.Count);
        Assert.Equal(harvested, State.HarvestedProps.All);
        Assert.False(Sim.Sim.IsFaulted);
    }

    /// <summary>Runs the applier directly, without journaling; use it only for operations that are expected to fail.</summary>
    public bool TryApplyRaw(Operation op, out AppliedOperation? applied, out string failure, out int componentIndex) =>
        new OperationApplier(State, Sim.Terrain).TryApply(State.LastOpId.Next, State.CurrentTick, null, op, out applied, out failure, out componentIndex);
}
