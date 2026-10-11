using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Tests.Markers;

public sealed class MarkerOperationTests
{
    internal static MarkerSetOp Set(ulong id, PlayerId? owner = null, string name = "Base", Vector3? position = null, ulong planet = 1) =>
        new(new MarkerId(id), owner, new PlanetId(planet), position ?? new Vector3(10, 4000, -5), name, MarkerIcon.Base, MarkerColor.Red);

    [Fact]
    public void MarkerSetOp_NewId_BumpsAllocator()
    {
        var f = new SimFixture();

        f.Sim.ApplyServerOperation(Set(5));
        Assert.Equal(6ul, f.State.MarkerIds.NextValue);
        f.Sim.ApplyServerOperation(Set(5, name: "Replaced"));
        Assert.Equal(6ul, f.State.MarkerIds.NextValue);
        f.Sim.ApplyServerOperation(Set(9));
        Assert.Equal(10ul, f.State.MarkerIds.NextValue);
        Assert.Equal(2, f.State.Markers.Count);
        Assert.Equal([5ul, 9ul], f.State.Markers.All.Select(m => m.Id.Value));
    }

    [Fact]
    public void MarkerSetOp_ExistingId_ReplacesButKeepsOwner()
    {
        var f = new SimFixture();
        var owner = f.Join();
        f.Sim.ApplyServerOperation(Set(1, owner));

        f.Sim.ApplyServerOperation(Set(1, owner, "Renamed"));

        Assert.True(f.State.Markers.TryGet(new MarkerId(1), out var marker));
        Assert.Equal("Renamed", marker.Name);
        Assert.Equal(1, f.State.Markers.CountPrivate(owner));
        Assert.Equal(2ul, f.State.MarkerIds.NextValue);
    }

    [Fact]
    public void MarkerSetOp_IdBelowNextAndUnknown_PreconditionFails()
    {
        var f = new SimFixture();
        f.Sim.ApplyServerOperation(Set(5));
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(5)));

        AssertRejected(f, Set(3));
        AssertRejected(f, Set(5));
        AssertRejected(f, Set(0));
        AssertRejected(f, Set(MarkerIdAllocator.MaxValue + 1));
    }

    [Fact]
    public void MarkerSetOp_OwnerChange_PreconditionFails()
    {
        var f = new SimFixture();
        var a = f.Join(1);
        var b = f.Join(2);
        f.Sim.ApplyServerOperation(Set(1, a));

        AssertRejected(f, Set(1, b));
        AssertRejected(f, Set(1, null));
    }

    [Fact]
    public void MarkerSetOp_OverLimit_PreconditionFails()
    {
        var f = new SimFixture();
        var a = f.Join(1);
        var b = f.Join(2);
        for (ulong i = 1; i <= 256; i++)
        {
            f.Sim.ApplyServerOperation(Set(i, a));
        }

        for (ulong i = 1; i <= 256; i++)
        {
            f.Sim.ApplyServerOperation(Set(1000 + i, null));
        }

        AssertRejected(f, Set(2000, a));
        AssertRejected(f, Set(2000, null));
        f.Sim.ApplyServerOperation(Set(2000, b));
        f.Sim.ApplyServerOperation(Set(1, a, "Edit at the limit"));
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(1)));
        f.Sim.ApplyServerOperation(Set(2001, a));
        Assert.Equal(256, f.State.Markers.CountPrivate(a));
        Assert.Equal(256, f.State.Markers.CountShared);
    }

    [Fact]
    public void MarkerSetOp_InvalidNameOrPosition_PreconditionFails()
    {
        var f = new SimFixture();

        AssertRejected(f, Set(1, name: ""));
        AssertRejected(f, Set(1, name: "   "));
        AssertRejected(f, Set(1, name: "tab\there"));
        AssertRejected(f, Set(1, name: new string('a', 33)));
        AssertRejected(f, Set(1, position: new Vector3(float.NaN, 0, 0)));
        AssertRejected(f, Set(1, position: new Vector3(100_001, 0, 0)));
        AssertRejected(f, Set(1, planet: 99));
        AssertRejected(f, Set(1) with { Icon = (MarkerIcon)8 });
        AssertRejected(f, Set(1) with { Color = (MarkerColor)8 });
        f.Sim.ApplyServerOperation(Set(1, name: new string('a', 32)));
        f.Sim.ApplyServerOperation(Set(2, name: "Zażółć"));
    }

    [Fact]
    public void MarkerRemoveOp_Unknown_PreconditionFails()
    {
        var f = new SimFixture();
        f.Sim.ApplyServerOperation(Set(1));

        AssertRejected(f, new MarkerRemoveOp(new MarkerId(2)));
        f.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(1)));
        AssertRejected(f, new MarkerRemoveOp(new MarkerId(1)));
        Assert.Equal(2ul, f.State.MarkerIds.NextValue);
    }

    private static void AssertRejected(SimFixture f, Operation op)
    {
        var hash = StateHasher.Hash(f.State);
        var journal = f.Journal.Entries.Count;
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(op));
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journal, f.Journal.Entries.Count);
    }
}
