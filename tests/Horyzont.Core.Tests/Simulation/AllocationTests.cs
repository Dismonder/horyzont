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
