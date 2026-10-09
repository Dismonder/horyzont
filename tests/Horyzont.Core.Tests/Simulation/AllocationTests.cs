using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.Simulation;

public sealed class AllocationTests
{
    // BlockClearOp, AppliedOperation, its one-element ConstructChange list and JournalEntry: about 220 bytes on x64.
    private const long BudgetPerRemoval = 320;

    // Measured: 69_056 B for the four accepted placements of PlacementsFor (stable between runs; ~16 KiB chunk of the new Construct plus
    // ConstructCreateOp, AppliedOperation with its change list and JournalEntry per placement), rounded up to the next 256 B.
    private const long BudgetForFourPlacements = 69_120;

    private static readonly CellPos[] _cells = [new(0, 0, 0), new(0, 0, 3), new(0, 0, -3), new(0, 0, 6), new(0, 0, -6)];

    private sealed class NullJournal : IJournalSink
    {
        public void Append(JournalEntry entry)
        {
        }
    }

    private static (SimFixture Fixture, PlayerId Player, EntityId Construct) Arrange()
    {
        var f = new SimFixture(journal: new NullJournal());
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        foreach (var cell in _cells[1..])
        {
            f.SetBlock(c, cell);
        }

        return (f, p, c);
    }

    private static PlaceBlockIntent[] PlacementsFor(SimFixture f, PlayerId p) =>
        [f.PlaceOnTerrain(p, f.Ground(-2)), f.PlaceOnTerrain(p, f.Ground(0, 3)), f.PlaceOnTerrain(p, f.Ground(0, -3)), f.PlaceOnTerrain(p, f.Ground(-3, 3))];

    private static int RunPlacements(SimFixture f, PlayerId p, PlaceBlockIntent[] intents)
    {
        var accepted = 0;
        foreach (var intent in intents)
        {
            for (var t = 0; t < 6; t++)
            {
                f.Sim.AdvanceTick();
            }

            if (f.Sim.Submit(p, intent).IsAccepted)
            {
                accepted++;
            }
        }

        return accepted;
    }

    private static int Run(SimFixture f, PlayerId p, RemoveBlockIntent[] intents)
    {
        var accepted = 0;
        foreach (var intent in intents)
        {
            for (var t = 0; t < 6; t++)
            {
                f.Sim.AdvanceTick();
            }

            if (f.Sim.Submit(p, intent).IsAccepted)
            {
                accepted++;
            }
        }

        return accepted;
    }

    [Fact]
    public void SubmitRemoveAndAdvanceTick_SteadyState_AllocateOnlyOperationRecords()
    {
        // Warm-up on an identical world so JIT and lazy statics do not count.
        var (warm, warmPlayer, warmConstruct) = Arrange();
        Run(warm, warmPlayer, _cells.Select(cell => new RemoveBlockIntent(warmConstruct, cell)).ToArray());

        var (f, p, c) = Arrange();
        var intents = _cells[1..].Select(cell => new RemoveBlockIntent(c, cell)).ToArray();
        Assert.True(f.Sim.Submit(p, new RemoveBlockIntent(c, _cells[0])).IsAccepted);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = Run(f, p, intents);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(intents.Length, accepted);
        Assert.InRange(allocated, 0, intents.Length * BudgetPerRemoval);
    }

    [Fact]
    public void SubmitPlaceOnTerrainBesideConstruct_SteadyState_AllocatesOnlyOperationRecords()
    {
        // Validation, including the W2 overlap test against the neighbours, allocates nothing; the new construct itself does.
        var (warm, warmPlayer, _) = Arrange();
        RunPlacements(warm, warmPlayer, PlacementsFor(warm, warmPlayer));

        var (f, p, _) = Arrange();
        var intents = PlacementsFor(f, p);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var accepted = RunPlacements(f, p, intents);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(4, intents.Length);
        Assert.Equal(intents.Length, accepted);
        Assert.InRange(allocated, 0, BudgetForFourPlacements);
    }

    [Fact]
    public void SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate()
    {
        var (f, p, a) = Arrange();
        var target = f.State.Constructs.TryGet(a, out var construct) ? construct : throw new InvalidOperationException();
        var newCenter = target.CellCenterToParent(new CellPos(0, 1, 0));
        f.Sim.ApplyServerOperation(new ConstructCreateOp(
            f.State.EntityIds.PeekNext(), FrameRef.Of(SimFixture.Planet), newCenter - new Vector3(0.5f, 0.5f, 0.5f) + new Vector3(0.75f, 0f, 0f),
            Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(f.Stone, 0)));
        var intent = new PlaceBlockIntent(new ConstructFaceTarget(a, new CellPos(0, 0, 0), Face.PosY), f.Stone, 0);
        Assert.Equal(RejectReason.CellOccupied, f.Sim.Submit(p, intent).Reason);
        for (var i = 0; i < 100; i++)
        {
            f.Sim.Submit(p, intent);
        }

        // Steady state is reached when a round allocates nothing: in a busy run the JIT promotes the methods (tier-up) during the first
        // rounds. A per-call allocation never reaches zero.
        var allocated = long.MaxValue;
        var rejected = 0;
        for (var round = 0; round < 60 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            rejected = 0;
            for (var i = 0; i < 1000; i++)
            {
                rejected += f.Sim.Submit(p, intent).Reason == RejectReason.CellOccupied ? 1 : 0;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(1000, rejected);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void AdvanceTick_SteadyState_DoesNotAllocate()
    {
        var (f, p, _) = Arrange();
        f.Sim.Submit(p, f.Dig(p, f.Ground(-2)));
        f.Ticks(10);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            f.Sim.AdvanceTick();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
