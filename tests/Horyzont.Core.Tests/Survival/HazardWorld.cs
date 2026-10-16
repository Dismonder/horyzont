using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
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
    /// <param name="survival">Whether the simulation has the survival content of <see cref="HazardTestContent"/> (intents are resolved); then player n stands in front of the blocks like in <see cref="FunctionalBlockFixture.ForIntents"/>.</param>
    /// <param name="hazards">The hazard rules of the simulation, null for the defaults.</param>
    /// <param name="map">Replaces an item definition of the content (for tests with their own stack limits); null keeps them all.</param>
    /// <param name="secondPlanet">Whether the world has a second planet.</param>
    /// <param name="hazardContent">Whether the survival content has the hazards and the generator (false is the content of K6-33).</param>
    public HazardWorld(
        int players = 1, bool survival = false, HazardRules? hazards = null, Func<ItemDef, ItemDef>? map = null, bool secondPlanet = false, bool hazardContent = true)
    {
        var state = WorldFactory.CreateWithHazards(players, secondPlanet);
        Blocks = survival
            ? new FunctionalBlockFixture(state, HazardTestContent.Create(map, hazardContent), HazardBlocks.ContentFor(state), null, hazards)
            : new FunctionalBlockFixture(state);
        if (survival)
        {
            for (var n = 1; n <= players; n++)
            {
                Blocks.Move(new PlayerId((ulong)n), FunctionalBlockFixture.StandFor(n));
            }
        }
    }

    /// <summary>The cell the first generator placed by an intent gets: on the top face of the last floor cell.</summary>
    public static CellPos PlacedCell => new(4, 1, 0);

    /// <summary>The cell the second generator placed by an intent gets: on the +X face of <see cref="PlacedCell"/>.</summary>
    public static CellPos PlacedCell2 => new(5, 1, 0);

    /// <summary>The intent that puts a generator on the top face of the last floor cell (<see cref="PlacedCell"/>).</summary>
    public static PlaceBlockIntent PlaceGeneratorIntent() =>
        new(new ConstructFaceTarget(FunctionalBlockFixture.ConstructId, FunctionalBlocks.FloorCells[4], Face.PosY), HazardBlocks.GeneratorType, 0);

    /// <summary>The intent that puts a generator on the +X face of <see cref="PlacedCell"/> (<see cref="PlacedCell2"/>).</summary>
    public static PlaceBlockIntent PlaceSecondGeneratorIntent() =>
        new(new ConstructFaceTarget(FunctionalBlockFixture.ConstructId, PlacedCell, Face.PosX), HazardBlocks.GeneratorType, 0);

    /// <summary>The intent that takes down the generator in a cell.</summary>
    public static RemoveBlockIntent RemoveGeneratorIntent(CellPos cell) => new(FunctionalBlockFixture.ConstructId, cell);

    /// <summary>Places a generator by intent as player 1 and returns its input container.</summary>
    public ContainerId PlaceByIntent(CellPos cell)
    {
        Blocks.Accept(Player1, cell == PlacedCell ? PlaceGeneratorIntent() : PlaceSecondGeneratorIntent());
        var state = GeneratorAt(cell);
        Assert.True(state is not null, "Placing the generator by an intent left no generator state in the cell (K6-34 B4: GeneratorRules).");
        return state.Input;
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
