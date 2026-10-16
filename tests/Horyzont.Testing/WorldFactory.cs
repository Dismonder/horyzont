using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Testing;

/// <summary>Worlds for tests, created only through the public <c>WorldState.CreateNew</c>.</summary>
public static class WorldFactory
{
    public const string StoneBlockKey = "core:stone_block";

    /// <summary>Material id 3 of <see cref="CreateWithPlayerContainers"/>; the test content gives it no yield.</summary>
    public const string BedrockMaterialKey = "bedrock";

    public const ushort BagSize = 40;
    public const ushort HotbarSize = 10;
    public const ushort EquipmentSize = 7;

    public static readonly WorldId DefaultWorldId = new(new Guid("5d1c3f5e-8b7a-4f43-9a51-0d6f7e2c1b10"));

    public static readonly PlanetId HomePlanet = new(1);

    public static PlanetParams HomePlanetParams => new(HomePlanet, new SystemId(1), 7, "core:test/1");

    /// <summary>One planet, block types core:stone_block and core:missing, materials stone and dirt.</summary>
    public static WorldState CreateDefault() => CreateWith(HomePlanetParams);

    /// <summary>Default registries with the given planets.</summary>
    public static WorldState CreateWith(params PlanetParams[] planets) =>
        WorldState.CreateNew(DefaultWorldId, 42, planets, [StoneBlockKey], ["stone", "dirt"]);

    /// <summary>Default registries with the four bodies of <see cref="StarSystemGenesis.CreateWorldBodies"/> (K5-21) and their star system.</summary>
    public static WorldState CreateWithStarSystem(ulong seed)
    {
        var created = StarSystemGenesis.CreateWorldBodies(seed, HomePlanetParams.GeneratorProfile);
        return WorldState.CreateNew(DefaultWorldId, seed, created.System, created.Bodies, [StoneBlockKey], ["stone", "dirt"]);
    }

    /// <summary>
    /// The default world plus material <see cref="BedrockMaterialKey"/> (id 3, no yield) with the item types of <see cref="SurvivalContentFactory"/> (ids 1..4) and <paramref name="players"/> joined players
    /// (player n has identity Dev n). Every player got, in one compound operation, a bag (<see cref="BagOf"/>, 40 slots), a hotbar
    /// (<see cref="HotbarOf"/>, 10 slots) and an equipment container (<see cref="EquipmentOf"/>, 7 slots); all are empty.
    /// </summary>
    public static WorldState CreateWithPlayerContainers(int players = 1) =>
        WithPlayers(
            WorldState.CreateNew(
                DefaultWorldId, 42, [HomePlanetParams], [StoneBlockKey], ["stone", "dirt", BedrockMaterialKey], SurvivalContentFactory.ItemKeys),
            players);

    /// <summary>Generator profile whose layout (V1) places props (K6-31); <see cref="HomePlanetParams"/> uses an unknown profile and has none.</summary>
    public const string PropProfile = "core:planet/1";

    /// <summary>
    /// A world for the props of K6-31: one planet with profile <see cref="PropProfile"/> (radius 4000 m, seed 7), the material keys of
    /// <see cref="SurvivalContentFactory.PropMaterialKeys"/>, the item keys of <see cref="SurvivalContentFactory.PropItemKeys"/> (ids 1..8) and
    /// <paramref name="players"/> joined players with empty containers, as in <see cref="CreateWithPlayerContainers"/>.
    /// </summary>
    public static WorldState CreateWithProps(int players = 1, string profile = PropProfile) => WithPlayers(
        WorldState.CreateNew(
            DefaultWorldId, 42, [HomePlanetParams with { GeneratorProfile = profile }], [StoneBlockKey], SurvivalContentFactory.PropMaterialKeys,
            SurvivalContentFactory.PropItemKeys),
        players);

