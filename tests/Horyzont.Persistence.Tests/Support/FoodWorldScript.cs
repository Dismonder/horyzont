using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>What <see cref="FoodWorldScript.Build"/> made: the two players and the construct with the bed of the first one.</summary>
internal sealed record FoodWorldBuilt(PlayerId Alice, PlayerId Bob, EntityId Construct);

/// <summary>Builds the world of the K6-33 persistence tests through server operations, so the journal and the checkpoints see every step.</summary>
internal static class FoodWorldScript
{
    /// <summary>The vitals of Alice (online at the checkpoint): hurt, tired, rested for ten more minutes.</summary>
    public static readonly PlayerVitals AliceVitals = new(80f, 55.5f, new Tick(36000), false);

    /// <summary>The vitals of Bob (offline at the checkpoint): exhausted.</summary>
    public static readonly PlayerVitals BobVitals = new(30f, 0f, new Tick(12000), true);

    public static ItemTypeId Berries => WorldFactory.Item(SurvivalContentFactory.BerriesKey);

    public static ItemTypeId Jam => WorldFactory.Item(SurvivalContentFactory.JamKey);

    public static ItemTypeId Broth => WorldFactory.Item(SurvivalContentFactory.BrothKey);

    public static ItemTypeId Stone => WorldFactory.Item(SurvivalContentFactory.StoneKey);

    public static ItemTypeId Gem => WorldFactory.Item(SurvivalContentFactory.GemKey);

    /// <summary>A fresh world with the block types of <see cref="FoodBlocks"/> and the eight item types of the food content.</summary>
    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], FoodBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.FoodItemKeys);

    /// <summary>The compound of eating one item from a bag slot of Alice (the destroy, then the food slots).</summary>
    public static CompoundOp Eat(PlayerId player, int bagSlot, ItemTypeId type, FoodSlots after) =>
        new([new ItemDestroyOp(new SlotRef(WorldFactory.BagOf((int)player.Value), (ushort)bagSlot), type, 1, ItemSink.Eat), new FoodSetOp(player, after)]);

    /// <summary>
    /// Alice has a bed with her respawn point, two discoveries, berries eaten at tick 100 and jam at tick 200 (food slots 0 and 1) and the vitals
    /// <see cref="AliceVitals"/>; Bob has only the berries eaten at tick 50 (a record without a bed and without discoveries), the vitals
    /// <see cref="BobVitals"/> and has left the game.
    /// </summary>
    public static FoodWorldBuilt Build(WorldHarness h)
    {
        var alice = h.Join(1);
        var bob = h.Join(2);
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(alice, 1));
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(bob, 2));
        var construct = h.State.EntityIds.PeekNext();
        h.Sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, new CellPos(0, 0, 0),
            BlockState.Create(FunctionalBlocks.StoneType, 0)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, FunctionalBlocks.BedCell, BlockState.Create(FunctionalBlocks.BedType, 0)));
        h.Sim.ApplyServerOperation(new BlockEntitySetOp(construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType, new BedBlockState(alice)));
        h.Sim.ApplyServerOperation(new RespawnPointSetOp(alice, new BedRef(construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType)));
        h.Sim.ApplyServerOperation(new ItemDiscoverOp(alice, Stone));
        h.Sim.ApplyServerOperation(new ItemDiscoverOp(alice, Gem));

        var bag = WorldFactory.BagOf(1);
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(bag, 0), Berries, 3, 20, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(bag, 1), Jam, 2, 20, ItemSource.Admin));
        var first = new FoodSlots(new FoodSlot(Berries, new Tick(100)), default, default);
        var second = first.With(1, new FoodSlot(Jam, new Tick(200)));
        h.Sim.ApplyServerOperation(Eat(alice, 0, Berries, first));
        h.Sim.ApplyServerOperation(Eat(alice, 1, Jam, second));
        h.Sim.ApplyServerOperation(new FoodSetOp(bob, new FoodSlots(new FoodSlot(Berries, new Tick(50)), default, default)));

        Assert.True(h.Sim.UpdatePlayerVitals(alice, AliceVitals));
        Assert.True(h.Sim.UpdatePlayerVitals(bob, BobVitals));
        Assert.True(h.State.Players.TryGet(bob, out var bobState));
        h.Sim.ApplyServerOperation(new PlayerLeaveOp(bob, bobState.Frame, bobState.ParentSeq, bobState.Position, Quaternion.Normalize(bobState.BodyRotation), bobState.Pitch));
        return new FoodWorldBuilt(alice, bob, construct);
    }

    /// <summary>The tail of the fixture journal: Alice gets broth and eats it into the third slot (one compound of the destroy and the food slots).</summary>
    public static void BuildTail(WorldHarness h, FoodWorldBuilt built)
    {
        var bag = WorldFactory.BagOf(1);
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(bag, 2), Broth, 1, 20, ItemSource.Admin));
        Assert.True(h.State.Progress.TryGet(built.Alice, out var progress));
        var third = progress.Food.With(2, new FoodSlot(Broth, new Tick(300)));
        h.Sim.ApplyServerOperation(Eat(built.Alice, 2, Broth, third));
    }
}
