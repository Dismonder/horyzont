using FsCheck.Fluent;

using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.Serialization;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, D12: the burning of fuel cells by an oxygen generator as pure functions of ticks. One cell burns 600 ticks in these tests (10 s at 60 ticks per second).</summary>
public sealed class OxygenGeneratorProcessTests
{
    private const ulong PerCell = 600;

    private static readonly ItemTypeId _fuel = new(13);

    private static Tick T(ulong value) => new(value);

    [Fact]
    public void CellsIn_CountsOnlyFuel()
    {
        ItemStack[] input = [new(_fuel, 3), new(new ItemTypeId(3), 5), default, new(_fuel, 2)];

        Assert.Equal(5, OxygenGeneratorProcess.CellsIn(input, _fuel));
        Assert.Equal(0, OxygenGeneratorProcess.CellsIn([], _fuel));
        Assert.Equal(0, OxygenGeneratorProcess.CellsIn(input, new ItemTypeId(14)));
    }

    [Fact]
    public void Settle_IdleWithCells_StartsNow()
    {
        var settlement = OxygenGeneratorProcess.Settle(2, PerCell, null, T(1000));

        Assert.Equal(new GeneratorSettlement(1, T(1000)), settlement);
    }

    [Fact]
    public void Settle_IdleWithoutCells_StaysIdle()
    {
        Assert.Equal(new GeneratorSettlement(0, null), OxygenGeneratorProcess.Settle(0, PerCell, null, T(1000)));
    }

    [Fact]
    public void Settle_BurnsWholeCellsOnly()
    {
        // Three queued cells plus the cell already consumed at start; only whole periods start another queued cell.
        Assert.Equal(new GeneratorSettlement(0, T(1000)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(1000)));
        Assert.Equal(new GeneratorSettlement(0, T(1000)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(1599)));
        Assert.Equal(new GeneratorSettlement(1, T(1600)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(1600)));
        Assert.Equal(new GeneratorSettlement(1, T(1600)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(2199)));
        Assert.Equal(new GeneratorSettlement(2, T(2200)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(2200)));

        // A time before the start burns nothing.
        Assert.Equal(new GeneratorSettlement(0, T(1000)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(500)));
    }

