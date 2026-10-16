using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Tests.Serialization;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>
/// K6-32: "the furnace follows the ticks". For a running station, settling at any set of intermediate ticks gives the same total of finished pieces
/// and the same final state (input, output, start) as one settlement at the last tick.
/// </summary>
public sealed class StationProcessProperties
{
    private const int Cases = 2000;
    private const ushort ResultLimit = 5;

    private static readonly ItemTypeId _ore = CraftingFixture.Stone;
    private static readonly ItemTypeId _peat = CraftingFixture.Soil;
    private static readonly ItemTypeId _ingot = CraftingFixture.Gem;

    private static readonly StationRecipe _recipe = new(
        [new StationEntry(_ore, 3), new StationEntry(CraftingFixture.Tool, 1)], [new StationEntry(_peat, 2)], new StationEntry(_ingot, 2));

    private static Gen<Case> CaseGen { get; } =
        from ore in Gen.Choose(0, 40)
        from tools in Gen.Choose(0, 12)
        from peat in Gen.Choose(0, 30)
        from outputA in Gen.Frequency((3, Gen.Constant(0)), (1, Gen.Choose(0, ResultLimit)))
        from outputB in Gen.Frequency((3, Gen.Constant(0)), (1, Gen.Choose(0, ResultLimit)))
        from outputC in Gen.Frequency((3, Gen.Constant(0)), (1, Gen.Choose(0, ResultLimit)))
        from duration in Gen.Choose(1, 600)
        from started in Gen.Choose(0, 1000)
        from whole in Gen.Frequency((6, Gen.Choose(0, 12)), (1, Gen.Choose(0, 100_000 / 600)))
        from part in Gen.Choose(0, duration - 1)
        from cuts in Gen.Choose(0, 1000).ArrayOf(6)
        select new Case(ore, tools, peat, [outputA, outputB, outputC], duration, started, (whole * duration) + part, cuts);

    [Fact]
    public void SettlingAtAnyIntermediateTicks_EqualsOneSettlement()
    {
        Kit.ForAll(CaseGen, Check, Cases);

        // A property over cases in which nothing is ever finished holds trivially: the generator must reach pieces and limits.
        AssertCoverage();
    }

    [Fact]
    public void Generator_ReachesPiecesAndBothLimits() => AssertCoverage();

    private static void AssertCoverage()
    {
        int manyPieces = 0, idleAtEnd = 0, total = 0;
        foreach (var c in CaseGen.Sample(400, 20))
        {
            var final = Run(c, [c.Elapsed]);
            total++;
            manyPieces += final.Done >= 3 ? 1 : 0;
            idleAtEnd += final.Started is null ? 1 : 0;
        }

        Assert.True(manyPieces >= 80, $"{manyPieces} of {total} cases made three pieces or more");
        Assert.True(idleAtEnd >= 20 && idleAtEnd <= total - 20, $"{idleAtEnd} of {total} cases ended idle");
    }

    private static void Check(Case c)
    {
        var once = Run(c, [c.Elapsed]);
        var cuts = c.CutsPerMille.Select(x => (int)((long)c.Elapsed * x / 1000)).Order().Append(c.Elapsed).ToArray();
        var stepwise = Run(c, cuts);

        Assert.Equal(once.Done, stepwise.Done);
        Assert.Equal(once.Started, stepwise.Started);
        Assert.Equal(once.Input, stepwise.Input);
        Assert.Equal(once.Output, stepwise.Output);
    }

    /// <summary>Settles at the given offsets from the start (ascending), applying every settlement to the slots.</summary>
    private static Final Run(Case c, int[] offsets)
    {
        var input = new[]
        {
            Stack(_ore, c.Ore), Stack(CraftingFixture.Tool, c.Tools), Stack(_peat, c.Peat),
        };
        var output = c.Output.Select(n => Stack(_ingot, n)).ToArray();
        Tick? started = new Tick((ulong)c.Started);
        var done = 0;
        foreach (var offset in offsets)
        {
            var now = new Tick((ulong)(c.Started + offset));
            var settlement = StationProcess.Settle(input, output, _recipe, (ulong)c.Duration, started, now, ResultLimit);
            Assert.True(settlement.Done >= 0);
            for (var piece = 0; piece < settlement.Done; piece++)
            {
                Take(input, _ore, 3);
                Take(input, CraftingFixture.Tool, 1);
                Take(input, _peat, 2);
                Put(output, _ingot, 2);
            }

            done += settlement.Done;
            started = settlement.NextStarted;
        }

        return new Final(done, started, input.Select(s => (s.Type.Value, s.Count)).ToArray(), output.Select(s => (s.Type.Value, s.Count)).ToArray());
    }

    private static ItemStack Stack(ItemTypeId type, int count) => count == 0 ? ItemStack.Empty : new ItemStack(type, (ushort)count);

    private static void Take(ItemStack[] slots, ItemTypeId type, int count)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Type == type)
            {
                Assert.True(slots[i].Count >= count, "the settlement made more pieces than the input allows");
                slots[i] = Stack(type, slots[i].Count - count);
                return;
            }
        }

        Assert.Fail("the settlement made a piece without an ingredient");
    }

    /// <summary>Adds items to the first slots that take them: stacks of the type up to the limit, then empty slots.</summary>
    private static void Put(ItemStack[] slots, ItemTypeId type, int count)
    {
        for (var i = 0; i < slots.Length && count > 0; i++)
        {
            if (slots[i].IsEmpty || slots[i].Type == type)
            {
                var room = ResultLimit - slots[i].Count;
                var add = Math.Min(room, count);
                slots[i] = Stack(type, slots[i].Count + add);
                count -= add;
            }
        }

        Assert.Equal(0, count);
    }

    private sealed record Case(int Ore, int Tools, int Peat, int[] Output, int Duration, int Started, int Elapsed, int[] CutsPerMille);

    private sealed record Final(int Done, Tick? Started, (ushort Type, ushort Count)[] Input, (ushort Type, ushort Count)[] Output);
}
