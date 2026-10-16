using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Content;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>
/// A <see cref="SimFixture"/> over <see cref="WorldFactory.CreateWithFunctionalBlocks"/> with helpers for the operations of K6-30.
/// Setup helpers apply the operations through <see cref="ServerSimulation.ApplyServerOperation"/>, so they throw until the behaviour exists.
/// </summary>
internal sealed class FunctionalBlockFixture
{
    public FunctionalBlockFixture(int players = 1, bool withConstruct = true)
    {
        Sim = new SimFixture(state: WorldFactory.CreateWithFunctionalBlocks(players, withConstruct));
    }

    /// <summary>A fixture over a world made elsewhere, e.g. <see cref="WorldFactory.CreateWithFood"/> (K6-33).</summary>
    /// <param name="state">The world.</param>
    public FunctionalBlockFixture(Horyzont.Core.World.WorldState state)
    {
        Sim = new SimFixture(state: state);
    }

    /// <summary>A fixture whose simulation has the given survival content and block content (K6-33 intent tests).</summary>
    /// <param name="state">The world, e.g. of <see cref="WorldFactory.CreateWithFood"/>.</param>
    /// <param name="survival">Survival content, null for none.</param>
    /// <param name="content">Block content, null for none.</param>
    /// <param name="vitals">The vitals rules of the world, null for the defaults.</param>
    /// <param name="hazards">The hazard rules of the world, null for the defaults (K6-34).</param>
    public FunctionalBlockFixture(
        Horyzont.Core.World.WorldState state, SurvivalContent? survival, Horyzont.Core.World.Blocks.BlockContentTable? content,
        Horyzont.Core.Survival.VitalsRules? vitals = null, Horyzont.Core.Survival.HazardRules? hazards = null)
    {
        Sim = new SimFixture(state: state, content: content, survival: survival, vitals: vitals, hazards: hazards);
    }

    private FunctionalBlockFixture(int players, bool withSurvival, ushort chestSlots, bool intents, GameRules? rules)
    {
        Assert.True(intents);
        var state = WorldFactory.CreateWithFunctionalBlocks(players);
        Sim = new SimFixture(
            rules, state: state, content: FunctionalBlocks.ContentFor(state, chestSlots),
            survival: withSurvival ? SurvivalContentFactory.CreateTest() : null);
        for (var n = 1; n <= players; n++)
        {
            Move(new PlayerId((ulong)n), StandFor(n));
        }
    }

    private FunctionalBlockFixture(int players, CraftingOptions options)
    {
        var state = WorldFactory.CreateWithCrafting(players, options.SecondPlanet);
        Sim = new SimFixture(
            rules: options.Rules, state: state, content: CraftingBlocks.ContentFor(state),
            survival: options.WithRecipes ? SurvivalContentFactory.CreateTestWithRecipes() : SurvivalContentFactory.CreateCraftingWithoutRecipes());
        for (var n = 1; n <= players; n++)
        {
            Move(new PlayerId((ulong)n), StandFor(n));
        }
    }

    private readonly record struct CraftingOptions(bool SecondPlanet, bool WithRecipes, GameRules? Rules);

    /// <summary>
    /// A fixture for crafting intents (K6-32 stage B): the world of <see cref="WorldFactory.CreateWithCrafting"/> with the block content of
    /// <see cref="CraftingBlocks.ContentFor"/> and the survival content of <see cref="SurvivalContentFactory.CreateTestWithRecipes"/> (or, without
    /// <paramref name="withRecipes"/>, <see cref="SurvivalContentFactory.CreateCraftingWithoutRecipes"/>). Player n stands at <see cref="StandFor"/>.
    /// </summary>
    /// <param name="players">Number of players.</param>
    /// <param name="withRecipes">Whether the survival content has the test recipes.</param>
    /// <param name="secondPlanet">Whether the world has a second planet.</param>
    /// <param name="rules">Game rules, null for the defaults.</param>
    public static FunctionalBlockFixture ForCrafting(int players = 1, bool withRecipes = true, bool secondPlanet = false, GameRules? rules = null) =>
        new(players, new CraftingOptions(secondPlanet, withRecipes, rules));

