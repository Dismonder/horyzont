using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Crafting;
using Horyzont.Server.Inventory;
using Horyzont.Server.Survival;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// A simulation with the hazard content (<see cref="WorldFactory.CreateWithHazards"/>, <see cref="SurvivalContentFactory.CreateTestWithHazards"/>, two players online at the pole of the planet)
/// with the vitals service, the inventory service and the station service wired to a list of the messages they send and to a fake source of the motion of the bodies; no network and no runtime.
/// A tick is <see cref="Tick"/>: the services first, then the simulation advances (the order of the server). The home planet has the profile given to the rig (the temperate one by default;
/// <c>core:barren_moon</c> makes it a moon with a vacuum all day and night).
/// </summary>
internal sealed class HazardServerRig
{
    /// <summary>The cell of the oxygen generator of the rig: on the top face of the last floor cell, 5.5 m along x from the pole.</summary>
    public static readonly CellPos GeneratorCell = FunctionalBlocks.FreeCell;

    public static readonly CellPos SeatCell = FunctionalBlocks.FreeCell2;

    /// <summary>Initializes a new instance of the <see cref="HazardServerRig"/> class.</summary>
    /// <param name="homeProfile">The profile of the home planet (body 0).</param>
    /// <param name="rules">The rules of the game, null for the defaults.</param>
    /// <param name="hazards">The hazard rules, null for the defaults.</param>
    /// <param name="logger">The logger of the vitals service, null for none.</param>
    public HazardServerRig(string homeProfile = "core:temperate", GameRules? rules = null, HazardRules? hazards = null, ILogger<VitalsService>? logger = null)
    {
        var world = WorldFactory.CreateWithHazards(players: 2);
        Sim = new ServerSimulation(
            world, new FakeTerrainService(), Journal, rules ?? new GameRules(), NullLogger<ServerSimulation>.Instance, HazardBlocks.ContentFor(world),
            SurvivalContentFactory.CreateTestWithHazards(homeProfile), null, hazards);
        void Send(PlayerId player, Message message) => Sent.Add((player, message));
        Vitals = new VitalsService(() => Sim, Motion, Send, () => false, logger);
        Inventory = new InventoryService(() => Sim, Send, () => false, NullLogger<InventoryService>.Instance);
        Stations = new StationService(() => Sim, Inventory.CollectObservedStations, () => false, NullLogger<StationService>.Instance);

        // The players spawn at the pole, inside the blocks of the construct (the rays of the shelter would hit them); the rig puts them on the open ground 100 m away.
        StandAt(Alice, OpenGround);
        StandAt(Bob, OpenGround);
    }

    /// <summary>A point on the surface of the planet 100 m from the pole, 10 cm above the ground, where no block of the construct is within 16 m.</summary>
    public static Vector3 OpenGround { get; } = new(100f, MathF.Sqrt((4000f * 4000f) - (100f * 100f)) + 0.1f, 0f);

    public PlayerId Alice { get; } = new(1);

    public PlayerId Bob { get; } = new(2);

    public InMemoryJournal Journal { get; } = new();

    public ServerSimulation Sim { get; }

    public FoodServerRig.FakeMotion Motion { get; } = new();

    public VitalsService Vitals { get; }

    public InventoryService Inventory { get; }

    public StationService Stations { get; }

    public List<(PlayerId Player, Message Message)> Sent { get; } = [];

    public static ItemTypeId HeatCell => WorldFactory.Item(SurvivalContentFactory.HeatCellKey);

    public static ItemTypeId Canister => WorldFactory.Item(SurvivalContentFactory.OxygenCanisterKey);

    public static ItemTypeId Tank => WorldFactory.Item(SurvivalContentFactory.TankModuleKey);

    public static ItemTypeId Thermal => WorldFactory.Item(SurvivalContentFactory.ThermalModuleKey);

    public static ItemTypeId Fuel => WorldFactory.Item(SurvivalContentFactory.FuelPackKey);

    /// <summary>Ticks one fuel cell burns in the content of the rig (10 seconds at 60 ticks per second).</summary>
    public ulong TicksPerCell => Sim.Survival!.Hazards!.Generator.TicksPerCell(Sim.Rules.TickRate);

    public PlayerVitals? VitalsOf(PlayerId player) => Sim.State.Players.TryGet(player, out var state) ? state.Vitals : null;

    public SuitResources SuitOf(PlayerId player) => VitalsOf(player)?.Suit ?? throw new InvalidOperationException("the player has no suit yet");

    public List<PlayerVitalsMessage> MessagesTo(PlayerId player) => [.. Sent.Where(s => s.Player == player).Select(s => s.Message).OfType<PlayerVitalsMessage>()];

