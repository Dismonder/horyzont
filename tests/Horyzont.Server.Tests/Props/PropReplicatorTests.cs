using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Server.Props;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Props;

/// <summary>
/// Replication of the harvested props by region (K6-31 D17): a player gets the full state of every region within 512 m of the body (empty
/// ones too), the full state of a region again after every operation that changed it, and nothing for regions that it left. The tests drive
/// the tick by hand; the replicator scans every 30th tick and at once after a player is ready.
/// </summary>
public sealed class PropReplicatorTests
{
    private static readonly PlayerId _alice = PropServerFixture.Player(1);
    private static readonly PlayerId _bob = PropServerFixture.Player(2);

    private sealed class Rig
    {
        public Rig(PropServerFixture f)
        {
            F = f;
            Replicator = new PropReplicator(
                () => f.Sim,
                (player, message) =>
                {
                    Sent.Add((player, message));
                    HarvestedAtSend.Add(f.State.HarvestedProps.Count);
                },
                Local.Contains,
                NullLogger<PropReplicator>.Instance);
        }

        public PropServerFixture F { get; }

        public PropReplicator Replicator { get; }

        public HashSet<PlayerId> Local { get; } = [];

        public List<(PlayerId Player, Message Message)> Sent { get; } = [];

        /// <summary>The number of harvested props when each message was sent (parallel to <see cref="Sent"/>).</summary>
        public List<int> HarvestedAtSend { get; } = [];

        public IEnumerable<PropStates> For(PlayerId player) => Sent.Where(s => s.Player == player).Select(s => (PropStates)s.Message);

        public List<PropRegionState> RegionsFor(PlayerId player) => [.. For(player).SelectMany(m => m.Regions)];

        /// <summary>Ready, one tick: the first state of the player.</summary>
        public void Join(PlayerId player)
        {
            Replicator.OnPlayerReady(player);
            F.Run(1, Replicator.Tick);
        }

        public void Run(int ticks) => F.Run(ticks, Replicator.Tick);
    }

    private static List<PropRegionKey> RegionsAround(PropServerFixture f, PlayerId player, float meters)
    {
        Assert.True(f.State.Players.TryGet(player, out var state));
        Assert.True(f.State.TryToPlanetFrame(state.Frame, state.Position, out _, out var feet));
        Assert.True(f.State.Planets.TryGet(PropServerFixture.Planet, out var planet));
        var regions = new List<PropRegionKey>();
        new PropInterest().CollectRegions(planet, feet, meters, regions);
        return regions;
    }

    [Fact]
    public void EnterRegion_SendsStateEvenEmpty()
    {
        var f = new PropServerFixture(2);
        f.Place(_bob, PropServerFixture.Antipode);
        var rig = new Rig(f);
        f.Run(7, rig.Replicator.Tick);
        Assert.Empty(rig.Sent);

        // Ready at tick 7, not a multiple of 30: the first state does not wait for the next scan.
        rig.Join(_alice);

        var regions = rig.RegionsFor(_alice);
        Assert.NotEmpty(regions);
        Assert.Equal(RegionsAround(f, _alice, 512f), regions.Select(r => r.Region));
        Assert.All(regions, r => Assert.Empty(r.Props));
        Assert.All(rig.For(_alice), m => Assert.Equal(PropServerFixture.Planet, m.Planet));
        Assert.DoesNotContain(rig.Sent, s => s.Player == _bob);

        // Nothing changed: the next scans send nothing.
        var sent = rig.Sent.Count;
        rig.Run(90);
        Assert.Equal(sent, rig.Sent.Count);
    }

    [Fact]
    public void Harvest_SendsRegionToInterestedOnly()
    {
        var f = new PropServerFixture(2);
        f.Place(_bob, PropServerFixture.Antipode);
        var rig = new Rig(f);
        rig.Join(_alice);
        rig.Join(_bob);
        var tree = f.FindProp(PropKindV1.Pine);
        rig.Sent.Clear();

        f.Run(3, rig.Replicator.Tick);
        var harvested = f.Harvest(tree.Id);

        var message = Assert.IsType<PropStates>(Assert.Single(rig.Sent, s => s.Player == _alice).Message);
        var region = Assert.Single(message.Regions);
        Assert.Equal(tree.Id.Region, region.Region);
        Assert.Equal(new HarvestedPropEntry(tree.Id, f.State.CurrentTick), Assert.Single(region.Props));
        Assert.Single(rig.Sent);
        Assert.NotEqual(OpId.None, harvested.Id);
    }

