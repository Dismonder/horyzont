using Horyzont.Core.Content;
using Horyzont.Core.Hashing;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>Mining yield and the fill cost (K6-29W D8-D12). Test world: material 1 stone (2 stone/m³), 2 dirt (3 soil/m³), 3 bedrock.</summary>
public sealed class MiningYieldTests
{
    private const byte StoneMaterial = 1;
    private const byte DirtMaterial = 2;
    private const byte BedrockMaterial = 3;

    private static SurvivalContent ContentWith(string stoneYield = "2", string dirtYield = "3") => ContentLoader.Parse(
        SurvivalContentFactory.ItemsJson,
        SurvivalContentFactory.YieldsJson
            .Replace("\"itemsPerCubicMeter\": 2", $"\"itemsPerCubicMeter\": {stoneYield}", StringComparison.Ordinal)
            .Replace("\"itemsPerCubicMeter\": 3", $"\"itemsPerCubicMeter\": {dirtYield}", StringComparison.Ordinal),
        SurvivalContentFactory.StartJson);

    private static ItemFixture Fixture(SurvivalContent? content = null) =>
        new(terrain: new FakeTerrainService(), content: content ?? SurvivalContentFactory.CreateTest());

    private static IntentResult Dig(ItemFixture f) => f.Sim.Sim.Submit(Player1, f.Sim.Dig(Player1, f.Sim.Ground(2)));

    private static IntentResult Fill(ItemFixture f, byte material) =>
        f.Sim.Sim.Submit(Player1, f.Sim.Fill(Player1, f.Sim.Ground(2), material: material));

    private static void Tick(ItemFixture f) => f.Sim.Ticks(60);

    [Fact]
    public void Dig_Stone_CompoundOfHarvestCreatesThenTerrainEdit()
    {
        var f = Fixture();
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);

