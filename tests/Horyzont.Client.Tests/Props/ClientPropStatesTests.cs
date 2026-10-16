using System.Numerics;

using Horyzont.Client.Props;
using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Testing;

namespace Horyzont.Client.Tests.Props;

/// <summary>The harvested props a remote client knows (K6-31 D17, D18): whole regions replaced by message 45, forgotten when far away.</summary>
public sealed class ClientPropStatesTests
{
    private static readonly PlanetId _planet = WorldFactory.HomePlanet;
    private static readonly PropId _a = PropId.Create(0, 17, 33, 0);
    private static readonly PropId _b = PropId.Create(0, 18, 34, 1);
    private static readonly PropId _otherRegion = PropId.Create(2, 100, 100, 0);

    private static PropStates Message(params PropRegionState[] regions) => new(_planet, regions);

    private static PropRegionState Region(PropId region, params PropId[] harvested) =>
        new(region.Region, [.. harvested.Select(p => new HarvestedPropEntry(p, new Tick(40)))]);

    [Fact]
    public void States_ReplaceRegion()
    {
        var states = new ClientPropStates();
        Assert.Equal(_a.Region, _b.Region);

        states.Apply(Message(Region(_a, _a, _b)));
        Assert.True(states.IsHarvested(_planet, _a));
        Assert.True(states.IsHarvested(_planet, _b));
        Assert.False(states.IsHarvested(_planet, _otherRegion));

        // The next state of the region is the whole truth: what is not listed regrew.
        states.Apply(Message(Region(_a, _b)));
        Assert.False(states.IsHarvested(_planet, _a));
        Assert.True(states.IsHarvested(_planet, _b));
        Assert.False(states.IsHarvested(new PlanetId(2), _b));
    }

    [Fact]
    public void EmptyRegion_Clears()
    {
        var states = new ClientPropStates();
        states.Apply(Message(Region(_a, _a), Region(_otherRegion, _otherRegion)));
        Assert.Equal(2, states.RegionCount);

        states.Apply(Message(Region(_a)));

        Assert.False(states.IsHarvested(_planet, _a));
        Assert.True(states.IsHarvested(_planet, _otherRegion));
        Assert.Equal(1, states.RegionCount);
    }

    [Fact]
    public void RegionBeyond768m_Forgotten()
    {
        var planet = WorldFactory.HomePlanetParams;
        Assert.True(CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cells));
        var states = new ClientPropStates();
        states.Apply(Message(Region(_a, _a)));
        var centre = CubeSphereGrid.DirectionOfPoint(_a.Face, (_a.Region.RegionU * 8) + 4, (_a.Region.RegionV * 8) + 4, cells);
        var tangent = Vector3.Normalize(Vector3.Cross(centre, Vector3.UnitY));

        // Within the drop distance the region stays, however often the client looks; beyond it, it is gone.
        states.Forget(planet, centre * planet.RadiusMeters);
        states.Forget(planet, Vector3.Normalize(centre + (tangent * (700f / planet.RadiusMeters))) * planet.RadiusMeters);
        Assert.True(states.IsHarvested(_planet, _a));

        states.Forget(planet, Vector3.Normalize(centre + (tangent * (900f / planet.RadiusMeters))) * planet.RadiusMeters);
        Assert.False(states.IsHarvested(_planet, _a));
        Assert.Equal(0, states.RegionCount);
        Assert.Equal(768f, ClientPropStates.DropMeters);
    }

    [Fact]
    public void RegionChanged_Raised()
    {
        var states = new ClientPropStates();
        var raised = new List<(PlanetId Planet, PropRegionKey Region)>();
        states.RegionChanged += (planet, region) => raised.Add((planet, region));

        states.Apply(Message(Region(_a, _a), Region(_otherRegion)));
        states.Apply(Message(Region(_a, _a, _b)));

        Assert.Equal([(_planet, _a.Region), (_planet, _otherRegion.Region), (_planet, _a.Region)], raised);
    }

    [Fact]
    public void Disconnect_Clears()
    {
        var states = new ClientPropStates();
        var replication = new ClientReplication(_ => { }, constructs: null, propStates: states);
        var raised = 0;
        states.RegionChanged += (_, _) => raised++;

        replication.OnMessage(Message(Region(_a, _a), Region(_otherRegion, _otherRegion)));
        Assert.True(states.IsHarvested(_planet, _a));
        Assert.Equal(2, raised);

        replication.OnDisconnected(DisconnectCode.ServerShutdown);

        Assert.Equal(0, states.RegionCount);
        Assert.False(states.IsHarvested(_planet, _a));
        Assert.False(states.IsHarvested(_planet, _otherRegion));
        Assert.Equal(4, raised);
    }
}
