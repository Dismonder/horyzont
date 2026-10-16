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

/// <summary>What <see cref="CraftingWorldScript.Build"/> made: the two players, the construct, the two chests and the containers of the furnace.</summary>
internal sealed record CraftingWorldBuilt(PlayerId Alice, PlayerId Bob, EntityId Construct, ContainerId Chest, ContainerId Chest2, ContainerId Input, ContainerId Output)
{
    public static CellPos Chest2Cell => new(3, 1, 0);

    public static CellPos FurnaceCell => FunctionalBlocks.FreeCell;

    public static CellPos ChestCell => FunctionalBlocks.ChestCell;

    public static CellPos BedCell => FunctionalBlocks.BedCell;

    public static BedRef Bed(EntityId construct) => new(construct, BedCell, FunctionalBlocks.BedType);
}

/// <summary>Builds the world of the K6-32 persistence tests through server operations, so the journal and the checkpoints see every step.</summary>
internal static class CraftingWorldScript
{
    public static ItemTypeId Stone => WorldFactory.Item(SurvivalContentFactory.StoneKey);

    public static ItemTypeId Brick => WorldFactory.Item(SurvivalContentFactory.BrickKey);

    public static ItemTypeId Gem => WorldFactory.Item(SurvivalContentFactory.GemKey);

    public static ItemTypeId Ore => WorldFactory.Item(SurvivalContentFactory.OreKey);

    public static ItemTypeId Peat => WorldFactory.Item(SurvivalContentFactory.PeatKey);

    public static ItemTypeId Ingot => WorldFactory.Item(SurvivalContentFactory.IngotKey);

    /// <summary>A fresh world with the block types of <see cref="CraftingBlocks"/> and the twelve item types of the crafting content.</summary>
    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], CraftingBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.CraftingItemKeys);

    /// <summary>
    /// Two players with their containers; a construct with a stone floor, a chest with ore and one stone, a second chest with one stone, a bed of Alice with her respawn point and a furnace
    /// smelting (four ore and two peat in the input, started now); Alice discovered the stone and the ore (she also has the bed), Bob the gem only
    /// (a record without a respawn point), and the world has the progress key <c>test:boss_2</c>.
    /// </summary>
    public static CraftingWorldBuilt Build(WorldHarness h)
    {
        var alice = h.Join(1);
        var bob = h.Join(2);
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(alice, 1));
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(bob, 2));
        var construct = h.State.EntityIds.PeekNext();
        h.Sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, new CellPos(0, 0, 0),
            BlockState.Create(FunctionalBlocks.StoneType, 0)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, CraftingWorldBuilt.ChestCell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, CraftingWorldBuilt.BedCell, BlockState.Create(FunctionalBlocks.BedType, 0)));

        var chest = h.State.ContainerIds.PeekNext();
        h.Sim.ApplyServerOperation(new ContainerCreateOp(
            chest, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, CraftingWorldBuilt.ChestCell), alice, ContainerAccess.Shared, 8));
        h.Sim.ApplyServerOperation(new BlockEntitySetOp(construct, CraftingWorldBuilt.ChestCell, FunctionalBlocks.ChestType, new ChestBlockState(chest)));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest, 0), Ore, 3, 500, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest, 1), Stone, 1, 500, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, CraftingWorldBuilt.Chest2Cell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        var chest2 = h.State.ContainerIds.PeekNext();
        h.Sim.ApplyServerOperation(new ContainerCreateOp(
            chest2, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, CraftingWorldBuilt.Chest2Cell), bob, ContainerAccess.Shared, 8));
        h.Sim.ApplyServerOperation(new BlockEntitySetOp(construct, CraftingWorldBuilt.Chest2Cell, FunctionalBlocks.ChestType, new ChestBlockState(chest2)));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest2, 0), Stone, 1, 500, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new BlockEntitySetOp(construct, CraftingWorldBuilt.BedCell, FunctionalBlocks.BedType, new BedBlockState(alice)));
        h.Sim.ApplyServerOperation(new RespawnPointSetOp(alice, CraftingWorldBuilt.Bed(construct)));

        var input = h.State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        var host = ContainerHost.ConstructBlock(construct, CraftingWorldBuilt.FurnaceCell);
        h.Sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, host, alice, ContainerAccess.Shared, CraftingBlocks.InputSlots),
            new ContainerCreateOp(output, ContainerKind.StationOutput, host, alice, ContainerAccess.Shared, CraftingBlocks.OutputSlots),
            new BlockEntitySetOp(
                construct, CraftingWorldBuilt.FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", h.State.CurrentTick)),
            new BlockSetOp(construct, CraftingWorldBuilt.FurnaceCell, BlockState.Create(CraftingBlocks.FurnaceType, 0)),
        ]));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(input, 0), Ore, 4, 500, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(input, 1), Peat, 2, 500, ItemSource.Admin));

        h.Sim.ApplyServerOperation(new ItemDiscoverOp(alice, Stone));
        h.Sim.ApplyServerOperation(new ItemDiscoverOp(alice, Ore));
        h.Sim.ApplyServerOperation(new ItemDiscoverOp(bob, Gem));
        h.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:boss_2"));
        return new CraftingWorldBuilt(alice, bob, construct, chest, chest2, input, output);
    }
}
