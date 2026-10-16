using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// A <see cref="FunctionalBlockFixture"/> over <see cref="WorldFactory.CreateWithHazards"/> (K6-34): the food world with the suit refills (types 9 and 10), the modules (11 and 12) and the
/// generator fuel (13) in the registry, and helpers to set the suit, to refill it, to place and feed an oxygen generator and to wear modules. The applier does not read content, so operations need none;
/// a generator is a block of any type (the stone type here), like a station in <see cref="Horyzont.Core.Tests.Crafting.CraftingFixture"/>.
/// </summary>
internal sealed class HazardWorld
{
    /// <summary>The block type that holds the generator state in these tests.</summary>
    public const ushort GeneratorBlockType = FunctionalBlocks.StoneType;

    /// <param name="players">Number of players.</param>
    /// <param name="survival">Whether the simulation has the survival content of <see cref="HazardTestContent"/> (intents are resolved).</param>
    public HazardWorld(int players = 1, bool survival = false)
    {
        var state = WorldFactory.CreateWithHazards(players);
        Blocks = survival
            ? new FunctionalBlockFixture(state, HazardTestContent.Create(), FoodBlocks.ContentFor(state), null)
            : new FunctionalBlockFixture(state);
    }

    public static PlayerId Player1 => FunctionalBlockFixture.Player1;

    public static PlayerId Player2 => FunctionalBlockFixture.Player2;

    public static ItemTypeId Stone => ItemFixture.Stone;

    public static ItemTypeId HeatCell => WorldFactory.Item(SurvivalContentFactory.HeatCellKey);

    public static ItemTypeId Canister => WorldFactory.Item(SurvivalContentFactory.OxygenCanisterKey);

    public static ItemTypeId Tank => WorldFactory.Item(SurvivalContentFactory.TankModuleKey);

    public static ItemTypeId Thermal => WorldFactory.Item(SurvivalContentFactory.ThermalModuleKey);

    public static ItemTypeId Fuel => WorldFactory.Item(SurvivalContentFactory.FuelPackKey);

    /// <summary>The cell of the first generator.</summary>
    public static CellPos GeneratorCell => FunctionalBlocks.FreeCell;

    /// <summary>The cell of the second generator.</summary>
    public static CellPos GeneratorCell2 => FunctionalBlocks.FreeCell2;

    public FunctionalBlockFixture Blocks { get; }

    public WorldState State => Blocks.State;

    public ServerSimulation Simulation => Blocks.Sim.Sim;

    public Tick Now => State.CurrentTick;

    public AppliedOperation Apply(Operation op) => Blocks.Apply(op);

    /// <summary>The operation is refused: it throws and changes nothing (hash, last operation, journal).</summary>
    public void AssertFails(Operation op) => Blocks.AssertFails(op);

    public static SuitRefillOp Refill(PlayerId player, SuitResource resource, ushort amount, ushort capacity) => new(player, resource, amount, capacity);

    public static ContainerId BagOf(PlayerId player) => WorldFactory.BagOf((int)player.Value);

    public static ContainerId EquipmentOf(PlayerId player) => WorldFactory.EquipmentOf((int)player.Value);

    public static SlotRef BagSlot(PlayerId player, int slot) => new(BagOf(player), (ushort)slot);

    /// <summary>Creates items in a bag slot of a player (server operation).</summary>
    public AppliedOperation Give(PlayerId player, ItemTypeId type, int count = 1, int bagSlot = 0) =>
        Apply(new ItemCreateOp(BagSlot(player, bagSlot), type, (ushort)count, 100, ItemSource.Admin));

    /// <summary>Puts a module straight into an equipment slot (server operation, no layout check: the applier does not know the layout).</summary>
    public AppliedOperation Equip(PlayerId player, int slot, ItemTypeId type) =>
        Apply(new ItemCreateOp(new SlotRef(EquipmentOf(player), (ushort)slot), type, 1, 1, ItemSource.Admin));

    /// <summary>The slots of the equipment container of a player.</summary>
    public ItemStack[] EquipmentOfPlayer(PlayerId player)
    {
        var container = Blocks.Container(EquipmentOf(player));
        var slots = new ItemStack[container.Size];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = container.GetSlot(i);
        }

        return slots;
    }

    /// <summary>Sets the vitals of an online player with the given suit (full health and stamina).</summary>
    public void SetSuit(PlayerId player, SuitResources? suit)
    {
        Assert.True(Simulation.UpdatePlayerVitals(player, new PlayerVitals(100f, 100f, default, false, suit)));
    }

    /// <summary>The vitals of a player; null without any.</summary>
    public PlayerVitals? VitalsOf(PlayerId player) => Blocks.Sim.PlayerOf(player).Vitals;

    /// <summary>The suit of a player; null without vitals or without a suit.</summary>
    public SuitResources? SuitOf(PlayerId player) => VitalsOf(player)?.Suit;

    /// <summary>The state of the generator in a cell; null for any other state or none.</summary>
    public OxygenGeneratorBlockState? GeneratorAt(CellPos cell) =>
        Blocks.HasState(cell, out var state) ? state as OxygenGeneratorBlockState : null;

    public static BlockEntitySetOp GeneratorState(CellPos cell, ContainerId input, Tick? started = null) =>
        new(FunctionalBlockFixture.ConstructId, cell, GeneratorBlockType, new OxygenGeneratorBlockState(input, started));

    public static ContainerCreateOp InputContainer(ContainerId id, CellPos cell, ushort size = 2, ContainerKind kind = ContainerKind.StationInput) =>
        new(id, kind, ContainerHost.ConstructBlock(FunctionalBlockFixture.ConstructId, cell), Player1, ContainerAccess.Shared, size);

    /// <summary>The compound that places a generator (D12): the input container, the state and the block last. The input id is the next free container id.</summary>
    public CompoundOp PlaceGenerator(CellPos cell, Tick? started = null, ushort inputSlots = 2)
    {
        var input = State.ContainerIds.PeekNext();
        return new CompoundOp(
        [
            InputContainer(input, cell, inputSlots),
            GeneratorState(cell, input, started),
            FunctionalBlockFixture.PlaceBlock(cell, GeneratorBlockType),
        ]);
    }

    /// <summary>The compound that removes a generator whose input is empty (D12).</summary>
    public static CompoundOp RemoveGenerator(CellPos cell, ContainerId input) => new(
    [
        new ContainerRemoveOp(input),
        FunctionalBlockFixture.ClearState(cell, GeneratorBlockType),
        new BlockClearOp(FunctionalBlockFixture.ConstructId, cell),
    ]);

    /// <summary>Places a generator by <see cref="PlaceGenerator"/> and returns its input container.</summary>
    public ContainerId SetUpGenerator(CellPos cell, Tick? started = null, ushort inputSlots = 2)
    {
        Apply(PlaceGenerator(cell, started, inputSlots));
        return GeneratorAt(cell)!.Input;
    }

    /// <summary>Puts fuel cells into a slot of the input of a generator (server operation).</summary>
    public AppliedOperation FeedGenerator(ContainerId input, int slot, int count) =>
        Apply(new ItemCreateOp(new SlotRef(input, (ushort)slot), Fuel, (ushort)count, 100, ItemSource.Admin));

    public void AdvanceTicks(int ticks) => Blocks.Sim.Ticks(ticks);
}
