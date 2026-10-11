using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Tests.Markers;

public sealed class MarkerStoreTests
{
    [Fact]
    public void MarkerStore_UpsertRemove_CountsAndOrder()
    {
        var f = new SimFixture();
        var a = f.Join(1);
        var b = f.Join(2);

        foreach (var (id, owner) in new (ulong, PlayerId?)[] { (1, a), (3, null), (5, b), (7, null), (9, a) })
        {
            f.Sim.ApplyServerOperation(MarkerOperationTests.Set(id, owner));
        }

        Assert.Equal([1ul, 3ul, 5ul, 7ul, 9ul], f.State.Markers.All.Select(m => m.Id.Value));
        Assert.Equal(2, f.State.Markers.CountPrivate(a));
        Assert.Equal(1, f.State.Markers.CountPrivate(b));
        Assert.Equal(2, f.State.Markers.CountShared);

        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(9)));
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(3)));
        Assert.Equal(1, f.State.Markers.CountPrivate(a));
        Assert.Equal(1, f.State.Markers.CountShared);
        Assert.False(f.State.Markers.TryGet(new MarkerId(9), out _));
        Assert.Equal(10ul, f.State.MarkerIds.NextValue);
    }

    [Fact]
    public void MarkerStore_Mutations_MarkTheChangeTracker()
    {
        var f = new SimFixture();
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1));
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(2));
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(2)));

        var checkpoint = f.State.CaptureCheckpoint();

        Assert.Equal([1ul], checkpoint.UpsertedMarkers.Select(m => m.Id.Value));
        Assert.Equal([2ul], checkpoint.RemovedMarkers.Select(m => m.Value));
        Assert.Equal(3ul, checkpoint.NextMarkerId);
    }
}