        var result = Dig(f);

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
        Assert.Equal(2, compound.Components.Count);
        Assert.Equal(new ItemCreateOp(BagSlot(0), Stone, 6, 500, ItemSource.Harvest), compound.Components[0]);
        var edit = Assert.IsType<TerrainEditOp>(compound.Components[1]);
        Assert.Equal(TerrainEditMode.Remove, edit.Mode);
        Assert.Equal(new ItemStack(Stone, 6), f.Slot(BagSlot(0)));
        Assert.Single(f.Sim.Terrain.AppliedEdits);
        Assert.Equal(1, f.Sim.Terrain.MeasureCalls);
        Assert.Same(compound, f.Sim.Journal.Entries[^1].Operation);
    }

    [Fact]
    public void Dig_TwoMaterials_OneCreatePerMaterialInMaterialOrder()
    {
        var f = Fixture();
        f.Sim.Terrain.MeasureResult = new MeasuredSphere(new Dictionary<byte, int> { [DirtMaterial] = 2, [StoneMaterial] = 1 }, 4);

        var compound = Assert.IsType<CompoundOp>(Dig(f).Applied!.Operation);

        Assert.Equal(
            [new ItemCreateOp(BagSlot(0), Stone, 2, 500, ItemSource.Harvest), new ItemCreateOp(BagSlot(1), Soil, 6, 500, ItemSource.Harvest)],
            compound.Components.Take(2));
    }

    [Fact]
    public void Dig_Fraction_AccumulatesAcrossDigs()
    {
        var f = Fixture(ContentWith(stoneYield: "0.4"));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 1);

        var first = Dig(f);
        Tick(f);
        var second = Dig(f);
        Tick(f);
        var third = Dig(f);

        Assert.IsType<TerrainEditOp>(first.Applied!.Operation);
        Assert.IsType<TerrainEditOp>(second.Applied!.Operation);
        var compound = Assert.IsType<CompoundOp>(third.Applied!.Operation);
        Assert.Equal(new ItemCreateOp(BagSlot(0), Stone, 1, 500, ItemSource.Harvest), compound.Components[0]);
        Assert.Equal(new ItemStack(Stone, 1), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void Dig_Bedrock_PlainTerrainEdit()
    {
        var f = Fixture();
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(BedrockMaterial, 5);

        var result = Dig(f);

        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
        Assert.All(f.Container(Bag).Slots.ToArray(), s => Assert.True(s.IsEmpty));
    }

    [Fact]
    public void Dig_NothingMeasurable_PlainTerrainEdit()
    {
        var f = Fixture();

        var result = Dig(f);

        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
        Assert.Equal(1, f.Sim.Terrain.MeasureCalls);
    }

    [Fact]
    public void Dig_MergesIntoHotbarStackFirst()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(0), Stone, 10));
        f.Apply(Create(HotbarSlot(4), Stone, 10));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);

        var compound = Assert.IsType<CompoundOp>(Dig(f).Applied!.Operation);

        Assert.Equal(new ItemCreateOp(HotbarSlot(4), Stone, 6, 500, ItemSource.Harvest), compound.Components[0]);
        Assert.Equal(new ItemStack(Stone, 16), f.Slot(HotbarSlot(4)));
        Assert.Equal(new ItemStack(Stone, 10), f.Slot(BagSlot(0)));
    }

    [Fact]
    public void Dig_StacksFull_FillsTheBagThenTheHotbar()
    {
        var f = Fixture();
        f.Apply(ItemMoveIntentTests.FillWithTools(Bag, WorldFactory.BagSize - 1));
        f.Apply(Create(HotbarSlot(2), Stone, 498));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 501);

        var compound = Assert.IsType<CompoundOp>(Dig(f).Applied!.Operation);

        Assert.Equal(
            [
                new ItemCreateOp(HotbarSlot(2), Stone, 2, 500, ItemSource.Harvest),
                new ItemCreateOp(BagSlot(WorldFactory.BagSize - 1), Stone, 500, 500, ItemSource.Harvest),
                new ItemCreateOp(HotbarSlot(0), Stone, 500, 500, ItemSource.Harvest),
            ],
            compound.Components.Take(3));
    }

    [Fact]
    public void Dig_FullInventory_EditAppliedOverflowNotCreated()
    {
        var f = Fixture();
        f.Apply(ItemMoveIntentTests.FillWithTools(Bag, WorldFactory.BagSize));
        f.Apply(ItemMoveIntentTests.FillWithTools(Hotbar, WorldFactory.HotbarSize));
        var hash = StateHasher.Hash(f.State);
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);

        var result = Dig(f);

        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
        Assert.Single(f.Sim.Terrain.AppliedEdits);
        Assert.Equal(WorldFactory.BagSize + WorldFactory.HotbarSize, CountOf(f.State, Tool));
        Assert.Equal(0, CountOf(f.State, Stone));
        Assert.NotEqual(hash, StateHasher.Hash(f.State));
    }

    [Fact]
    public void Dig_PartlyFull_CreatesWhatFits()
    {
        var f = Fixture();
        f.Apply(ItemMoveIntentTests.FillWithTools(Bag, WorldFactory.BagSize));
        f.Apply(ItemMoveIntentTests.FillWithTools(Hotbar, WorldFactory.HotbarSize - 1));
        f.Apply(Create(HotbarSlot(WorldFactory.HotbarSize - 1), Stone, 497));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);

        var compound = Assert.IsType<CompoundOp>(Dig(f).Applied!.Operation);

        Assert.Equal(new ItemCreateOp(HotbarSlot(WorldFactory.HotbarSize - 1), Stone, 3, 500, ItemSource.Harvest), compound.Components[0]);
        Assert.Equal(500, CountOf(f.State, Stone));
    }

    [Fact]
    public void Fill_ConsumesSoilBuildCost()
    {
        var f = Fixture();
        f.Apply(Create(HotbarSlot(2), Soil, 5));
        f.Apply(Create(BagSlot(3), Soil, 4));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(2);

        var result = Fill(f, DirtMaterial);

        Assert.True(result.IsAccepted, $"{result.Reason}");
        var compound = Assert.IsType<CompoundOp>(result.Applied!.Operation);
        Assert.Equal(
            [new ItemDestroyOp(BagSlot(3), Soil, 4, ItemSink.BuildCost), new ItemDestroyOp(HotbarSlot(2), Soil, 2, ItemSink.BuildCost)],
            compound.Components.Take(2));
        var edit = Assert.IsType<TerrainEditOp>(compound.Components[2]);
        Assert.Equal(TerrainEditMode.Add, edit.Mode);
        Assert.Equal(DirtMaterial, edit.MaterialId);
        Assert.Equal(3, CountOf(f.State, Soil));
        Assert.Single(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void Fill_WithoutSoil_NotEnoughItems_TerrainUntouched()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(3), Soil, 5));
        f.Apply(Create(BagSlot(4), Stone, 50));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(2);
        var hash = StateHasher.Hash(f.State);
        var journaled = f.Sim.Journal.Entries.Count;

        Assert.Equal(RejectReason.NotEnoughItems, Fill(f, DirtMaterial).Reason);

        Assert.Empty(f.Sim.Terrain.AppliedEdits);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journaled, f.Sim.Journal.Entries.Count);
    }

    [Fact]
    public void Fill_MaterialWithoutItem_NotEnoughItems()
    {
        var f = Fixture();
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(1);

        Assert.Equal(RejectReason.NotEnoughItems, Fill(f, BedrockMaterial).Reason);
        Assert.Empty(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void Fill_NothingToFill_FreePlainEdit()
    {
        var f = Fixture();
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(0);

        var result = Fill(f, DirtMaterial);

        Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
    }

    [Fact]
    public void Fill_NotMeasurable_AreaNotLoaded()
    {
        var f = Fixture();
        f.Apply(Create(BagSlot(3), Soil, 50));

        Assert.Equal(RejectReason.AreaNotLoaded, Fill(f, DirtMaterial).Reason);
        Assert.Empty(f.Sim.Terrain.AppliedEdits);
    }

    [Fact]
    public void Fill_Credit_CarriesOverpayment()
    {
        var f = Fixture(ContentWith(dirtYield: "1.5"));
        f.Apply(Create(BagSlot(0), Soil, 10));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(1);

        Assert.True(Fill(f, DirtMaterial).IsAccepted);
        Assert.Equal(8, CountOf(f.State, Soil));
        Tick(f);
        Assert.True(Fill(f, DirtMaterial).IsAccepted);
        Assert.Equal(7, CountOf(f.State, Soil));
        Tick(f);
        Assert.True(Fill(f, DirtMaterial).IsAccepted);
        Assert.Equal(5, CountOf(f.State, Soil));
    }

    [Fact]
    public void Fill_Rejected_DoesNotChangeTheCredit()
    {
        var f = Fixture(ContentWith(dirtYield: "1.5"));
        f.Apply(Create(BagSlot(0), Soil, 2));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(1);
        Assert.True(Fill(f, DirtMaterial).IsAccepted);
        Assert.Equal(0, CountOf(f.State, Soil));

        // Credit 0.5: a fill of 3 voxels needs 4 soil and is refused; had it been charged, the credit would drop to 0.
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(3);
        Tick(f);
        Assert.Equal(RejectReason.NotEnoughItems, Fill(f, DirtMaterial).Reason);

        // With the credit kept, one voxel needs 1.5 - 0.5 = 1 soil (2 with a lost credit).
        f.Apply(Create(BagSlot(0), Soil, 3));
        f.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(1);
        Assert.True(Fill(f, DirtMaterial).IsAccepted);
        Assert.Equal(2, CountOf(f.State, Soil));
    }

    [Fact]
    public void NoContent_BehavesAsK2()
    {
        var f = new ItemFixture(terrain: new FakeTerrainService());
        f.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);

        var dig = Dig(f);
        Tick(f);
        var fill = Fill(f, DirtMaterial);

        Assert.IsType<TerrainEditOp>(dig.Applied!.Operation);
        Assert.IsType<TerrainEditOp>(fill.Applied!.Operation);
        Assert.Equal(0, f.Sim.Terrain.MeasureCalls);
        Assert.Equal(0, CountOf(f.State, Stone));
        Assert.Null(f.Sim.Sim.Survival);
    }

    [Fact]
    public void Replay_DigJournal_NeverMeasures()
    {
        var live = Fixture();
        live.Sim.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);
        Assert.True(Dig(live).IsAccepted);
        live.Apply(Create(BagSlot(9), Soil, 20));
        live.Sim.Terrain.MeasureResult = MeasuredSphere.AirOf(2);
        Tick(live);
        Assert.True(Fill(live, DirtMaterial).IsAccepted);

        var terrain = new FakeTerrainService();
        var state = WorldFactory.CreateWithPlayerContainers();
        var fresh = new SimFixture(terrain: terrain, state: state, survival: SurvivalContentFactory.CreateTest());
        foreach (var entry in live.Sim.Journal.Entries)
        {
            fresh.Sim.Replay(entry);
        }

        Assert.Equal(0, terrain.MeasureCalls);
        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fresh.State));
        Assert.Equal(live.Sim.Terrain.AppliedEdits, terrain.AppliedEdits);
    }

    private static int CountOf(WorldState state, Core.Ids.ItemTypeId type)
    {
        var total = 0;
        foreach (var container in state.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                if (stack.Type == type)
                {
                    total += stack.Count;
                }
            }
        }

        return total;
    }
}
