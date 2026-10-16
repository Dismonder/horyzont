using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Integration.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Integration.Tests.Survival;

/// <summary>
/// A client, the in-memory network, the session, the simulation, the inventory, the station service and the vitals service with the hazard content (K6-34 stage C): the home planet is a moon with a vacuum
/// (<c>core:barren_moon</c>), so the oxygen of the suit drains all the time, a refill from the bag and a module in the equipment reach the client through message 44, a running oxygen generator within 10 m
/// protects and restores the oxygen, and a restart brings the suit and the generator back. The start kit hands out two oxygen canisters (bag slot 35), a tank module (36) and four fuel packs (38).
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class HazardFlowTests
{
    private const string Moon = "core:barren_moon";
    private const int CanisterSlot = 35;
    private const int TankSlot = 36;
    private const int FuelSlot = 38;
    private static readonly CellPos _generatorCell = FunctionalBlocks.FreeCell;

    private static readonly ItemTypeId _canister = WorldFactory.Item(SurvivalContentFactory.OxygenCanisterKey);
    private static readonly ItemTypeId _tank = WorldFactory.Item(SurvivalContentFactory.TankModuleKey);
    private static readonly ItemTypeId _fuel = WorldFactory.Item(SurvivalContentFactory.FuelPackKey);

    /// <summary>Sets the oxygen of a player (outside the journal, as the vitals service does).</summary>
    private static void SetOxygen(FoodRig rig, PlayerId player, float oxygen)
    {
        Assert.True(rig.Runtime.State.Players.TryGet(player, out var state));
        var vitals = state.Vitals ?? throw new InvalidOperationException("no vitals yet");
        Assert.True(rig.Runtime.Simulation.UpdatePlayerVitals(player, vitals with { Suit = (vitals.Suit ?? new SuitResources(100f, 100f, 100f, 100f, 100f)) with { Oxygen = oxygen } }));
    }

    /// <summary>A floor of five blocks and an oxygen generator on its top face (the cell 5.5 m from the pole), burning from now with <paramref name="queued"/> fuel packs behind the burning one.</summary>
    private static ContainerId BuildGenerator(FoodRig rig, PlayerId owner, ushort queued)
    {
        var sim = rig.Runtime.Simulation;
        var construct = rig.Runtime.State.EntityIds.PeekNext();
        sim.ApplyServerOperation(new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, FunctionalBlocks.FloorCells[0], BlockState.Create(FunctionalBlocks.StoneType, 0)));
        foreach (var cell in FunctionalBlocks.FloorCells.Skip(1))
        {
            sim.ApplyServerOperation(new BlockSetOp(construct, cell, BlockState.Create(FunctionalBlocks.StoneType, 0)));
        }

        var input = rig.Runtime.State.ContainerIds.PeekNext();
        sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, ContainerHost.ConstructBlock(construct, _generatorCell), owner, ContainerAccess.Shared, 2),
            new BlockEntitySetOp(construct, _generatorCell, HazardBlocks.GeneratorType, new OxygenGeneratorBlockState(input, rig.Runtime.State.CurrentTick)),
            new BlockSetOp(construct, _generatorCell, BlockState.Create(HazardBlocks.GeneratorType, 0)),
        ]));
        if (queued > 0)
        {
            sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(input, 0), _fuel, queued, 100, ItemSource.Admin));
        }

        return input;
    }

    [Fact]
    public async Task RefillOxygen_RemoteClient_DeltaAndVitals()
    {
        using var dir = new TempDir();
        await using var rig = new FoodRig(dir.Path, hazards: true, homeProfile: Moon);
        await rig.StartAsync();
        var player = await rig.JoinAsync(1);
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Suit.Oxygen < 100f && (m.Environment.Present & 0x10) != 0), "the moon did not drain the oxygen");
        var bag = rig.Mirror.IdOf(ContainerKind.PlayerBag);
        Assert.Equal(new ItemStack(_canister, 2), rig.Mirror.Slot(ContainerKind.PlayerBag, CanisterSlot));
        SetOxygen(rig, player, 20f);

        var reply = await rig.SubmitAsync(new SuitRefillIntent(bag, CanisterSlot));

        // One canister is consumed (the delta reaches the mirror) and the next message 44 carries about 70 oxygen: 20 plus the 50 of the canister, less the few ticks of drain.
        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.True(await rig.RunUntilAsync(() => rig.Mirror.Slot(ContainerKind.PlayerBag, CanisterSlot).Count == 1), "the canister did not leave the bag");
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Suit.Oxygen is > 65f and <= 70f), "the oxygen did not rise by the canister");
        await rig.DisconnectAndStopAsync();
    }

    [Fact]
    public async Task MoonWalk_WithRunningGenerator_NoDamage()
    {
        using var dir = new TempDir();
        await using var rig = new FoodRig(dir.Path, hazards: true, homeProfile: Moon);
        await rig.StartAsync();
        var player = await rig.JoinAsync(1);
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Suit.Oxygen < 100f), "no vitals with the drain arrived");
        SetOxygen(rig, player, 20f);

        // Without a generator the oxygen only goes down, and nothing protects against the vacuum.
        rig.StepMany(30);
        var drained = rig.Latest<PlayerVitalsMessage>()!;
        Assert.True(drained.Suit.Oxygen < 20f, "the oxygen should have drained: " + drained.Suit);
        Assert.False(drained.Environment.Generator);

        // A generator that burns for ten seconds, five metres away: the vacuum is blocked, the oxygen restores at 5 a second, the health stays full.
        BuildGenerator(rig, player, queued: 0);
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Environment.Generator && m.Suit.Oxygen > 25f), "the generator did not protect and restore the oxygen");
        var protectedMessage = rig.Latest<PlayerVitalsMessage>()!;
        Assert.Equal(100f, protectedMessage.Health, 0.5f);
        Assert.True((protectedMessage.Environment.Present & 0x10) != 0);
        Assert.True((protectedMessage.Environment.Active & 0x10) == 0);
        await rig.DisconnectAndStopAsync();
    }

    [Fact]
    public async Task EquipModule_RemoteClient_CapacityInVitals()
    {
        using var dir = new TempDir();
        await using var rig = new FoodRig(dir.Path, hazards: true);
        await rig.StartAsync();
        await rig.JoinAsync(1);
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Capacity.Oxygen == 100f), "no vitals arrived");
        var bag = rig.Mirror.IdOf(ContainerKind.PlayerBag);
        var equipment = rig.Mirror.IdOf(ContainerKind.Equipment);

        var reply = await rig.SubmitAsync(new ItemMoveIntent(new SlotRef(bag, TankSlot), ItemMoveTarget.AnyIn(equipment), 1, _tank, 1));

        // The tank takes a module slot (the first one is slot 4) and the capacity of the oxygen becomes 200 in the next message.
        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.True(await rig.RunUntilAsync(() => rig.Mirror.Slot(ContainerKind.Equipment, 4) == new ItemStack(_tank, 1)), "the tank did not reach the equipment");
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Capacity.Oxygen == 200f), "the capacity did not rise");
        await rig.DisconnectAndStopAsync();
    }

    [Fact]
    public async Task Restart_SuitAndGeneratorRestored()
    {
        using var dir = new TempDir();
        ulong hash;
        PlayerVitals? saved;
        PlayerId player;
        OxygenGeneratorBlockState generator;
        await using (var first = new FoodRig(dir.Path, hazards: true, homeProfile: Moon))
        {
            await first.StartAsync();
            player = await first.JoinAsync(1);
            Assert.True(await first.WaitForAsync<PlayerVitalsMessage>(m => m.Suit.Oxygen < 100f), "no vitals with the drain arrived");
            var input = BuildGenerator(first, player, queued: 2);
            SetOxygen(first, player, 42f);
            first.StepMany(20);
            await first.DisconnectAndStopAsync();
            Assert.True(first.Runtime.State.Players.TryGet(player, out var state));
            saved = state.Vitals;
            Assert.True(first.Runtime.State.BlockEntities.TryGet(new BlockRef(first.Runtime.State.Constructs.All[0].Id, _generatorCell), out var entity));
            generator = Assert.IsType<OxygenGeneratorBlockState>(entity);
            Assert.Equal(input, generator.Input);
            hash = first.Hash;
        }

        Assert.NotNull(saved);
        Assert.NotNull(saved.Value.Suit);
        Assert.InRange(saved.Value.Suit!.Value.Oxygen, 40f, 50f);

        await using var second = new FoodRig(dir.Path, hazards: true, homeProfile: Moon);
        await second.StartAsync();

        // The suit is not part of the hash, the generator is: the hash is the same, the suit comes back from the checkpoint and the generator keeps its start and its fuel.
        Assert.Equal(hash, second.Hash);
        Assert.True(second.Runtime.State.Players.TryGet(player, out var restored));
        Assert.Equal(saved, restored.Vitals);
        Assert.True(second.Runtime.State.BlockEntities.TryGet(new BlockRef(second.Runtime.State.Constructs.All[0].Id, _generatorCell), out var restoredEntity));
        Assert.Equal(generator, restoredEntity);
        Assert.True(second.Runtime.State.Containers.TryGet(generator.Input, out var fuel));
        Assert.Equal(new ItemStack(_fuel, 2), fuel.GetSlot(0));
        await second.StopAsync();
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Safe to leave: the directory is unique and under the temp directory.
            }
        }
    }
}
