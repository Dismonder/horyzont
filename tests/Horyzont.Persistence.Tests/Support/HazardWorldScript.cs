using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>What <see cref="HazardWorldScript.Build"/> made: the two players, the construct, the burning generator and its input, and the idle generator and its input.</summary>
internal sealed record HazardWorldBuilt(PlayerId Alice, PlayerId Bob, EntityId Construct, ContainerId BurningInput, ContainerId IdleInput);

/// <summary>Builds the world of the K6-34 persistence tests through server operations, so the journal and the checkpoints see every step.</summary>
internal static class HazardWorldScript
{
    /// <summary>The cell of the generator that burns.</summary>
    public static readonly CellPos BurningCell = FunctionalBlocks.FreeCell;

    /// <summary>The cell of the idle generator.</summary>
    public static readonly CellPos IdleCell = FunctionalBlocks.FreeCell2;

    /// <summary>The suit of Alice at the checkpoint: heating 100, cooling 50, filter 0, shield 25 and 75.5 oxygen.</summary>
    public static readonly SuitResources AliceSuit = new(100f, 50f, 0f, 25f, 75.5f);

    /// <summary>The vitals of Alice (online at the checkpoint): hurt, tired, rested for ten more minutes, with the suit above.</summary>
    public static readonly PlayerVitals AliceVitals = new(80f, 55.5f, new Tick(36000), false, AliceSuit);

    /// <summary>The vitals of Bob (offline at the checkpoint): exhausted, without a suit (a record of blob 2 in version 2 would read the same).</summary>
    public static readonly PlayerVitals BobVitals = new(30f, 0f, new Tick(12000), true);

    /// <summary>The tick at which the burning generator started.</summary>
    public static readonly Tick BurningStart = new(100);

    /// <summary>The tick at which the tail moves the start of the burning generator after one cell burnt.</summary>
    public static readonly Tick SettledStart = new(700);

    public static ItemTypeId Canister => WorldFactory.Item(SurvivalContentFactory.OxygenCanisterKey);

    public static ItemTypeId Tank => WorldFactory.Item(SurvivalContentFactory.TankModuleKey);

    public static ItemTypeId Fuel => WorldFactory.Item(SurvivalContentFactory.FuelPackKey);

    /// <summary>A fresh world with the block types of <see cref="HazardBlocks"/> and the item types of the hazard content.</summary>
    public static WorldState NewWorld() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], HazardBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.HazardItemKeys);

    /// <summary>
    /// Alice and Bob with their containers; Alice has a tank in the module slot 4, two canisters in the bag and the vitals <see cref="AliceVitals"/>; Bob has left with <see cref="BobVitals"/>; a construct has two
    /// oxygen generators: one that burns since <see cref="BurningStart"/> with two fuel packs in the input, and an idle one with an empty input.
    /// </summary>
    public static HazardWorldBuilt Build(WorldHarness h)
    {
        var alice = h.Join(1);
        var bob = h.Join(2);
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(alice, 1));
        h.Sim.ApplyServerOperation(WorldFactory.PlayerContainers(bob, 2));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.EquipmentOf(1), 4), Tank, 1, 1, ItemSource.Admin));
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 0), Canister, 2, 100, ItemSource.Admin));

        var construct = h.State.EntityIds.PeekNext();
        h.Sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, FunctionalBlocks.FloorCells[0],
            BlockState.Create(FunctionalBlocks.StoneType, 0)));
        foreach (var cell in FunctionalBlocks.FloorCells.Skip(1))
        {
            h.Sim.ApplyServerOperation(new BlockSetOp(construct, cell, BlockState.Create(FunctionalBlocks.StoneType, 0)));
        }

        var burning = Generator(h, construct, BurningCell, alice, BurningStart);
        h.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(burning, 0), Fuel, 2, 100, ItemSource.Admin));
        var idle = Generator(h, construct, IdleCell, alice, null);

        Assert.True(h.Sim.UpdatePlayerVitals(alice, AliceVitals));
        Assert.True(h.Sim.UpdatePlayerVitals(bob, BobVitals));
        Assert.True(h.State.Players.TryGet(bob, out var bobState));
        h.Sim.ApplyServerOperation(new PlayerLeaveOp(bob, bobState.Frame, bobState.ParentSeq, bobState.Position, Quaternion.Normalize(bobState.BodyRotation), bobState.Pitch));
        return new HazardWorldBuilt(alice, bob, construct, burning, idle);
    }

    /// <summary>
    /// The tail of the fixture journal: Alice uses a canister (the compound of the destroy and the operation 42: +50 oxygen against the capacity 200 of the tank) and the burning generator is settled after one cell
    /// (the compound of the destroy of a fuel pack and the state with the new start).
    /// </summary>
    public static void BuildTail(WorldHarness h, HazardWorldBuilt built)
    {
        h.Sim.ApplyServerOperation(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(WorldFactory.BagOf(1), 0), Canister, 1, ItemSink.SuitRefill),
            new SuitRefillOp(built.Alice, SuitResource.Oxygen, 50, 200),
        ]));
        h.Sim.ApplyServerOperation(new CompoundOp(
        [
            new ItemDestroyOp(new SlotRef(built.BurningInput, 0), Fuel, 1, ItemSink.Fuel),
            new BlockEntitySetOp(built.Construct, BurningCell, HazardBlocks.GeneratorType, new OxygenGeneratorBlockState(built.BurningInput, SettledStart)),
        ]));
    }

    private static ContainerId Generator(WorldHarness h, EntityId construct, CellPos cell, PlayerId owner, Tick? started)
    {
        var input = h.State.ContainerIds.PeekNext();
        h.Sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, ContainerHost.ConstructBlock(construct, cell), owner, ContainerAccess.Shared, 2),
            new BlockEntitySetOp(construct, cell, HazardBlocks.GeneratorType, new OxygenGeneratorBlockState(input, started)),
            new BlockSetOp(construct, cell, BlockState.Create(HazardBlocks.GeneratorType, 0)),
        ]));
        return input;
    }
}