    /// <summary>Runs <paramref name="ticks"/> ticks of the server: the vitals service and the station service, then the simulation advances.</summary>
    public void Tick(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Vitals.Step();
            Stations.Step();
            Sim.AdvanceTick();
        }
    }

    /// <summary>The player walks (has a body that stands); Bob too when <paramref name="both"/>.</summary>
    public void Walk(bool both = false)
    {
        Motion.Set(Alice, Horyzont.Core.Motion.MovementMode.Walking);
        if (both)
        {
            Motion.Set(Bob, Horyzont.Core.Motion.MovementMode.Walking);
        }
    }

    /// <summary>Both players become ready to receive messages 44.</summary>
    public void ReadyBoth()
    {
        Vitals.OnPlayerReady(Alice);
        Vitals.OnPlayerReady(Bob);
    }

    /// <summary>Sets the suit of a player outside the journal, the way the service does it.</summary>
    public void SetSuit(PlayerId player, SuitResources suit, float health = 100f)
    {
        var current = VitalsOf(player) ?? new PlayerVitals(health, 100f, default, false);
        Assert.True(Sim.UpdatePlayerVitals(player, current with { Health = health, Suit = suit }));
    }

    public void Give(PlayerId player, int bagSlot, ItemTypeId type, ushort count = 1) =>
        Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.BagOf((int)player.Value), (ushort)bagSlot), type, count, 500, ItemSource.Admin));

    /// <summary>Puts a module of the suit into a slot of the equipment of a player (4 to 6 are the modules).</summary>
    public void Equip(PlayerId player, int slot, ItemTypeId type) =>
        Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.EquipmentOf((int)player.Value), (ushort)slot), type, 1, 1, ItemSource.Admin));

    /// <summary>Takes a module out of the equipment (destroyed).</summary>
    public void Unequip(PlayerId player, int slot, ItemTypeId type) =>
        Sim.ApplyServerOperation(new ItemDestroyOp(new SlotRef(WorldFactory.EquipmentOf((int)player.Value), (ushort)slot), type, 1, ItemSink.Admin));

    /// <summary>The player uses the refill in a bag slot (the intent of the client).</summary>
    public IntentResult Refill(PlayerId player, int bagSlot) =>
        Sim.Submit(player, new SuitRefillIntent(WorldFactory.BagOf((int)player.Value), (ushort)bagSlot));

    /// <summary>An oxygen generator put up by one server compound, as the resolver does it (the input, the state, the block).</summary>
    /// <param name="started">When the burning started, null for an idle generator.</param>
    /// <param name="cell">The cell, <see cref="GeneratorCell"/> by default.</param>
    /// <returns>The input container.</returns>
    public ContainerId PlaceGenerator(Tick? started, CellPos? cell = null)
    {
        var at = cell ?? GeneratorCell;
        var input = Sim.State.ContainerIds.PeekNext();
        Sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, ContainerHost.ConstructBlock(FunctionalBlocks.Construct, at), Alice, ContainerAccess.Shared, 2),
            new BlockEntitySetOp(FunctionalBlocks.Construct, at, HazardBlocks.GeneratorType, new OxygenGeneratorBlockState(input, started)),
            new BlockSetOp(FunctionalBlocks.Construct, at, BlockState.Create(HazardBlocks.GeneratorType, 0)),
        ]));
        return input;
    }

    public void Stock(ContainerId container, int slot, ItemTypeId type, ushort count) =>
        Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(container, (ushort)slot), type, count, 500, ItemSource.Admin));

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

    public OxygenGeneratorBlockState Generator(CellPos? cell = null)
    {
        Assert.True(Sim.State.BlockEntities.TryGet(new BlockRef(FunctionalBlocks.Construct, cell ?? GeneratorCell), out var state));
        return Assert.IsType<OxygenGeneratorBlockState>(state);
    }

    /// <summary>Places the feet of a player in the frame of the planet.</summary>
    public void StandAt(PlayerId player, Vector3 feet)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Assert.True(Sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    /// <summary>Puts the eye of the player <paramref name="meters"/> above the centre of the generator block (to open its input).</summary>
    public void StandOver(PlayerId player, float meters)
    {
        var centre = new Vector3(GeneratorCell.X + 0.5f, 4000f + GeneratorCell.Y + 0.5f, GeneratorCell.Z + 0.5f);
        StandAt(player, centre + new Vector3(0f, meters - Sim.Rules.EyeHeight, 0f));
    }

    /// <summary>Seats a player in <see cref="SeatCell"/> of the construct (its frame, the seat set); the construct is a ship when <paramref name="ship"/>.</summary>
    public void Seat(PlayerId player, bool ship)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Sim.ApplyServerOperation(new BlockSetOp(FunctionalBlocks.Construct, SeatCell, BlockState.Create(FunctionalBlocks.StoneType, 0)));
        if (ship)
        {
            Assert.True(Sim.State.Constructs.TryGet(FunctionalBlocks.Construct, out var construct));
            Sim.ApplyServerOperation(TestShips.ModeOp(construct, ConstructMode.Ship));
        }

        var local = new Vector3(SeatCell.X + 0.5f, SeatCell.Y + 0.5f, SeatCell.Z + 0.5f);
        Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, FrameRef.Of(FunctionalBlocks.Construct), state.ParentSeq + 1, local, Quaternion.Identity, Vector3.Zero, SeatCell));
    }

    /// <summary>Changes the frame counter of a player in place (a teleport or a reparent) without moving it.</summary>
    public void Reparent(PlayerId player)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, state.Frame, state.ParentSeq + 1, state.Position, state.BodyRotation, Vector3.Zero, state.Seat));
    }

    /// <summary>Moves a player into the frame of the star system (out of every planet).</summary>
    public void LeavePlanet(PlayerId player)
    {
        Assert.True(Sim.State.Players.TryGet(player, out var state));
        Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, FrameRef.Of(WorldFactory.HomePlanetParams.System), state.ParentSeq + 1, new Vector3(0f, 0f, 9000f), Quaternion.Identity, Vector3.Zero, null));
    }
}
