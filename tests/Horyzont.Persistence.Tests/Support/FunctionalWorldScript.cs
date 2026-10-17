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

/// <summary>What <see cref="FunctionalWorldScript.Build"/> made: the player, the construct, the chest container and the cells of the blocks.</summary>
internal sealed record FunctionalWorld(PlayerId Player, EntityId Construct, ContainerId Chest)
{
    public static CellPos Floor => new(0, 0, 0);

    public static CellPos ChestCell => FunctionalBlocks.ChestCell;

    public static CellPos BedCell => FunctionalBlocks.BedCell;

    public static CellPos LampCell => FunctionalBlocks.LampCell;

    public static CellPos DoorCell => FunctionalBlocks.DoorLowCell;

    public static BedRef Bed(EntityId construct) => new(construct, BedCell, FunctionalBlocks.BedType);
}

/// <summary>Builds the world of the K6-30 persistence tests through server operations, so the journal and the checkpoints see every step.</summary>
internal static class FunctionalWorldScript
{
    /// <summary>A fresh world with the block types of <see cref="FunctionalBlocks"/> and the item types of the survival content.</summary>
    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], FunctionalBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.ItemKeys);

    /// <summary>
    /// Player 1 with the three containers; a construct with a stone floor and a chest (a container hosted by the block, its state, one stone
    /// stack), a bed owned by the player with its respawn point, a lit lamp and a two-block open door.
    /// </summary>
    public static FunctionalWorld Build(WorldHarness h)
    {
        var player = h.Join(1);
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(player, 1));
        var construct = h.State.EntityIds.PeekNext();
        h.Sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, FunctionalWorld.Floor,
            BlockState.Create(FunctionalBlocks.StoneType, 0)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, FunctionalBlocks.ChestCell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, FunctionalBlocks.BedCell, BlockState.Create(FunctionalBlocks.BedType, 0)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, FunctionalBlocks.LampCell, BlockState.Create(FunctionalBlocks.LampType, 0, 0, 1)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, FunctionalBlocks.DoorLowCell, BlockState.Create(FunctionalBlocks.DoorType, 0, 0, 1)));
        h.Sim.ApplyServerOperation(new BlockSetOp(construct, FunctionalBlocks.DoorHighCell, BlockState.Create(FunctionalBlocks.DoorType, 0, 0, 1)));

        var chest = h.State.ContainerIds.PeekNext();
        h.Sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(chest, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, FunctionalBlocks.ChestCell), player, ContainerAccess.Shared, 20),
            new BlockEntitySetOp(construct, FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType, new ChestBlockState(chest)),
        ]));
        h.Sim.ApplyServerOperation(new ItemCreateOp(
            new SlotRef(chest, 3), WorldFactory.Item(SurvivalContentFactory.StoneKey), 40, 500, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new BlockEntitySetOp(construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType, new BedBlockState(player)));
        h.Sim.ApplyServerOperation(new RespawnPointSetOp(player, FunctionalWorld.Bed(construct)));
        return new FunctionalWorld(player, construct, chest);
    }
}