    [Fact]
    public void Settle_AllBurned_Idle()
    {
        Assert.Equal(new GeneratorSettlement(3, null), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(3400)));
        Assert.Equal(new GeneratorSettlement(3, null), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(500_000)));
        Assert.Equal(new GeneratorSettlement(2, null), OxygenGeneratorProcess.Settle(2, PerCell, T(0), T(1800)));

        // With no queued cells the prepaid cell continues until its deadline.
        Assert.Equal(new GeneratorSettlement(0, T(1000)), OxygenGeneratorProcess.Settle(0, PerCell, T(1000), T(1100)));
        Assert.Equal(new GeneratorSettlement(0, null), OxygenGeneratorProcess.Settle(0, PerCell, T(1000), T(1600)));
        Assert.Equal(new GeneratorSettlement(3, T(2800)), OxygenGeneratorProcess.Settle(3, PerCell, T(1000), T(2800)));
    }

    [Fact]
    public void Settle_IndependentOfCallFrequency()
    {
        var gen =
            from cells in Gen.Choose(1, 6)
            from perCell in Gen.Choose(1, 1000)
            from start in Gen.Choose(0, 5000)
            from steps in Gen.Choose(1, 12).SelectMany(n => Gen.Choose(0, 3000).ArrayOf(n))
            select (cells, perCell: (ulong)perCell, start: (ulong)start, steps);

        Kit.ForAll(gen, c =>
        {
            // Settle at every moment of the series, taking the burned cells out of the input and moving the start, as the server does.
            var cells = c.cells;
            Tick? started = T(c.start);
            var burnedTotal = 0;
            var now = c.start;
            foreach (var step in c.steps)
            {
                now += (ulong)step;
                var settlement = OxygenGeneratorProcess.Settle(cells, c.perCell, started, T(now));
                cells -= settlement.Burned;
                burnedTotal += settlement.Burned;
                started = settlement.NextStarted;
            }

            // One settlement at the last moment gives the same cells and the same start.
            var once = OxygenGeneratorProcess.Settle(c.cells, c.perCell, T(c.start), T(now));
            Assert.Equal(once.Burned, burnedTotal);
            Assert.Equal(once.NextStarted, started);
        });
    }

    [Fact]
    public void IsRunning_UntilLastCellEnds()
    {
        // One prepaid cell plus two queued cells run until 2800 (exclusive).
        Assert.True(OxygenGeneratorProcess.IsRunning(2, PerCell, T(1000), T(1000)));
        Assert.True(OxygenGeneratorProcess.IsRunning(2, PerCell, T(1000), T(1999)));
        Assert.True(OxygenGeneratorProcess.IsRunning(2, PerCell, T(1000), T(2799)));
        Assert.False(OxygenGeneratorProcess.IsRunning(2, PerCell, T(1000), T(2800)));
        Assert.False(OxygenGeneratorProcess.IsRunning(2, PerCell, T(1000), T(9999)));

        // Without a start there is no prepaid cell; with a start and no queue it still runs.
        Assert.False(OxygenGeneratorProcess.IsRunning(2, PerCell, null, T(1000)));
        Assert.True(OxygenGeneratorProcess.IsRunning(0, PerCell, T(1000), T(1000)));
    }

    [Fact]
    public void Multiply_Saturates()
    {
        // cells * ticksPerCell and started + that overflow ulong: they must saturate instead of wrapping to a small number that would stop the generator.
        const ulong huge = ulong.MaxValue / 2;

        Assert.True(OxygenGeneratorProcess.IsRunning(3, huge, T(10), T(ulong.MaxValue - 1)));
        Assert.True(OxygenGeneratorProcess.IsRunning(int.MaxValue, ulong.MaxValue, T(ulong.MaxValue - 5), T(ulong.MaxValue - 1)));
        Assert.Equal(1UL, OxygenGeneratorProcess.RemainingTicks(3, huge, T(10), T(ulong.MaxValue - 1)));
        Assert.Equal(new GeneratorSettlement(0, T(10)), OxygenGeneratorProcess.Settle(3, huge, T(10), T(1_000_000)));
        Assert.Equal(new GeneratorSettlement(1, T(10 + huge)), OxygenGeneratorProcess.Settle(3, huge, T(10), T(10 + huge)));
    }

    [Fact]
    public void RemoveLastCellMidBurn_GivesNoFreeTime()
    {
        var first = OxygenGeneratorProcess.Settle(1, PerCell, null, T(1000));
        Assert.Equal(1, first.Burned);
        Assert.True(OxygenGeneratorProcess.IsRunning(0, PerCell, first.NextStarted, T(1599)));
        Assert.False(OxygenGeneratorProcess.IsRunning(0, PerCell, first.NextStarted, T(1600)));
        Assert.Equal(new GeneratorSettlement(0, null), OxygenGeneratorProcess.Settle(0, PerCell, first.NextStarted, T(1600)));
    }

    [Fact]
    public void NegativeCells_Throws()
    {
        Assert.True(OxygenGeneratorProcess.IsRunning(0, PerCell, T(0), T(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OxygenGeneratorProcess.Settle(-1, PerCell, T(0), T(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OxygenGeneratorProcess.IsRunning(-1, PerCell, T(0), T(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OxygenGeneratorProcess.RemainingTicks(-1, PerCell, T(0), T(10)));
    }

    [Fact]
    public void TicksPerCellZero_Throws()
    {
        Assert.True(OxygenGeneratorProcess.IsRunning(1, 1, T(0), T(0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => OxygenGeneratorProcess.Settle(1, 0, T(0), T(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OxygenGeneratorProcess.IsRunning(1, 0, T(0), T(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => OxygenGeneratorProcess.RemainingTicks(1, 0, T(0), T(10)));
    }

    [Fact]
    public void RemainingTicks_Counts()
    {
        Assert.Equal(1800UL, OxygenGeneratorProcess.RemainingTicks(2, PerCell, T(1000), T(1000)));
        Assert.Equal(1300UL, OxygenGeneratorProcess.RemainingTicks(2, PerCell, T(1000), T(1500)));
        Assert.Equal(1UL, OxygenGeneratorProcess.RemainingTicks(2, PerCell, T(1000), T(2799)));
        Assert.Equal(0UL, OxygenGeneratorProcess.RemainingTicks(2, PerCell, T(1000), T(2800)));
        Assert.Equal(1700UL, OxygenGeneratorProcess.RemainingTicks(3, PerCell, T(1000), T(1700)));

        // Idle, without cells and before the start (a start in the future) is 0 or the whole time; only the first two are specified here.
        Assert.Equal(0UL, OxygenGeneratorProcess.RemainingTicks(2, PerCell, null, T(1500)));
        Assert.Equal(600UL, OxygenGeneratorProcess.RemainingTicks(0, PerCell, T(1000), T(1000)));
    }
}
