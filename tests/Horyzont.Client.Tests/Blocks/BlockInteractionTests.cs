using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Blocks;
using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Tests.Inventory;
using Horyzont.Client.Tools;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Blocks;

public sealed class BlockInteractionTests
{
    private static readonly Vector3 _origin = new(0, 4000, 0);
    private static readonly PlayerId _me = new(1);
    private static readonly PlayerId _other = new(2);
    private static readonly ContainerId _chestId = new(7);
    private static readonly CellPos _stationCell = new(4, 1, 0);

    private sealed class FakeSender : IIntentSender
    {
        private uint _seq = 10;

        public bool CanSend { get; set; } = true;

        public List<Intent> Intents { get; } = [];

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }

    /// <summary>The constructs of a world state, a sphere for the terrain.</summary>
    private sealed class StateWorldView(WorldState state) : IToolWorldView
    {
        public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters) => state.Planets.TryGet(planet, out parameters);

        public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit) =>
            new FakeTerrainService().Raycast(planet, origin, direction, maxDistance, out hit);

        public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit) =>
            ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(planet), origin, direction, maxDistance, out hit);

        public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value) => state.Constructs.TryGet(construct, out value);

        public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
        {
            origin = default;
            rotation = Quaternion.Identity;
            return false;
        }

        public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) => false;

        public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination) => 0;
    }

    private sealed class Rig
    {
        public Rig(bool crafting = false)
        {
            State = crafting ? WorldFactory.CreateWithCrafting(players: 2) : WorldFactory.CreateWithFunctionalBlocks(players: 2);
            Sim = new ServerSimulation(State, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
            Pending = new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance);
            Inventory = new InventoryFixture();
            Chests = new ChestSession(Inventory.Inventory, Sender, Sent.Add, NullLogger<ChestSession>.Instance);
            Entities = new ClientBlockEntities();
            Crafting = new CraftingSession(Sender, new GameRules(), NullLogger<CraftingSession>.Instance);
            Stations = new StationSession(
                Inventory.Inventory, Entities, SurvivalContentFactory.CreateTestWithRecipes(), Sender, Sent.Add, ReadTick, new GameRules(),
                NullLogger<StationSession>.Instance);
            Interaction = new BlockInteraction(
                new StateWorldView(State), Sender, Pending, crafting ? CraftingBlocks.ContentFor(State) : FunctionalBlocks.ContentFor(State), Entities, Chests,
                _me, new GameRules(), Crafting, Stations);
        }

        public WorldState State { get; }

        public ServerSimulation Sim { get; }

        public FakeSender Sender { get; } = new();

        public List<Message> Sent { get; } = [];

        public PendingIntentTracker Pending { get; }

        public InventoryFixture Inventory { get; }

        public ChestSession Chests { get; }

        public ClientBlockEntities Entities { get; }

        public CraftingSession Crafting { get; }

        public StationSession Stations { get; }

        public BlockInteraction Interaction { get; }

        public void PlaceBlock(ushort type, CellPos cell) =>
            Sim.ApplyServerOperation(new BlockSetOp(FunctionalBlocks.Construct, cell, BlockState.Create(type, 0)));

        private static bool ReadTick(out ulong tick)
        {
            tick = 0;
            return false;
        }

        public void SetState(CellPos cell, ushort type, byte state) =>
            Sim.ApplyServerOperation(new BlockFunctionStateSetOp(FunctionalBlocks.Construct, cell, type, state));

        public void Replicate(CellPos cell, BlockEntityState state) =>
            Entities.Apply(new BlockEntityStates(FunctionalBlocks.Construct, [new BlockEntityEntry(cell, state)]));

        public static AimRay AimAt(CellPos cell, float distance = 2f)
        {
            var to = _origin + new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            var from = to + new Vector3(0, 0.1f, -distance - 0.5f);
            return new AimRay(WorldFactory.HomePlanet, from, Vector3.Normalize(to - from), -Vector3.UnitZ, from - new Vector3(0, 1.62f, 0));
        }
    }

    [Fact]
    public void AimFurnace_PromptOpenStation_OpensStationSession()
    {
        var rig = new Rig(crafting: true);
        rig.PlaceBlock(CraftingBlocks.FurnaceType, _stationCell);
        var aim = Rig.AimAt(_stationCell);

        // Without the replicated state the containers of the furnace are unknown: no prompt, like a chest.
        rig.Interaction.Observe(aim);
        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.NothingToInteract, rig.Interaction.Interact(aim, 5));

        rig.Replicate(_stationCell, new StationBlockState(new ContainerId(20), new ContainerId(21), string.Empty, null));
        rig.Interaction.Observe(aim);

        Assert.Equal(
            new BlockPrompt(BlockPromptKind.OpenStation, FunctionalBlocks.Construct, _stationCell, StationKind.Furnace), rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.StationRequested, rig.Interaction.Interact(aim, 5));
        Assert.Equal([new ContainerSubscribe(new ContainerId(20)), new ContainerSubscribe(new ContainerId(21))], rig.Sent);
        Assert.True(rig.Stations.IsOpening);
        Assert.Equal(StationKind.Furnace, rig.Stations.Kind);
        Assert.Empty(rig.Sender.Intents);
        Assert.False(rig.Crafting.IsStationOpen);
    }

    [Theory]
    [InlineData(CraftingBlocks.WorkbenchType, StationKind.Workbench)]
    [InlineData(CraftingBlocks.KitchenType, StationKind.Kitchen)]
    [InlineData(CraftingBlocks.FabricatorType, StationKind.Fabricator)]
    public void AimWorkbench_PromptOpenStation_OpensCrafting(ushort type, StationKind kind)
    {
        var rig = new Rig(crafting: true);
        rig.PlaceBlock(type, _stationCell);
        var aim = Rig.AimAt(_stationCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(new BlockPrompt(BlockPromptKind.OpenStation, FunctionalBlocks.Construct, _stationCell, kind), rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.CraftingOpened, rig.Interaction.Interact(aim, 5));
        Assert.True(rig.Crafting.IsStationOpen);
        Assert.Equal(kind, rig.Crafting.Kind);
        Assert.Equal(new BlockRef(FunctionalBlocks.Construct, _stationCell), rig.Crafting.Station);
        Assert.Empty(rig.Sent);
        Assert.Empty(rig.Sender.Intents);
        Assert.False(rig.Stations.IsOpening);
    }

    [Theory]
    [InlineData(CraftingBlocks.AnvilType)]
    [InlineData(CraftingBlocks.ToolrackType)]
    [InlineData(CraftingBlocks.GrindstoneType)]
    public void AimUpgrade_NoPrompt(ushort type)
    {
        var rig = new Rig(crafting: true);
        rig.PlaceBlock(type, _stationCell);
        var aim = Rig.AimAt(_stationCell);

        rig.Interaction.Observe(aim);

        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.NothingToInteract, rig.Interaction.Interact(aim, 5));
        Assert.False(rig.Crafting.IsStationOpen);
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void AimDoor_PromptOpen_SendsToggle()
    {
        var rig = new Rig();
        var aim = Rig.AimAt(FunctionalBlocks.DoorLowCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(new BlockPrompt(BlockPromptKind.OpenDoor, FunctionalBlocks.Construct, FunctionalBlocks.DoorLowCell), rig.Interaction.Prompt);

        var result = rig.Interaction.Interact(aim, 5);

        Assert.Equal(BlockInteractionResult.Sent, result);
        var intent = Assert.IsType<BlockInteractIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(new BlockInteractIntent(FunctionalBlocks.Construct, FunctionalBlocks.DoorLowCell, BlockInteractAction.Toggle), intent);
        Assert.Equal(1, rig.Pending.Count);
    }

    [Fact]
    public void AimOpenDoor_PromptClose_SendsToggle()
    {
        var rig = new Rig();
        rig.SetState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1);
        var aim = Rig.AimAt(FunctionalBlocks.DoorLowCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.CloseDoor, rig.Interaction.Prompt?.Kind);
        Assert.Equal(BlockInteractionResult.Sent, rig.Interaction.Interact(aim, 5));
        Assert.Equal(BlockInteractAction.Toggle, Assert.IsType<BlockInteractIntent>(Assert.Single(rig.Sender.Intents)).Action);
    }

    [Fact]
    public void AimLamp_PromptSwitchOff()
    {
        var rig = new Rig();
        rig.SetState(FunctionalBlocks.LampCell, FunctionalBlocks.LampType, 1);
        var aim = Rig.AimAt(FunctionalBlocks.LampCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.SwitchLightOff, rig.Interaction.Prompt?.Kind);
        Assert.Equal(BlockInteractionResult.Sent, rig.Interaction.Interact(aim, 5));
        Assert.Equal(
            new BlockInteractIntent(FunctionalBlocks.Construct, FunctionalBlocks.LampCell, BlockInteractAction.Toggle),
            Assert.Single(rig.Sender.Intents));
    }

    [Fact]
    public void AimUnlitLamp_PromptSwitchOn()
    {
        var rig = new Rig();

        rig.Interaction.Observe(Rig.AimAt(FunctionalBlocks.LampCell));

        Assert.Equal(BlockPromptKind.SwitchLightOn, rig.Interaction.Prompt?.Kind);
    }

    [Fact]
    public void AimFreeBed_SendsSleep()
    {
        var rig = new Rig();
        rig.Replicate(FunctionalBlocks.BedCell, new BedBlockState(PlayerId.None));
        var aim = Rig.AimAt(FunctionalBlocks.BedCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.Sleep, rig.Interaction.Prompt?.Kind);
        Assert.Equal(BlockInteractionResult.Sent, rig.Interaction.Interact(aim, 5));
        Assert.Equal(
            new BlockInteractIntent(FunctionalBlocks.Construct, FunctionalBlocks.BedCell, BlockInteractAction.Sleep),
            Assert.Single(rig.Sender.Intents));
    }

    [Fact]
    public void AimBedOfOther_PromptOccupied_SendsNothing()
    {
        var rig = new Rig();
        rig.Replicate(FunctionalBlocks.BedCell, new BedBlockState(_other));
        var aim = Rig.AimAt(FunctionalBlocks.BedCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.BedOccupied, rig.Interaction.Prompt?.Kind);
        Assert.Equal(BlockInteractionResult.Refused, rig.Interaction.Interact(aim, 5));
        Assert.Empty(rig.Sender.Intents);
        Assert.Equal(0, rig.Pending.Count);
    }

    [Fact]
    public void AimOwnBed_PromptOwnBed_SendsNothing()
    {
        var rig = new Rig();
        rig.Replicate(FunctionalBlocks.BedCell, new BedBlockState(_me));
        var aim = Rig.AimAt(FunctionalBlocks.BedCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.OwnBed, rig.Interaction.Prompt?.Kind);
        Assert.Equal(BlockInteractionResult.Refused, rig.Interaction.Interact(aim, 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void AimBedWithoutReplicatedState_NoPrompt()
    {
        var rig = new Rig();
        var aim = Rig.AimAt(FunctionalBlocks.BedCell);

        rig.Interaction.Observe(aim);

        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.NothingToInteract, rig.Interaction.Interact(aim, 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void AimChest_SendsSubscribe()
    {
        var rig = new Rig();
        rig.Replicate(FunctionalBlocks.ChestCell, new ChestBlockState(_chestId));
        var aim = Rig.AimAt(FunctionalBlocks.ChestCell);

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.OpenChest, rig.Interaction.Prompt?.Kind);
        Assert.Equal(BlockInteractionResult.ChestRequested, rig.Interaction.Interact(aim, 5));
        Assert.Equal(new ContainerSubscribe(_chestId), Assert.Single(rig.Sent));
        Assert.Empty(rig.Sender.Intents);
        Assert.True(rig.Chests.IsOpening);
        Assert.Equal(FunctionalBlocks.Construct, rig.Chests.Construct);
        Assert.Equal(FunctionalBlocks.ChestCell, rig.Chests.Cell);
    }

    [Fact]
    public void AimLockedChest_PromptLocked_PressRetriesTheSubscription()
    {
        var rig = new Rig();
        rig.Replicate(FunctionalBlocks.ChestCell, new ChestBlockState(_chestId));
        var aim = Rig.AimAt(FunctionalBlocks.ChestCell);
        rig.Interaction.Interact(aim, 5);
        rig.Inventory.Inventory.OnMessage(ContainerState.Rejected(_chestId, RejectReason.ContainerLocked));

        rig.Interaction.Observe(aim);

        Assert.Equal(BlockPromptKind.ChestLocked, rig.Interaction.Prompt?.Kind);

        // The owner may have made the chest shared since: pressing the key asks again.
        Assert.Equal(BlockInteractionResult.ChestRequested, rig.Interaction.Interact(aim, 6));
        Assert.Equal(2, rig.Sent.Count);
    }

    [Fact]
    public void AimChestWithoutReplicatedState_NoPrompt()
    {
        var rig = new Rig();
        var aim = Rig.AimAt(FunctionalBlocks.ChestCell);

        rig.Interaction.Observe(aim);

        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.NothingToInteract, rig.Interaction.Interact(aim, 5));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void AimPlainBlock_NoPrompt()
    {
        var rig = new Rig();
        var aim = Rig.AimAt(FunctionalBlocks.FloorCells[3]);

        rig.Interaction.Observe(aim);

        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.NothingToInteract, rig.Interaction.Interact(aim, 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void PendingFull_Waiting()
    {
        var rig = new Rig();
        for (var i = 0; i < PendingIntentTracker.MaxPending; i++)
        {
            rig.Pending.Add((uint)(100 + i), new RemoveBlockIntent(FunctionalBlocks.Construct, new CellPos(0, 0, 0)), default, 5);
        }

        var aim = Rig.AimAt(FunctionalBlocks.DoorLowCell);

        Assert.Equal(BlockInteractionResult.Waiting, rig.Interaction.Interact(aim, 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void SessionCannotSend_SendsNothing()
    {
        var rig = new Rig();
        rig.Sender.CanSend = false;

        Assert.Equal(BlockInteractionResult.CannotSend, rig.Interaction.Interact(Rig.AimAt(FunctionalBlocks.DoorLowCell), 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void OutOfReach_NoPrompt()
    {
        var rig = new Rig();
        var far = Rig.AimAt(FunctionalBlocks.DoorLowCell, distance: 20f);

        rig.Interaction.Observe(far);

        Assert.Null(rig.Interaction.Prompt);
        Assert.Equal(BlockInteractionResult.NothingToInteract, rig.Interaction.Interact(far, 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void ToolInputFrame_BlockInteract_DefaultsToFalse()
    {
        var frame = new ToolInputFrame(false, false, false, false, null, 0, 0, 0, 0, 0);

        Assert.False(frame.BlockInteract);
        Assert.True((frame with { BlockInteract = true }).BlockInteract);
    }

    [Fact]
    public void ClearAim_DropsThePrompt()
    {
        var rig = new Rig();
        rig.Interaction.Observe(Rig.AimAt(FunctionalBlocks.DoorLowCell));
        Assert.NotNull(rig.Interaction.Prompt);

        rig.Interaction.ClearAim();

        Assert.Null(rig.Interaction.Prompt);
    }
}