    /// <summary>
    /// A fixture for intents (K6-30 stage B): the simulation has the block content of <see cref="FunctionalBlocks.ContentFor"/> and, unless
    /// <paramref name="withSurvival"/> is false, the survival content of <see cref="SurvivalContentFactory.CreateTest"/>. Player n stands at
    /// <see cref="StandFor"/>, in front of the blocks and within reach of all of them.
    /// </summary>
    /// <param name="players">Number of players.</param>
    /// <param name="withSurvival">Whether the simulation has survival content.</param>
    /// <param name="chestSlots">Slots of the container of a chest block.</param>
    /// <param name="rules">Rules of the simulation (default rules when null).</param>
    public static FunctionalBlockFixture ForIntents(int players = 1, bool withSurvival = true, ushort chestSlots = 4, GameRules? rules = null) =>
        new(players, withSurvival, chestSlots, intents: true, rules);

    /// <summary>Feet of player <paramref name="n"/> (1-based): 3 m in front of the row of blocks, 1 m above the floor; 0.5 m apart.</summary>
    public static Vector3 StandFor(int n) => new(2f + (0.5f * (n - 1)), 4001f, 3f);

    /// <summary>Moves a player to <paramref name="feet"/> (planet frame).</summary>
    public void Move(PlayerId player, Vector3 feet) => Sim.MovePlayer(player, feet);

    /// <summary>Submits an intent as player 1.</summary>
    public IntentResult Submit(Intent intent) => Sim.Sim.Submit(Player1, intent);

    /// <summary>Submits an intent as <paramref name="player"/>.</summary>
    public IntentResult SubmitAs(PlayerId player, Intent intent) => Sim.Sim.Submit(player, intent);

    /// <summary>The intent is accepted; returns the applied operation.</summary>
    public AppliedOperation Accept(PlayerId player, Intent intent)
    {
        var result = SubmitAs(player, intent);
        Assert.True(result.IsAccepted, $"{intent.GetType().Name} was rejected: {result.Reason}");
        return result.Applied!;
    }

    /// <summary>The intent is rejected with <paramref name="reason"/> and changes nothing (hash, journal, simulation state).</summary>
    public void AssertRejected(PlayerId player, Intent intent, RejectReason reason)
    {
        var hash = StateHasher.Hash(State);
        var last = State.LastOpId;
        var journaled = Sim.Journal.Entries.Count;

        Assert.Equal(reason, SubmitAs(player, intent).Reason);

        Assert.Equal(hash, StateHasher.Hash(State));
        Assert.Equal(last, State.LastOpId);
        Assert.Equal(journaled, Sim.Journal.Entries.Count);
        Assert.False(Sim.Sim.IsFaulted);
    }

    /// <summary>The operation as a list: the components of a compound, else the operation itself.</summary>
    public static IReadOnlyList<Operation> Flatten(Operation op) => op is CompoundOp compound ? compound.Components : [op];

    /// <summary>Puts a plain block of <paramref name="type"/> in <paramref name="cell"/> when the cell is empty (server operation).</summary>
    public void EnsureBlock(CellPos cell, ushort type)
    {
        if (BlockAt(cell).IsEmpty)
        {
            Apply(PlaceBlock(cell, type));
        }
    }

    /// <summary>A chest block with a container (hosted by the block) and its state in <paramref name="cell"/>, set up by server operations.</summary>
    public ContainerId SetUpChestAt(CellPos cell, PlayerId owner, ContainerAccess access = ContainerAccess.Shared, ushort size = 4)
    {
        EnsureBlock(cell, FunctionalBlocks.ChestType);
        var id = State.ContainerIds.PeekNext();
        Apply(new CompoundOp([ChestContainer(id, ConstructId, cell, size, access, owner), SetChestState(cell, id)]));
        return id;
    }

    /// <summary>A bed state without an owner (or with <paramref name="owner"/>, whose respawn point is then set too) on the bed block of <paramref name="cell"/>.</summary>
    public void SetUpBed(CellPos cell, PlayerId owner = default)
    {
        EnsureBlock(cell, FunctionalBlocks.BedType);
        Apply(SetBedState(cell, owner));
        if (!owner.IsNone)
        {
            Apply(new RespawnPointSetOp(owner, BedAt(cell)));
        }
    }

    /// <summary>The stone item in the container, slot 0 by default.</summary>
    public void Give(ContainerId container, int slot, int count) => Apply(Stone(container, slot, count));

    public static PlayerId Player1 => new(1);

    public static PlayerId Player2 => new(2);

    public static EntityId ConstructId => FunctionalBlocks.Construct;

    public static ContainerId Bag => WorldFactory.BagOf(1);

    public static ContainerId Hotbar => WorldFactory.HotbarOf(1);

    public SimFixture Sim { get; }