    [Fact]
    public void Harvest_AfterOperationApplied()
    {
        var f = new PropServerFixture();
        var rig = new Rig(f);
        rig.Join(_alice);
        var first = f.FindProp(PropKindV1.Pine);
        var second = f.PropsAround(200f).First(p => p.Id != first.Id && p.Id.Region == first.Id.Region);
        rig.Sent.Clear();
        rig.HarvestedAtSend.Clear();

        f.Harvest(first.Id);
        f.Harvest(second.Id);

        // One message per operation, each sent when the operation was already applied, each with the whole region.
        Assert.Equal([1, 2], rig.HarvestedAtSend);
        var states = rig.For(_alice).ToList();
        Assert.Equal(2, states.Count);
        Assert.Equal([first.Id], Assert.Single(states[0].Regions).Props.Select(e => e.Prop));
        Assert.Equal(
            new[] { first.Id, second.Id }.OrderBy(i => i.Value),
            Assert.Single(states[1].Regions).Props.Select(e => e.Prop));
    }

    [Fact]
    public void Harvest_DigCompound_SendsEachChangedRegionOnce()
    {
        var f = new PropServerFixture();
        var rig = new Rig(f);
        rig.Join(_alice);
        var props = f.PropsAround(150f);
        var a = props[0];
        var b = props.First(p => p.Id.Region != a.Id.Region);
        rig.Sent.Clear();

        f.Sim.ApplyServerOperation(new CompoundOp(
        [
            new PropHarvestOp(PropServerFixture.Planet, a.Id, f.State.CurrentTick),
            new PropHarvestOp(PropServerFixture.Planet, b.Id, f.State.CurrentTick),
        ]));

        // One operation: one message, the two regions in ascending order, each with its prop.
        var message = Assert.IsType<PropStates>(Assert.Single(rig.Sent).Message);
        Assert.Equal(2, message.Regions.Count);
        Assert.True(message.Regions[0].Region.Value < message.Regions[1].Region.Value);
        Assert.Equal(new[] { a.Id.Region, b.Id.Region }.OrderBy(r => r.Value), message.Regions.Select(r => r.Region));
    }

    [Fact]
    public void LeaveAndReenter_SendsFreshState()
    {
        var f = new PropServerFixture();
        var rig = new Rig(f);
        rig.Join(_alice);
        var tree = f.FindProp(PropKindV1.Pine);

        // Away: the regions of the pole are dropped, and what happens to them is not sent.
        f.Place(_alice, PropServerFixture.Antipode);
        rig.Run(30);
        rig.Sent.Clear();
        f.Harvest(tree.Id);
        rig.Run(60);
        Assert.DoesNotContain(rig.RegionsFor(_alice), r => r.Region == tree.Id.Region);

        // Back: the state of the region is sent again, with the harvested tree.
        f.Place(_alice, PropServerFixture.Pole);
        rig.Run(30);

        var fresh = Assert.Single(rig.RegionsFor(_alice), r => r.Region == tree.Id.Region);
        Assert.Equal(tree.Id, Assert.Single(fresh.Props).Prop);
    }

    [Fact]
    public void RegrowWhileAway_ReenterSeesRegrown()
    {
        var f = new PropServerFixture();
        var rig = new Rig(f);
        rig.Join(_alice);
        var tree = f.FindProp(PropKindV1.Pine);
        f.Harvest(tree.Id);
        Assert.Contains(rig.RegionsFor(_alice), r => r.Region == tree.Id.Region && r.Props.Count == 1);

        f.Place(_alice, PropServerFixture.Antipode);
        rig.Run(30);
        rig.Sent.Clear();
        f.Regrow(tree.Id);
        rig.Run(60);
        Assert.DoesNotContain(rig.RegionsFor(_alice), r => r.Region == tree.Id.Region);

        f.Place(_alice, PropServerFixture.Pole);
        rig.Run(30);

        var fresh = Assert.Single(rig.RegionsFor(_alice), r => r.Region == tree.Id.Region);
        Assert.Empty(fresh.Props);
    }

