using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Crafting;
using Horyzont.Server.Inventory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Crafting;

/// <summary>
/// A simulation with the crafting content (<see cref="WorldFactory.CreateWithCrafting"/>, two players) with the inventory service, the station
/// service and the progress replication of K6-32 wired to a list of the messages they send; no network and no runtime. A tick is
/// <see cref="Tick"/>: the station service first, then the simulation advances (the order of the server).
/// </summary>
internal sealed class CraftingServerRig
{
    public static readonly CellPos FurnaceCell = FunctionalBlocks.FreeCell;

    /// <summary>Initializes a new instance of the <see cref="CraftingServerRig"/> class.</summary>
    /// <param name="legacy">
    /// What happened to the world before the discoveries existed: a simulation without recipes in the content applies it first (nothing is discovered
    /// then), and the simulation of the rig takes the world over.
    /// </param>
    public CraftingServerRig(Action<ServerSimulation>? legacy = null)
    {
        var world = WorldFactory.CreateWithCrafting(players: 2);
        if (legacy is not null)
        {
            legacy(new ServerSimulation(
                world, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance, CraftingBlocks.ContentFor(world),
                SurvivalContentFactory.CreateCraftingWithoutRecipes()));
        }

        Sim = new ServerSimulation(
            world, new FakeTerrainService(), Journal, new GameRules(), NullLogger<ServerSimulation>.Instance, CraftingBlocks.ContentFor(world),
            SurvivalContentFactory.CreateTestWithRecipes());
        void Send(PlayerId player, Message message) => Sent.Add((player, message));
        Inventory = new InventoryService(() => Sim, Send, () => false, NullLogger<InventoryService>.Instance);
        Stations = new StationService(() => Sim, Inventory.CollectObservedStations, () => false, NullLogger<StationService>.Instance);
        Progress = new ProgressReplication(() => Sim, Send);
    }

    public PlayerId Alice { get; } = new(1);

    public PlayerId Bob { get; } = new(2);

    public InMemoryJournal Journal { get; } = new();

    public ServerSimulation Sim { get; }

    public List<(PlayerId Player, Message Message)> Sent { get; } = [];

    public InventoryService Inventory { get; }

    public StationService Stations { get; }

    public ProgressReplication Progress { get; }

    public static ItemTypeId Ore => WorldFactory.Item(SurvivalContentFactory.OreKey);

    public static ItemTypeId Peat => WorldFactory.Item(SurvivalContentFactory.PeatKey);

    public static ItemTypeId Ingot => WorldFactory.Item(SurvivalContentFactory.IngotKey);

    /// <summary>Both players become ready the way the server does it: the inventory first (containers, start kit), then the progress.</summary>
    public void ReadyBoth()
    {
        foreach (var player in new[] { Alice, Bob })
        {
            Inventory.OnPlayerReady(player);
            Progress.OnPlayerReady(player);
        }
    }

    /// <summary>A furnace (two containers, the state, the block) put up by one server compound, as the resolver does (D18).</summary>
    public (ContainerId Input, ContainerId Output) PlaceFurnace(string recipe = "", Tick? started = null)
    {
        var input = Sim.State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        var host = ContainerHost.ConstructBlock(FunctionalBlocks.Construct, FurnaceCell);
        Sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, host, Alice, ContainerAccess.Shared, CraftingBlocks.InputSlots),
            new ContainerCreateOp(output, ContainerKind.StationOutput, host, Alice, ContainerAccess.Shared, CraftingBlocks.OutputSlots),
            new BlockEntitySetOp(
                FunctionalBlocks.Construct, FurnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, recipe, started)),
            new BlockSetOp(FunctionalBlocks.Construct, FurnaceCell, BlockState.Create(CraftingBlocks.FurnaceType, 0)),
        ]));
        return (input, output);
    }

    public void Stock(ContainerId container, int slot, ItemTypeId type, ushort count) =>
        Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(container, (ushort)slot), type, count, 500, ItemSource.Admin));

    /// <summary>Puts the eye of the player <paramref name="meters"/> above the centre of the furnace block.</summary>
    public void Stand(PlayerId player, float meters)
    {
        var centre = new Vector3(FurnaceCell.X + 0.5f, 4000f + FurnaceCell.Y + 0.5f, FurnaceCell.Z + 0.5f);
        var feet = centre + new Vector3(0f, meters - Sim.Rules.EyeHeight, 0f);
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Assert.True(Sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    public StationBlockState Station()
    {
        Assert.True(Sim.State.BlockEntities.TryGet(new BlockRef(FunctionalBlocks.Construct, FurnaceCell), out var state));
        return Assert.IsType<StationBlockState>(state);
    }

    public int CountIn(ContainerId container, ItemTypeId type)
    {
        Assert.True(Sim.State.Containers.TryGet(container, out var c));
        var sum = 0;
        for (var i = 0; i < c.Size; i++)
        {
            sum += c.GetSlot(i).Type == type ? c.GetSlot(i).Count : 0;
        }

        return sum;
    }

    /// <summary>Runs <paramref name="ticks"/> ticks of the server: the station service, then the simulation advances.</summary>
    public void Tick(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Stations.Step();
            Sim.AdvanceTick();
        }
    }

    public List<Message> SentTo(PlayerId player) => [.. Sent.Where(s => s.Player == player).Select(s => s.Message)];
}
