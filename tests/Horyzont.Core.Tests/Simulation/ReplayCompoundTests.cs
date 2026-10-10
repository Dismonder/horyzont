using Horyzont.Core.Content;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Items;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Items.ItemFixture;

namespace Horyzont.Core.Tests.Simulation;

public sealed class ReplayCompoundTests
{
    [Fact]
    public void Replay_CompoundWithTerrain_LoadsAreaFirst()
    {
        var live = new ItemFixture();
        live.Apply(new CompoundOp([Create(BagSlot(0), Stone, 6, source: ItemSource.Harvest), CompoundOpTests.Dig(live)]));
        var entry = live.Sim.Journal.Entries[^1];
        var terrain = new FakeTerrainService();
        var loadsAtApply = -1;
        terrain.OnApplyEdit = (_, _) => loadsAtApply = terrain.BlockingLoads.Count;
        var fresh = new ItemFixture(terrain: terrain);

        fresh.Sim.Sim.Replay(entry);

        Assert.Equal(1, loadsAtApply);
        Assert.Single(terrain.BlockingLoads);
        Assert.Equal(live.Sim.Terrain.AppliedEdits, terrain.AppliedEdits);
        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fresh.State));
        Assert.Equal(entry.Tick, fresh.State.CurrentTick);
    }

    [Fact]
    public void Replay_CompoundWithoutTerrain_LoadsNothing()
    {
        var live = new ItemFixture();
        live.Apply(new CompoundOp([Create(BagSlot(0), Stone, 6), Transfer(BagSlot(0), HotbarSlot(0), Stone, 2)]));
        var fresh = new ItemFixture();

        fresh.Sim.Sim.Replay(live.Sim.Journal.Entries[^1]);

        Assert.Empty(fresh.Sim.Terrain.BlockingLoads);
        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fresh.State));
    }

    [Fact]
    public void Replay_AfterTheItemCatalogChanged_GivesTheSameHash()
    {
        var live = new ItemFixture(content: SurvivalContentFactory.CreateTest());
        live.Apply(Create(BagSlot(0), Stone, 30));
        live.Apply(Create(BagSlot(1), Stone, 20));
        Assert.True(live.Sim.Sim.Submit(Player1, new Horyzont.Core.Intents.ItemMoveIntent(BagSlot(1), Horyzont.Core.Intents.ItemMoveTarget.ToSlot(BagSlot(0)), 20, Stone, 20)).IsAccepted);
        Assert.True(live.Sim.Sim.Submit(Player1, new Horyzont.Core.Intents.ItemMoveIntent(BagSlot(0), Horyzont.Core.Intents.ItemMoveTarget.AnyIn(Hotbar), 45, Stone, 50)).IsAccepted);
        Assert.True(live.Sim.Sim.Submit(Player1, new Horyzont.Core.Intents.ItemDropIntent(BagSlot(0), 1, Stone, 5)).IsAccepted);

        // The catalog now limits stone to 10 per stack; the journal operations carry their own limits.
        var lowered = ContentLoader.Parse(
            SurvivalContentFactory.ItemsJson.Replace(
                "\"key\": \"test:stone\", \"category\": \"resource\", \"flags\": [\"bulk\"]",
                "\"key\": \"test:stone\", \"category\": \"resource\", \"flags\": [\"bulk\"], \"maxStack\": 10, \"maxStackOverride\": true",
                StringComparison.Ordinal),
            SurvivalContentFactory.YieldsJson,
            SurvivalContentFactory.StartJson);
        Assert.Equal(10, lowered.Items.TryGet(SurvivalContentFactory.StoneKey, out var def) ? def.MaxStack : -1);
        var fresh = new ItemFixture(content: lowered);

        foreach (var entry in live.Sim.Journal.Entries)
        {
            fresh.Sim.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fresh.State));
    }

    [Fact]
    public void Replay_BrokenComponent_ReplayFailedWithComponentIndex()
    {
        var fresh = new ItemFixture();
        var hash = StateHasher.Hash(fresh.State);
        var id = fresh.State.LastOpId.Next;
        var entry = new JournalEntry(id, new Tick(5), null, new CompoundOp(
        [
            Create(BagSlot(0), Stone, 6),
            Destroy(BagSlot(0), Stone, 7),
            CompoundOpTests.Dig(fresh),
        ]));

        var exception = Assert.Throws<ReplayFailedException>(() => fresh.Sim.Sim.Replay(entry));

        Assert.Equal(id, exception.OpId);
        Assert.Equal(OperationKind.Compound, exception.Kind);
        Assert.Equal(1, exception.ComponentIndex);
        Assert.Equal(hash, StateHasher.Hash(fresh.State));
        Assert.Empty(fresh.Sim.Terrain.AppliedEdits);
        Assert.False(fresh.Sim.Sim.IsFaulted);
    }

    [Fact]
    public void Replay_BrokenTerrainComponent_FailsBeforeLoading()
    {
        var fresh = new ItemFixture();
        var entry = new JournalEntry(fresh.State.LastOpId.Next, new Tick(5), null, new CompoundOp(
        [
            Create(BagSlot(0), Stone, 6),
            CompoundOpTests.Dig(fresh) with { Radius = 1000f },
        ]));

        var exception = Assert.Throws<ReplayFailedException>(() => fresh.Sim.Sim.Replay(entry));

        Assert.Equal(1, exception.ComponentIndex);
        Assert.Empty(fresh.Sim.Terrain.BlockingLoads);
    }

    [Fact]
    public void Replay_PlainOperation_ComponentIndexNull()
    {
        var fresh = new ItemFixture();
        var entry = new JournalEntry(fresh.State.LastOpId.Next, new Tick(5), null, Destroy(BagSlot(0), Stone, 1));

        var exception = Assert.Throws<ReplayFailedException>(() => fresh.Sim.Sim.Replay(entry));

        Assert.Null(exception.ComponentIndex);
        Assert.Equal(OperationKind.ItemDestroy, exception.Kind);
    }
}