    /// <summary>
    /// A world like <see cref="CreateWithProps"/> for any content: the item keys of <paramref name="content"/> in file order (ids from 1) and
    /// the given material keys (ids from 1).
    /// </summary>
    public static WorldState CreateForContent(SurvivalContent content, IReadOnlyList<string> materialKeys, int players = 1) => WithPlayers(
        WorldState.CreateNew(
            DefaultWorldId, 42, [HomePlanetParams with { GeneratorProfile = PropProfile }], [StoneBlockKey], materialKeys,
            content.Items.Items.Select(i => i.Key)),
        players);

    /// <summary>Item type id of a key of <see cref="SurvivalContentFactory.PropItemKeys"/> in a world of <see cref="CreateWithProps"/>.</summary>
    public static ItemTypeId PropItem(string key)
    {
        var index = SurvivalContentFactory.PropItemKeys.ToList().IndexOf(key);
        return index >= 0 ? new ItemTypeId((ushort)(index + 1)) : throw new ArgumentException($"Unknown test item '{key}'.", nameof(key));
    }

    /// <summary>
    /// <see cref="CreateWithPlayerContainers"/> with the block types of <see cref="FunctionalBlocks.BlockKeys"/> (ids 1..5) and, when
    /// <paramref name="withConstruct"/>, the construct <see cref="FunctionalBlocks.Construct"/>: a stone floor and plain blocks (no state, no container)
    /// of a chest, a bed, a lamp and a two-block door at the cells of <see cref="FunctionalBlocks"/>, placed by server operations.
    /// </summary>
    public static WorldState CreateWithFunctionalBlocks(int players = 1, bool withConstruct = true) =>
        CreateBlockWorld(players, withConstruct, FunctionalBlocks.BlockKeys, SurvivalContentFactory.ItemKeys, [HomePlanetParams]);

    /// <summary>
    /// <see cref="CreateWithFunctionalBlocks"/> with the item types of <see cref="SurvivalContentFactory.CraftingItemKeys"/> (ids 1..12) and the block types of
    /// <see cref="CraftingBlocks.BlockKeys"/> (ids 1..13); the construct is the same as there. With <paramref name="secondPlanet"/> the world has
    /// <see cref="SecondPlanet"/> too (K6-32).
    /// </summary>
    /// <param name="players">Number of players.</param>
    /// <param name="secondPlanet">Whether the world has a second planet.</param>
    /// <returns>The world.</returns>
    public static WorldState CreateWithCrafting(int players = 1, bool secondPlanet = false) =>
        CreateBlockWorld(
            players, true, CraftingBlocks.BlockKeys, SurvivalContentFactory.CraftingItemKeys,
            secondPlanet ? [HomePlanetParams, SecondPlanetParams] : [HomePlanetParams]);

    /// <summary>Id of the second planet of <see cref="CreateWithCrafting"/> with <c>secondPlanet</c>.</summary>
    public static readonly PlanetId SecondPlanet = new(2);

    /// <summary>The second planet of <see cref="CreateWithCrafting"/> with <c>secondPlanet</c>.</summary>
    public static PlanetParams SecondPlanetParams => new(SecondPlanet, new SystemId(1), 9, "core:test/2");

