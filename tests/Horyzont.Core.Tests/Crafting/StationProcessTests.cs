using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32 D15: the settlement of a station with time as a pure function of ticks.</summary>
public sealed class StationProcessTests
{
    private const ulong Duration = 60;
    private const ushort GemLimit = 5;

    private static readonly ItemTypeId _ore = CraftingFixture.Stone;
    private static readonly ItemTypeId _peat = CraftingFixture.Soil;
    private static readonly ItemTypeId _ingot = CraftingFixture.Gem;

    /// <summary>2 ore and 1 peat as fuel make 1 ingot.</summary>
    private static readonly StationRecipe _recipe = new([new StationEntry(_ore, 2)], [new StationEntry(_peat, 1)], new StationEntry(_ingot, 1));

    [Fact]
    public void Idle_WithInput_StartsNow()
    {
        var result = StationProcess.Settle(Input(4, 2), Output(), _recipe, Duration, null, new Tick(100), GemLimit);

        Assert.Equal(new StationSettlement(0, new Tick(100)), result);
    }

    [Fact]
    public void Idle_WithoutInput_StaysIdle()
    {
        // The counterpart with a full wsad starts; one ore is not enough for a piece, and peat without ore is nothing.
        Assert.Equal(new Tick(100), StationProcess.Settle(Input(2, 1), Output(), _recipe, Duration, null, new Tick(100), GemLimit).NextStarted);

        Assert.Equal(new StationSettlement(0, null), StationProcess.Settle(Input(1, 1), Output(), _recipe, Duration, null, new Tick(100), GemLimit));
        Assert.Equal(new StationSettlement(0, null), StationProcess.Settle(Input(0, 5), Output(), _recipe, Duration, null, new Tick(100), GemLimit));
        Assert.Equal(new StationSettlement(0, null), StationProcess.Settle(Input(0, 0), Output(), _recipe, Duration, null, new Tick(100), GemLimit));
    }

    [Fact]
    public void Running_TwoDurations_TwoDone_StartAdvanced()
    {
        // Started at 100, 130 ticks later: two pieces are done (120 ticks), the third has run for 10. The next start is 100 + 2 * 60, not "now".
        var result = StationProcess.Settle(Input(6, 3), Output(), _recipe, Duration, new Tick(100), new Tick(230), GemLimit);

        Assert.Equal(new StationSettlement(2, new Tick(220)), result);
    }

    [Fact]
    public void Running_LessThanOneDuration_NothingDone_StartKept()
    {
        var result = StationProcess.Settle(Input(6, 3), Output(), _recipe, Duration, new Tick(100), new Tick(159), GemLimit);

        Assert.Equal(new StationSettlement(0, new Tick(100)), result);
        Assert.Equal(1, StationProcess.Settle(Input(6, 3), Output(), _recipe, Duration, new Tick(100), new Tick(160), GemLimit).Done);
    }

    [Fact]
    public void Running_InputRunsOut_DoneLimited_Idle()
    {
        // Wsad for two pieces, ten durations later: two pieces, and the station is idle afterwards.
        var result = StationProcess.Settle(Input(4, 2), Output(), _recipe, Duration, new Tick(100), new Tick(700), GemLimit);

        Assert.Equal(new StationSettlement(2, null), result);
    }

    [Fact]
    public void Running_OutputFull_DoneLimitedByRoom_Idle()
    {
        // The output holds 4 of 5 ingots: room for one piece only. The counterpart with an empty output makes all three.
        Assert.Equal(3, StationProcess.Settle(Input(20, 20), Output(), _recipe, Duration, new Tick(100), new Tick(700), GemLimit).Done);

        var result = StationProcess.Settle(Input(20, 20), Output(new ItemStack(_ingot, 4)), _recipe, Duration, new Tick(100), new Tick(700), GemLimit);

        Assert.Equal(new StationSettlement(1, null), result);
    }

    [Fact]
    public void Running_FuelMissing_NoProgress()
    {
        // The counterpart with fuel makes a piece; without peat nothing is done and the station stops.
        Assert.Equal(1, StationProcess.Settle(Input(4, 1), Output(), _recipe, Duration, new Tick(100), new Tick(160), GemLimit).Done);

        var result = StationProcess.Settle(Input(4, 0), Output(), _recipe, Duration, new Tick(100), new Tick(700), GemLimit);

        Assert.Equal(new StationSettlement(0, null), result);
    }

    [Fact]
    public void Progress_HalfDuration_Half()
    {
        Assert.Equal(0.5, StationProcess.Progress(new Tick(100), new Tick(130), Duration), 6);
        Assert.Equal(0.0, StationProcess.Progress(new Tick(100), new Tick(100), Duration), 6);

        // Past one duration the bar starts over for the next piece.
        Assert.Equal(0.5, StationProcess.Progress(new Tick(100), new Tick(250), Duration), 6);
    }

    [Fact]
    public void Settle_NoAllocation()
    {
        var input = Input(6, 3);
        var output = Output(new ItemStack(_ingot, 1));
        var recipe = _recipe;
        for (var i = 0; i < 50; i++)
        {
            StationProcess.Settle(input, output, recipe, Duration, new Tick(100), new Tick(230), GemLimit);
        }

        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 500; i++)
            {
                StationProcess.Settle(input, output, recipe, Duration, new Tick(100), new Tick(230), GemLimit);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }

    /// <summary>Input slots: the ore in slot 0, the peat in slot 1 and an unrelated stack in slot 2.</summary>
    private static ItemStack[] Input(int ore, int peat) =>
    [
        ore == 0 ? ItemStack.Empty : new ItemStack(_ore, (ushort)ore),
        peat == 0 ? ItemStack.Empty : new ItemStack(_peat, (ushort)peat),
        new ItemStack(CraftingFixture.Tool, 1),
    ];

    private static ItemStack[] Output(params ItemStack[] stacks) => [.. stacks, .. Enumerable.Repeat(ItemStack.Empty, 2 - stacks.Length)];
}
