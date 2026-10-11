using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Markers;
using Horyzont.Core.World.Markers;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Hashing;

public sealed class MarkerHashTests
{
    [Fact]
    public void StateHasher_MarkerChange_ChangesHash()
    {
        var f = new SimFixture();
        var empty = StateHasher.Hash(f.State);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1));
        var one = StateHasher.Hash(f.State);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, name: "Other"));
        var renamed = StateHasher.Hash(f.State);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, name: "Other") with { Icon = MarkerIcon.Poi });

        Assert.Equal(4, new[] { empty, one, renamed, StateHasher.Hash(f.State) }.Distinct().Count());
    }

    [Fact]
    public void StateHasher_NextMarkerIdChange_ChangesHash()
    {
        var f = new SimFixture();
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1));
        var withMarker = StateHasher.Hash(f.State);
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(1)));
        var removed = StateHasher.Hash(f.State);

        // No marker is left, but the counter moved, so the world differs from one that never had a marker.
        Assert.NotEqual(withMarker, removed);
        Assert.NotEqual(StateHasher.Hash(new SimFixture().State), removed);
    }

    [Fact]
    public void StateHasher_NoMarkersEver_HashUnchanged()
    {
        // Without markers the section is absent, so hashes of worlds from before K5-27 stay valid
        // (the frozen DeterminismTests golden hashes prove it against the pre-K5-27 value).
        var a = StateHasher.Hash(WorldFactory.CreateDefault());
        var b = StateHasher.Hash(WorldFactory.CreateDefault());
        Assert.Equal(a, b);
        Assert.Equal(1ul, WorldFactory.CreateDefault().MarkerIds.NextValue);
    }

    [Fact]
    public void StateHasher_SameOperations_SameMarkerHash()
    {
        var f = new SimFixture();
        var player = f.Join();
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(2, null));
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(4, player));
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(4)));

        var other = new SimFixture();
        var otherPlayer = other.Join();
        other.Sim.ApplyServerOperation(MarkerOperationTests.Set(2, null));
        other.Sim.ApplyServerOperation(MarkerOperationTests.Set(4, otherPlayer));
        other.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(4)));

        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(other.State));
    }
}
