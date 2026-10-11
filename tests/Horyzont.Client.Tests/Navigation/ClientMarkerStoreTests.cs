using System.Numerics;

using Horyzont.Client.Markers;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Markers;

namespace Horyzont.Client.Tests.Navigation;

public sealed class ClientMarkerStoreTests
{
    private static MarkerRecord Marker(ulong id, PlayerId? owner, string name) =>
        new(new MarkerId(id), owner, new PlanetId(1), new Vector3(id, 4000, 0), name, MarkerIcon.Base, MarkerColor.Blue);

    private static OperationApplied Applied(ulong op, Operation operation) => new(new OpId(op), new Tick(op), operation, []);

    private static MarkerSetOp Set(MarkerRecord m) => new(m.Id, m.Owner, m.Planet, m.Position, m.Name, m.Icon, m.Color);

    [Fact]
    public void ClientMarkerStore_StateThenOps_MatchesServer()
    {
        var store = new ClientMarkerStore();
        var changes = 0;
        store.Changed += () => changes++;
        var me = new PlayerId(7);

        store.OnMessage(new MarkersState([Marker(1, null, "Shared"), Marker(2, me, "Mine")]));
        // Operation numbers have gaps: the server sends a client only the operations it may see.
        store.OnMessage(Applied(10, Set(Marker(3, null, "New shared"))));
        store.OnMessage(Applied(40, Set(Marker(2, me, "Mine renamed"))));
        store.OnMessage(Applied(41, new MarkerRemoveOp(new MarkerId(1))));
        store.OnMessage(Applied(42, new MarkerRemoveOp(new MarkerId(99))));

        Assert.Equal([2ul, 3ul], store.All.Select(m => m.Id.Value));
        Assert.True(store.TryGet(new MarkerId(2), out var mine));
        Assert.Equal("Mine renamed", mine.Name);
        Assert.Equal(me, mine.Owner);
        Assert.Equal(4, changes);

        store.OnMessage(new MarkersState([Marker(5, null, "Fresh state replaces everything")]));
        Assert.Equal([5ul], store.All.Select(m => m.Id.Value));
        store.OnDisconnected(DisconnectCode.Normal);
        Assert.Equal(0, store.Count);
    }
}
