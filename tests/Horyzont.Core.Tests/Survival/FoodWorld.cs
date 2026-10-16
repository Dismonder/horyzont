using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// A <see cref="FunctionalBlockFixture"/> over <see cref="WorldFactory.CreateWithFood"/> (K6-33): the world of the K6-30 and K6-32 tests with the four food
/// types 5..8 in the registry, and helpers to give, eat and advance. The applier does not read content, so these tests need none.
/// </summary>
internal sealed class FoodWorld
{
    public FoodWorld(int players = 1)
    {
        Blocks = new FunctionalBlockFixture(WorldFactory.CreateWithFood(players));
    }

    public static PlayerId Player1 => FunctionalBlockFixture.Player1;

    public static PlayerId Player2 => FunctionalBlockFixture.Player2;

    public static ItemTypeId Berries => WorldFactory.Item(SurvivalContentFactory.BerriesKey);

    public static ItemTypeId Jam => WorldFactory.Item(SurvivalContentFactory.JamKey);

    public static ItemTypeId Broth => WorldFactory.Item(SurvivalContentFactory.BrothKey);

    public static ItemTypeId Bread => WorldFactory.Item(SurvivalContentFactory.BreadKey);

    public static ItemTypeId Stone => ItemFixture.Stone;

    public static ItemTypeId Gem => ItemFixture.Gem;

    public FunctionalBlockFixture Blocks { get; }

    public WorldState State => Blocks.State;

    public ServerSimulation Simulation => Blocks.Sim.Sim;

    /// <summary>The current tick of the world.</summary>
    public Tick Now => State.CurrentTick;

    public AppliedOperation Apply(Operation op) => Blocks.Apply(op);

    /// <summary>The operation is refused: it throws and changes nothing (hash, last operation, journal).</summary>
    public void AssertFails(Operation op) => Blocks.AssertFails(op);

    public static FoodSlot Slot(ItemTypeId type, ulong eaten) => new(type, new Tick(eaten));

    /// <summary>Up to three slots; the missing ones are empty.</summary>
    public static FoodSlots Slots(params FoodSlot[] slots) => new(
        slots.Length > 0 ? slots[0] : default,
        slots.Length > 1 ? slots[1] : default,
        slots.Length > 2 ? slots[2] : default);

    public static FoodSetOp Set(PlayerId player, params FoodSlot[] slots) => new(player, Slots(slots));

    public static ContainerId BagOf(PlayerId player) => WorldFactory.BagOf((int)player.Value);

    public static SlotRef BagSlot(PlayerId player, int slot) => new(BagOf(player), (ushort)slot);

    /// <summary>Creates food in a bag slot of a player (server operation).</summary>
    public AppliedOperation GiveFood(PlayerId player, ItemTypeId type, int count = 1, int bagSlot = 0) =>
        Apply(new ItemCreateOp(BagSlot(player, bagSlot), type, (ushort)count, 20, ItemSource.Admin));

    /// <summary>The compound of eating (D10): destroy one item with the sink <see cref="ItemSink.Eat"/>, then set the food slots.</summary>
    public static CompoundOp EatOp(PlayerId player, int bagSlot, ItemTypeId type, FoodSlots after) =>
        new([new ItemDestroyOp(BagSlot(player, bagSlot), type, 1, ItemSink.Eat), new FoodSetOp(player, after)]);

    public AppliedOperation Eat(PlayerId player, int bagSlot, ItemTypeId type, FoodSlots after) => Apply(EatOp(player, bagSlot, type, after));

    /// <summary>The food slots of a player; three empty slots without a record.</summary>
    public FoodSlots FoodOf(PlayerId player) => State.Progress.TryGet(player, out var progress) ? progress.Food : default;

    /// <summary>The stack in a bag slot.</summary>
    public ItemStack BagStack(PlayerId player, int bagSlot) => Blocks.Container(BagOf(player)).GetSlot(bagSlot);

    public void AdvanceTicks(int ticks) => Blocks.Sim.Ticks(ticks);
}
