using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// The refusals of the quick stack and take all leave early and cheaply (K6-30 review): no scan of every container of the world and no
/// allocation, however many containers there are.
/// </summary>
public sealed class QuickStackRejectionTests
{
    private const int ManyChests = 1200;

    [Fact]
    public void QuickStack_NothingToPutAway_NotEnoughItems_DoesNotAllocate_WithManyContainers()
    {
        var f = ForIntents();
        AddManyChests(f);
        var intent = new QuickStackIntent();
        Assert.Equal(RejectReason.NotEnoughItems, f.Submit(intent).Reason);

        var allocated = Measure(() => f.Submit(intent).Reason == RejectReason.NotEnoughItems);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void QuickStack_ItemsButNoChestInReach_NotEnoughItems_DoesNotAllocate_WithManyContainers()
    {
        var f = ForIntents();
        AddManyChests(f);
        f.Give(Bag, 0, 10);
        f.Move(Player1, new System.Numerics.Vector3(500f, 4001f, 500f));
        var intent = new QuickStackIntent();
        Assert.Equal(RejectReason.NotEnoughItems, f.Submit(intent).Reason);

        var allocated = Measure(() => f.Submit(intent).Reason == RejectReason.NotEnoughItems);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void QuickStack_RepeatedRefusalsAfterCopyingNearbyChests_RunIntoTheItemIntentLimit()
    {
        var f = ForIntents();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        f.Apply(new ItemCreateOp(new SlotRef(chest, 0), WorldFactory.Item(SurvivalContentFactory.GemKey), 1, 5, ItemSource.Admin));
        f.Give(Bag, 0, 10);
        var burst = f.Sim.Sim.Rules.ItemIntentBurst;

        // A chest in reach that holds no matching type: the refusal comes after the slots of the chest were copied, so it costs a token.
        var refused = 0;
        var limited = false;
        for (var i = 0; i < burst + 10 && !limited; i++)
        {
            var reason = f.Submit(new QuickStackIntent()).Reason;
            limited = reason == RejectReason.RateLimited;
            refused += reason == RejectReason.NotEnoughItems ? 1 : 0;
        }

        Assert.True(limited, "the refusals never ran into the limit");
        Assert.Equal(burst, refused);
    }

    [Fact]
    public void TakeAll_EmptyChest_NotEnoughItems_DoesNotAllocate()
    {
        var f = ForIntents();
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        var intent = new TakeAllIntent(chest);
        Assert.Equal(RejectReason.NotEnoughItems, f.Submit(intent).Reason);

        var allocated = Measure(() => f.Submit(intent).Reason == RejectReason.NotEnoughItems);

        Assert.Equal(0, allocated);
    }

    /// <summary>Many chest containers far from the player: the visited set must not decide the cost of the refusal.</summary>
    private static void AddManyChests(FunctionalBlockFixture f)
    {
        f.SetUpChestAt(FunctionalBlocks.ChestCell, Player1);
        for (var i = 0; i < ManyChests; i++)
        {
            f.SetUpChestAt(new CellPos(10 + (i % 25), 10 + ((i / 25) % 25), 20 + (i / 625)), Player1);
        }
    }

    /// <summary>Runs the submission until a round allocates nothing (the JIT promotes methods during the first rounds); returns the last round.</summary>
    private static long Measure(Func<bool> submit)
    {
        for (var i = 0; i < 100; i++)
        {
            submit();
        }

        var allocated = long.MaxValue;
        for (var round = 0; round < 60 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                Assert.True(submit());
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return allocated;
    }
}