    public Horyzont.Core.World.WorldState State => Sim.State;

    public uint ConstructVersion => State.Constructs.TryGet(ConstructId, out var construct) ? construct.Version : 0U;

    public BlockState BlockAt(CellPos cell) => State.Constructs.TryGet(ConstructId, out var construct) ? construct.GetBlock(cell) : BlockState.Empty;

    public AppliedOperation Apply(Operation op) => Sim.Sim.ApplyServerOperation(op);

    /// <summary>The operation is refused: it throws, the hash, LastOpId and journal do not change and the simulation is not faulted.</summary>
    public void AssertFails(Operation op)
    {
        var hash = StateHasher.Hash(State);
        var last = State.LastOpId;
        var journaled = Sim.Journal.Entries.Count;

        Assert.Throws<InvalidOperationException>(() => Sim.Sim.ApplyServerOperation(op));

        Assert.Equal(hash, StateHasher.Hash(State));
        Assert.Equal(last, State.LastOpId);
        Assert.Equal(journaled, Sim.Journal.Entries.Count);
        Assert.False(Sim.Sim.IsFaulted);
    }

    /// <summary>
    /// Runs the applier directly, without journaling; use it only for operations that are expected to fail (a success would leave
    /// <see cref="Horyzont.Core.World.WorldState.LastOpId"/> ahead of the journal).
    /// </summary>
    public bool TryApplyRaw(Operation op, out AppliedOperation? applied, out string failure, out int componentIndex) =>
        new OperationApplier(State, Sim.Terrain).TryApply(State.LastOpId.Next, State.CurrentTick, null, op, out applied, out failure, out componentIndex);

    public Container Container(ContainerId id) =>
        State.Containers.TryGet(id, out var container) ? container : throw new InvalidOperationException($"No container {id.Value}.");

    /// <summary>Containers hosted by the block in the cell of the fixture construct.</summary>
    public IEnumerable<Container> HostedBy(CellPos cell) =>
        State.Containers.All.Where(c => c.Host.Kind == ContainerHostKind.ConstructBlock && c.Host.Id == ConstructId.Value && c.Host.Cell == cell);

    public static ContainerCreateOp ChestContainer(
        ContainerId id, EntityId construct, CellPos cell, ushort size = 4, ContainerAccess access = ContainerAccess.Shared, PlayerId owner = default) =>
        new(id, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), owner.IsNone ? Player1 : owner, access, size);

    /// <summary>Creates a chest container and the state that refers to it for the chest block in <paramref name="cell"/>, in one compound (the applier keeps the two together).</summary>
    public ContainerId SetUpChest(CellPos cell, ushort size = 4, ContainerAccess access = ContainerAccess.Shared)
    {
        var id = State.ContainerIds.PeekNext();
        Apply(new CompoundOp([ChestContainer(id, ConstructId, cell, size, access), SetChestState(cell, id)]));
        return id;
    }

    /// <summary>Removes the empty chest container and the state that refers to it in one compound (the applier keeps the two together).</summary>
    public void RemoveChest(CellPos cell, ContainerId container) =>
        Apply(new CompoundOp([new ContainerRemoveOp(container), ClearState(cell, FunctionalBlocks.ChestType)]));

    public static BlockEntitySetOp SetChestState(CellPos cell, ContainerId container) =>
        new(ConstructId, cell, FunctionalBlocks.ChestType, new ChestBlockState(container));

    public static BlockEntitySetOp SetBedState(CellPos cell, PlayerId owner) =>
        new(ConstructId, cell, FunctionalBlocks.BedType, new BedBlockState(owner));

    public static BlockEntitySetOp ClearState(CellPos cell, ushort type) => new(ConstructId, cell, type, null);

    public static BedRef BedAt(CellPos cell) => new(ConstructId, cell, FunctionalBlocks.BedType);

    public static BlockFunctionStateSetOp SetFunctionState(CellPos cell, ushort type, byte state) => new(ConstructId, cell, type, state);

    public bool HasState(CellPos cell, out BlockEntityState? state) => State.BlockEntities.TryGet(new BlockRef(ConstructId, cell), out state);

    public static BlockSetOp PlaceBlock(CellPos cell, ushort type) => new(ConstructId, cell, BlockState.Create(type, 0));

    public static ItemCreateOp Stone(ContainerId container, int slot, int count) =>
        new(new SlotRef(container, (ushort)slot), WorldFactory.Item(SurvivalContentFactory.StoneKey), (ushort)count, 500, ItemSource.Admin);
}
