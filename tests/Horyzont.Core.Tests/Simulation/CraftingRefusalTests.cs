using System.Numerics;

using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Crafting.CraftingWorld;

namespace Horyzont.Core.Tests.Simulation;

public sealed class CraftingRefusalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Craft_RefusalsAfterChestSearch_RunIntoItemLimit(bool nearby)
    {
        var f = ForCrafting();
        f.SetUpChestWith(FunctionalBlocks.ChestCell, Player1, ContainerAccess.Shared, (Soil, 1));
        f.Discover(Player1, Stone);
        if (!nearby)
        {
            f.Move(Player1, new Vector3(500f, 4001f, 500f));
        }

        var intent = Craft("t:hand");
        for (var i = 0; i < f.Sim.Sim.Rules.ItemIntentBurst; i++)
        {
            Assert.Equal(RejectReason.NotEnoughItems, f.Submit(intent).Reason);
        }

        Assert.Equal(RejectReason.RateLimited, f.Submit(intent).Reason);
        f.Wait(3);
        Assert.Equal(RejectReason.NotEnoughItems, f.Submit(intent).Reason);
    }

    [Fact]
    public void Craft_MissingIngredientsWithoutChests_StaysFreeAndDoesNotAllocate()
    {
        var f = ForCrafting();
        f.Discover(Player1, Stone);
        var intent = Craft("t:hand");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(RejectReason.NotEnoughItems, f.Submit(intent).Reason);
        }

        var allocated = long.MaxValue;
        var refused = 0;
        for (var round = 0; round < 60 && allocated != 0; round++)
        {
            refused = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                refused += f.Submit(intent).Reason == RejectReason.NotEnoughItems ? 1 : 0;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(1000, refused);
        Assert.Equal(0, allocated);
    }
}