    [Fact]
    public void Batching_Max64RegionsPerMessage()
    {
        // A range of 1500 m reaches about 1700 regions: more than the 256 regions per player and scan that the replicator sends.
        var rules = new GameRules { PropSyncEnterMeters = 1500f, PropSyncLeaveMeters = 1600f };
        var f = new PropServerFixture(rules: rules);
        var rig = new Rig(f);
        var expected = RegionsAround(f, _alice, 1500f);
        Assert.True(expected.Count > 1000);

        rig.Join(_alice);

        Assert.Equal(256, rig.RegionsFor(_alice).Count);
        Assert.Equal(4, rig.For(_alice).Count());
        Assert.All(rig.For(_alice), m => Assert.InRange(m.Regions.Count, 1, ProtocolConstants.MaxPropRegionsPerMessage));
        Assert.All(rig.For(_alice), m => Assert.InRange(m.Regions.Sum(r => r.Props.Count), 0, ProtocolConstants.MaxPropEntriesPerMessage));

        // The rest follows with the next scans, each region once.
        rig.Run(30 * 8);
        var all = rig.RegionsFor(_alice).Select(r => r.Region).ToList();
        Assert.Equal(expected.Count, all.Count);
        Assert.Equal(expected.Select(r => r.Value).Order(), all.Select(r => r.Value).Order());
    }

    [Fact]
    public void LocalHostPlayer_NoMessages()
    {
        var f = new PropServerFixture(2);
        var rig = new Rig(f);
        rig.Local.Add(_alice);
        var tree = f.FindProp(PropKindV1.Pine);

        rig.Join(_alice);
        rig.Join(_bob);
        f.Harvest(tree.Id);
        rig.Run(60);

        // The twin: the remote player at the same place gets the states, the local one shares the world of the server and gets none.
        Assert.DoesNotContain(rig.Sent, s => s.Player == _alice);
        Assert.Contains(rig.Sent, s => s.Player == _bob);
        Assert.Contains(rig.RegionsFor(_bob), r => r.Region == tree.Id.Region && r.Props.Count == 1);
    }

    [Fact]
    public void Disconnect_InterestDropped()
    {
        var f = new PropServerFixture();
        var rig = new Rig(f);
        rig.Join(_alice);
        var tree = f.FindProp(PropKindV1.Pine);
        rig.Sent.Clear();

        rig.Replicator.OnPlayerLeaving(_alice, DisconnectCode.Normal);
        f.Harvest(tree.Id);
        rig.Run(60);
        Assert.Empty(rig.Sent);

        // Ready again (a new session): the whole state is sent, with the harvested tree.
        rig.Join(_alice);
        Assert.Equal(RegionsAround(f, _alice, 512f), rig.RegionsFor(_alice).Select(r => r.Region));
        Assert.Equal(tree.Id, Assert.Single(rig.RegionsFor(_alice), r => r.Region == tree.Id.Region).Props.Single().Prop);
    }

    [Fact]
    public void Idle_DoesNotAllocate()
    {
        var f = new PropServerFixture(2);
        f.Place(_bob, PropServerFixture.Antipode);
        var rig = new Rig(f);
        rig.Join(_alice);
        rig.Join(_bob);
        f.Harvest(f.FindProp(PropKindV1.Pine).Id);
        rig.Run(120);
        var sent = rig.Sent.Count;
        Assert.True(sent > 100, "the first states must have been sent before the measured run.");

        // Warm-up is over: 300 ticks (ten scans of two players) in which nothing changes allocate nothing.
        var allocated = 0L;
        for (var i = 0; i < 300; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            rig.Replicator.Tick();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            f.Sim.AdvanceTick();
        }

        Assert.Equal(0, allocated);
        Assert.Equal(sent, rig.Sent.Count);
    }
}
