using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Markers;

namespace Horyzont.Core.Tests.Checkpoints;

public sealed class MarkerCheckpointTests
{
    [Fact]
    public void Checkpoint_Markers_UpsertRemoveAndRequeue()
    {
        var f = new SimFixture();
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1));
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(2));
        var first = f.State.CaptureCheckpoint();
        Assert.Equal([1ul, 2ul], first.UpsertedMarkers.Select(m => m.Id.Value));
        f.State.CompleteCheckpoint(first);

        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(1)));
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(2, name: "Edited"));
        var second = f.State.CaptureCheckpoint();
        Assert.Equal([2ul], second.UpsertedMarkers.Select(m => m.Id.Value));
        Assert.Equal([1ul], second.RemovedMarkers.Select(m => m.Value));

        f.State.RequeueAfterFailedCheckpoint(second);
        var third = f.State.CaptureCheckpoint();
        Assert.Equal(second.UpsertedMarkers, third.UpsertedMarkers);
        Assert.Equal(second.RemovedMarkers, third.RemovedMarkers);
        f.State.CompleteCheckpoint(third);
        Assert.Empty(f.State.CaptureCheckpoint().UpsertedMarkers);
    }
}
