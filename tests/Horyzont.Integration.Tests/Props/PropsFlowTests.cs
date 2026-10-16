using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Integration.Tests.Support;
using Horyzont.Persistence.Faults;
using Horyzont.Testing;

namespace Horyzont.Integration.Tests.Props;

/// <summary>
/// Clients, in-memory transport, session, simulation, regrowth, replication and writer: a prop is hit and harvested, regrows and is dug
/// away, and the harvested props are the same for two clients and after a restart (K6-31 stage C, the criterion of the spec: "zebrane i
/// odrośnięte obiekty identyczne u 2 klientów i po restarcie"). The test content has a pine of 3 hit points that regrows after 10 s (600
/// ticks), and the tool in hotbar slot 0 that every new player gets with the start kit.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class PropsFlowTests
{
    private const byte StoneMaterial = 1;
    private static readonly PlanetId _planet = WorldFactory.HomePlanet;

    private static float Surface => PropsRig.Surface + 0.05f;

    private static async Task<(PropInstance Tree, PropsPlayer Alice, PropsPlayer Bob)> StartWithTwoAsync(PropsRig rig)
    {
        await rig.StartAsync();
        var tree = rig.FindProp(PropKindV1.Pine);
        var alice = await rig.JoinAsync(1, PropFixtures.PointBeside(tree, 3f, Surface));
        var bob = await rig.JoinAsync(2, PropFixtures.PointBeside(tree, 6f, Surface));
        Assert.True(
            await rig.RunUntilAsync(() => alice.Props.HasRegion(tree.Id.Region) && bob.Props.HasRegion(tree.Id.Region)), "the prop states did not arrive");
        return (tree, alice, bob);
    }

    private static async Task DisconnectAllAsync(PropsRig rig)
    {
        foreach (var player in rig.Players)
        {
            player.Client.Disconnect();
        }

        Assert.True(await rig.RunUntilAsync(() => !rig.Runtime.State.Players.Online.Any()));
    }

    private static void MoveAway(PropsRig rig, PropInstance tree, params PropsPlayer[] players)
    {
        foreach (var player in players)
        {
            rig.PlaceBeside(player, tree, 120f);
        }
    }

    [Fact]
    public async Task TwoClients_HarvestTree_BothSeeHarvested()
    {
        using var dir = new TempDir();
        await using var rig = new PropsRig(dir.Path);
        var (tree, alice, bob) = await StartWithTwoAsync(rig);
        Assert.False(alice.Props.IsHarvested(tree.Id));
        Assert.False(bob.Props.IsHarvested(tree.Id));
        var wood = rig.Count(alice.Player, SurvivalContentFactory.WoodKey);

        var reply = await rig.HarvestAsync(alice, tree);

        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.True(await rig.RunUntilAsync(() => alice.Props.IsHarvested(tree.Id) && bob.Props.IsHarvested(tree.Id)), "the harvest did not arrive");
        Assert.True(rig.Runtime.State.HarvestedProps.TryGetTick(_planet, tree.Id, out var tick));
        Assert.Equal(tick, alice.Props.TickOf(tree.Id));
        Assert.Equal(tick, bob.Props.TickOf(tree.Id));
        Assert.Equal(alice.Props.Snapshot(), bob.Props.Snapshot());
        Assert.Equal(wood + 3, rig.Count(alice.Player, SurvivalContentFactory.WoodKey));

        // A second hit on the harvested tree is refused for both players.
        var late = await rig.HitAsync(bob, tree.Id);
        Assert.Equal(RejectReason.NoHit, late.Reason);
        await DisconnectAllAsync(rig);
        await rig.StopAsync();
    }

    [Fact]
    public async Task TwoClients_Regrow_BothSeeRegrown()
    {
        using var dir = new TempDir();
        await using var rig = new PropsRig(dir.Path);
        var (tree, alice, bob) = await StartWithTwoAsync(rig);
        await rig.HarvestAsync(alice, tree);
        Assert.True(await rig.RunUntilAsync(() => alice.Props.IsHarvested(tree.Id) && bob.Props.IsHarvested(tree.Id)));

        // The players stay within 20 m: the tree waits well past its 10 s.
        rig.StepMany(900);
        Assert.True(rig.Runtime.State.HarvestedProps.Contains(_planet, tree.Id));
        Assert.True(alice.Props.IsHarvested(tree.Id));

        // They walk 120 m away (still in the interest of the regions): the next scan regrows the tree.
        MoveAway(rig, tree, alice, bob);
        Assert.True(
            await rig.RunUntilAsync(() => !alice.Props.IsHarvested(tree.Id) && !bob.Props.IsHarvested(tree.Id), maxFrames: 3000), "the tree did not regrow");

        Assert.False(rig.Runtime.State.HarvestedProps.Contains(_planet, tree.Id));
        Assert.Equal(alice.Props.Snapshot(), bob.Props.Snapshot());
        await DisconnectAllAsync(rig);
        await rig.StopAsync();
    }

    [Fact]
    public async Task Harvest_Restart_HarvestedIdentical_ClientsGetSameStates()
    {
        using var dir = new TempDir();
        ulong hash;
        PropInstance tree;
        Tick tick;
        await using (var first = new PropsRig(dir.Path))
        {
            var (found, alice, bob) = await StartWithTwoAsync(first);
            tree = found;
            await first.HarvestAsync(alice, tree);
            Assert.True(await first.RunUntilAsync(() => alice.Props.IsHarvested(tree.Id) && bob.Props.IsHarvested(tree.Id)));
            Assert.True(first.Runtime.State.HarvestedProps.TryGetTick(_planet, tree.Id, out tick));
            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
        }

        await using var second = new PropsRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        Assert.True(second.Runtime.State.HarvestedProps.TryGetTick(_planet, tree.Id, out var restored));
        Assert.Equal(tick, restored);
        var alice2 = await second.JoinAsync(1, PropFixtures.PointBeside(tree, 3f, Surface));
        var bob2 = await second.JoinAsync(2, PropFixtures.PointBeside(tree, 6f, Surface));
        Assert.True(
            await second.RunUntilAsync(() => alice2.Props.HasRegion(tree.Id.Region) && bob2.Props.HasRegion(tree.Id.Region)),
            "the prop states did not arrive after the restart");
        Assert.True(alice2.Props.IsHarvested(tree.Id));
        Assert.Equal(tick, alice2.Props.TickOf(tree.Id));
        Assert.Equal(alice2.Props.Snapshot(), bob2.Props.Snapshot());
        await second.StopAsync();
    }

    [Fact]
    public async Task Regrow_Restart_StateIdentical()
    {
        using var dir = new TempDir();
        ulong hash;
        PropInstance tree;
        await using (var first = new PropsRig(dir.Path))
        {
            var (found, alice, bob) = await StartWithTwoAsync(first);
            tree = found;
            await first.HarvestAsync(alice, tree);
            MoveAway(first, tree, alice, bob);
            Assert.True(
                await first.RunUntilAsync(() => !first.Runtime.State.HarvestedProps.Contains(_planet, tree.Id) && alice.Props.HasRegion(tree.Id.Region), maxFrames: 3000),
                "the tree did not regrow");
            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
        }

        await using var second = new PropsRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        Assert.Equal(0, second.Runtime.State.HarvestedProps.Count);
        var alice2 = await second.JoinAsync(1, PropFixtures.PointBeside(tree, 3f, Surface));
        Assert.True(await second.RunUntilAsync(() => alice2.Props.HasRegion(tree.Id.Region)), "the prop states did not arrive after the restart");
        Assert.False(alice2.Props.IsHarvested(tree.Id));
        Assert.Empty(alice2.Props.Snapshot());
        await second.StopAsync();
    }

    [Theory]
    [InlineData(FaultPoint.BeforeJournalCommit, false, 0)]
    [InlineData(FaultPoint.AfterJournalCommit, true, 3)]
    public async Task CrashMidHarvest_WholeOrNothing(FaultPoint point, bool harvested, int woodGained)
    {
        using var dir = new TempDir();
        var faults = new ArmedFaults();
        PropInstance tree;
        PlayerId player;
        int woodBefore;
        await using (var crashed = new PropsRig(dir.Path, faults))
        {
            var (found, alice, _) = await StartWithTwoAsync(crashed);
            tree = found;
            player = alice.Player;
            woodBefore = crashed.Count(player, SurvivalContentFactory.WoodKey);

            // Two hits leave no operation behind; everything before the last hit is durable, then the process dies while the harvest is written.
            await crashed.HitAsync(alice, tree.Id);
            await crashed.HitAsync(alice, tree.Id);
            Assert.True(await crashed.RunUntilAsync(() => RawJournalRows(dir.Path) >= (long)crashed.Runtime.State.LastOpId.Value));
            faults.Arm(point);
            var last = await crashed.HitAsync(alice, tree.Id);
            Assert.Equal(RejectReason.None, last.Reason);
            Assert.True(await crashed.RunUntilAsync(() => faults.Crashed));
            crashed.Runtime.RequestShutdown();
            for (var i = 0; i < 8000 && !crashed.Runtime.IsStopped; i++)
            {
                crashed.Step();
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }

        await using var restarted = new PropsRig(dir.Path);
        await restarted.StartAsync();

        // The harvest and the wood it gave are one operation: both are there or neither is.
        Assert.Equal(harvested, restarted.Runtime.State.HarvestedProps.Contains(_planet, tree.Id));
        Assert.Equal(woodBefore + woodGained, restarted.Count(player, SurvivalContentFactory.WoodKey));
        await restarted.StopAsync();
    }

    [Fact]
    public async Task DigUnderTree_TreeGoneForBoth_NeverRegrows()
    {
        using var dir = new TempDir();
        await using var rig = new PropsRig(dir.Path);
        var (tree, alice, bob) = await StartWithTwoAsync(rig);
        rig.Terrain.Last.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);
        Assert.True(rig.Runtime.State.Players.TryGet(alice.Player, out var body));
        var eye = body.EyePosition(rig.Runtime.Simulation.Rules);

        var reply = await rig.SubmitAsync(
            alice, new TerrainToolIntent(_planet, eye, System.Numerics.Vector3.Normalize(tree.Anchor - eye), 1f, TerrainEditMode.Remove, 0));

        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.True(await rig.RunUntilAsync(() => alice.Props.IsHarvested(tree.Id) && bob.Props.IsHarvested(tree.Id)), "the removal did not arrive");
        Assert.Equal(alice.Props.Snapshot(), bob.Props.Snapshot());

        // The ground is dug: the tree does not grow back over the hole, however long nobody is near (the twin of the regrowth test).
        MoveAway(rig, tree, alice, bob);
        rig.StepMany(1500);
        Assert.True(rig.Runtime.State.HarvestedProps.Contains(_planet, tree.Id));
        Assert.True(alice.Props.IsHarvested(tree.Id));
        await DisconnectAllAsync(rig);
        await rig.StopAsync();
    }

    [Fact]
    public async Task HarvestYield_InHotbar_ConservationHolds()
    {
        using var dir = new TempDir();
        await using var rig = new PropsRig(dir.Path);
        var (tree, alice, _) = await StartWithTwoAsync(rig);
        var nearby = new List<PropInstance>();
        rig.NewField().CollectInSphere(_planet, PropsRig.Pole, 300f, nearby);
        var second = nearby.First(p => p.Kind == PropKindV1.Pine && p.Id != tree.Id);
        var woodBefore = rig.Count(alice.Player, SurvivalContentFactory.WoodKey);

        await rig.HarvestAsync(alice, tree);
        rig.PlaceBeside(alice, second, 3f);
        await rig.HarvestAsync(alice, second);
        rig.StepMany(5);

        // Two pines of 3 wood: six more wood on the server, the same on the client, and every container of the client equals the server's.
        Assert.Equal(woodBefore + 6, rig.Count(alice.Player, SurvivalContentFactory.WoodKey));
        foreach (var container in rig.Runtime.State.Containers.All.Where(c => c.Owner == alice.Player))
        {
            Assert.Equal(container.Slots.ToArray(), alice.Containers.SlotsOf(container.Id));
            Assert.Equal(container.Version, alice.Containers.VersionOf(container.Id));
        }

        Assert.Equal(2, rig.Runtime.State.HarvestedProps.Count);
        Assert.NotEqual(0UL, StateHasher.Hash(rig.Runtime.State));
        await DisconnectAllAsync(rig);
        await rig.StopAsync();
    }

    private static long RawJournalRows(string directory)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = PropsRig.WorldPath(directory),
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(op_id), 0) FROM journal";
        return (long)command.ExecuteScalar()!;
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