    private static WorldState CreateBlockWorld(
        int players, bool withConstruct, IReadOnlyList<string> blockKeys, IReadOnlyList<string> itemKeys, PlanetParams[] planets)
    {
        var state = WithPlayers(
            WorldState.CreateNew(DefaultWorldId, 42, planets, blockKeys, ["stone", "dirt", BedrockMaterialKey], itemKeys),
            players);
        if (!withConstruct)
        {
            return state;
        }

        var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        sim.ApplyServerOperation(new ConstructCreateOp(
            FunctionalBlocks.Construct, FrameRef.Of(HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity,
            FunctionalBlocks.FloorCells[0], BlockState.Create(FunctionalBlocks.StoneType, 0)));
        foreach (var cell in FunctionalBlocks.FloorCells.Skip(1))
        {
            sim.ApplyServerOperation(new BlockSetOp(FunctionalBlocks.Construct, cell, BlockState.Create(FunctionalBlocks.StoneType, 0)));
        }

        (CellPos Cell, ushort Type)[] blocks =
        [
            (FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType),
            (FunctionalBlocks.BedCell, FunctionalBlocks.BedType),
            (FunctionalBlocks.LampCell, FunctionalBlocks.LampType),
            (FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType),
            (FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType),
        ];
        foreach (var (cell, type) in blocks)
        {
            sim.ApplyServerOperation(new BlockSetOp(FunctionalBlocks.Construct, cell, BlockState.Create(type, 0)));
        }

        return state;
    }

    /// <summary>
    /// <see cref="CreateWithFunctionalBlocks"/> with the item types of <see cref="SurvivalContentFactory.FoodItemKeys"/> (K6-33): the four test types of
    /// the other worlds and four food types (ids 5..8), the construct with a bed, the block types of <see cref="FoodBlocks"/> (a campfire as id 6) and the bag, hotbar and equipment of every player.
    /// With <paramref name="secondPlanet"/> the world has <see cref="SecondPlanet"/> too.
    /// </summary>
    public static WorldState CreateWithFood(int players = 1, bool secondPlanet = false) =>
        CreateBlockWorld(
            players, true, FoodBlocks.BlockKeys, SurvivalContentFactory.FoodItemKeys,
            secondPlanet ? [HomePlanetParams, SecondPlanetParams] : [HomePlanetParams]);

    private static WorldState WithPlayers(WorldState state, int players)
    {
        var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        for (var n = 1; n <= players; n++)
        {
            var join = sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, (ulong)n), $"player{n}");
            sim.ApplyServerOperation(join);
            sim.ApplyServerOperation(PlayerContainers(join.Player, n));
        }

        return state;
    }

    /// <summary>Item type id of a key of <see cref="SurvivalContentFactory.CraftingItemKeys"/> (its first four are <see cref="SurvivalContentFactory.ItemKeys"/>) in a world of <see cref="CreateWithPlayerContainers"/> or <see cref="CreateWithCrafting"/>, or of a food key of <see cref="SurvivalContentFactory.FoodItemKeys"/> (ids 5..8) in a world of <see cref="CreateWithFood"/>.</summary>
    public static ItemTypeId Item(string key)
    {
        for (var i = 0; i < SurvivalContentFactory.CraftingItemKeys.Count; i++)
        {
            if (SurvivalContentFactory.CraftingItemKeys[i] == key)
            {
                return new ItemTypeId((ushort)(i + 1));
            }
        }

        for (var i = 0; i < SurvivalContentFactory.FoodItemKeys.Count; i++)
        {
            if (SurvivalContentFactory.FoodItemKeys[i] == key)
            {
                return new ItemTypeId((ushort)(i + 1));
            }
        }

        throw new ArgumentException($"Unknown test item '{key}'.", nameof(key));
    }

    public static ContainerId BagOf(int player) => new((ulong)((3 * player) - 2));

    public static ContainerId HotbarOf(int player) => new((ulong)((3 * player) - 1));

    public static ContainerId EquipmentOf(int player) => new((ulong)(3 * player));

    /// <summary>The compound operation that creates the three containers of player <paramref name="n"/> with the ids above.</summary>
    public static CompoundOp PlayerContainers(PlayerId player, int n) => new(
    [
        new ContainerCreateOp(BagOf(n), ContainerKind.PlayerBag, ContainerHost.Player(player), player, ContainerAccess.Private, BagSize),
        new ContainerCreateOp(HotbarOf(n), ContainerKind.Hotbar, ContainerHost.Player(player), player, ContainerAccess.Private, HotbarSize),
        new ContainerCreateOp(EquipmentOf(n), ContainerKind.Equipment, ContainerHost.Player(player), player, ContainerAccess.Private, EquipmentSize),
    ]);
}
