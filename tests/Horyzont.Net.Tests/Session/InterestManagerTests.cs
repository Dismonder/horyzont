using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

public sealed class InterestManagerTests
{
    private static readonly PlayerId _viewer = new(1);
    private static readonly FrameRef _planet = FrameRef.Of(new PlanetId(1));
    private static readonly FrameRef _otherPlanet = FrameRef.Of(new PlanetId(2));
    private static readonly EntityId _far = new(10);
    private static readonly EntityId _creature = new(11);

    [Fact]
    public void InterestManager_Hysteresis_EntersAt500LeavesAbove550()
    {
        var interest = new InterestManager(50f);
        var entered = new List<EntityId>();
        var left = new List<EntityId>();

        Update(interest, 499, 119, _planet);
        interest.GetChanges(_viewer, entered, left);
        Assert.True(interest.IsVisible(_viewer, _far));
        Assert.True(interest.IsVisible(_viewer, _creature));
        Assert.Equal([_far, _creature], entered.OrderBy(e => e.Value));
        Assert.Empty(left);

        Update(interest, 520, 200, _planet);
        interest.GetChanges(_viewer, entered, left);
        Assert.True(interest.IsVisible(_viewer, _far));
        Assert.False(interest.IsVisible(_viewer, _creature));
        Assert.Empty(entered);
        Assert.Equal([_creature], left);

        Update(interest, 549, 200, _planet);
        Assert.True(interest.IsVisible(_viewer, _far));

        Update(interest, 551, 200, _planet);
        interest.GetChanges(_viewer, entered, left);
        Assert.False(interest.IsVisible(_viewer, _far));
        Assert.Equal([_far], left);

        Update(interest, 520, 200, _planet);
        Assert.False(interest.IsVisible(_viewer, _far)); // 520 m does not re-enter: entering needs 500 m

        Update(interest, 100, 100, _planet);
        interest.GetChanges(_viewer, entered, left);
        Assert.True(interest.IsVisible(_viewer, _far));
        Assert.Equal([_far, _creature], entered.OrderBy(e => e.Value));
        Update(interest, 100, 100, _otherPlanet);
        interest.GetChanges(_viewer, entered, left);
        Assert.False(interest.IsVisible(_viewer, _far));
        Assert.False(interest.IsVisible(_viewer, _creature));
        Assert.Equal([_far, _creature], left.OrderBy(e => e.Value));
    }

    [Fact]
    public void InterestManager_RadiusOfSubject_ExtendsRange()
    {
        var interest = new InterestManager(50f);
        var big = new InterestSubject(new EntityId(5), _planet, new Vector3(0, 0, 700), 250f, 500f);

        interest.Update([big], [new InterestViewer(_viewer, _planet, Vector3.Zero)]);

        Assert.True(interest.IsVisible(_viewer, big.Entity));
    }

    [Fact]
    public void InterestManager_SystemRegionLongRange_SeesShips()
    {
        var interest = new InterestManager(50f);
        var system = FrameRef.Of(new SystemId(1));
        var ship = new InterestSubject(new EntityId(5), system, new Vector3(9_000, 3_000, 0), 20f, 10_000f);

        interest.Update([ship], [new InterestViewer(_viewer, system, Vector3.Zero)]);

        Assert.True(interest.IsVisible(_viewer, ship.Entity));
    }

    [Fact]
    public void InterestManager_SubjectRemoved_Leaves()
    {
        var interest = new InterestManager(50f);
        var entered = new List<EntityId>();
        var left = new List<EntityId>();
        Update(interest, 10, 10, _planet);
        interest.GetChanges(_viewer, entered, left);

        interest.Update([], [new InterestViewer(_viewer, _planet, Vector3.Zero)]);
        interest.GetChanges(_viewer, entered, left);

        Assert.Equal([_far, _creature], left.OrderBy(e => e.Value));
    }

    [Fact]
    public void InterestManager_SteadyState_DoesNotAllocate()
    {
        var interest = new InterestManager(50f);
        var subjects = new InterestSubject[2000];
        var viewers = new InterestViewer[8];
        var random = new Random(5);
        for (var i = 0; i < subjects.Length; i++)
        {
            var position = new Vector3(random.Next(-3000, 3000), random.Next(-3000, 3000), random.Next(-200, 200));
            subjects[i] = new InterestSubject(new EntityId((ulong)i + 1), _planet, position, 1f, i % 3 == 0 ? 120f : 500f);
        }

        for (var i = 0; i < viewers.Length; i++)
        {
            viewers[i] = new InterestViewer(new PlayerId((ulong)i + 1), _planet, new Vector3(i * 300, 0, 0));
        }

        var entered = new List<EntityId>(4096);
        var left = new List<EntityId>(4096);
        for (var round = 0; round < 20; round++)
        {
            Tick(interest, subjects, viewers, entered, left, round);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 20; round < 120; round++)
        {
            Tick(interest, subjects, viewers, entered, left, round);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Tick(
        InterestManager interest, InterestSubject[] subjects, InterestViewer[] viewers, List<EntityId> entered, List<EntityId> left, int round)
    {
        // Subjects wander back and forth so that cells and visibility change, but within a bounded set.
        var offset = new Vector3((round % 10) * 20, 0, 0);
        for (var i = 0; i < subjects.Length; i++)
        {
            subjects[i] = subjects[i] with { Position = subjects[i].Position + ((round % 20) < 10 ? offset : -offset) * 0.1f };
        }

        interest.Update(subjects, viewers);
        foreach (var viewer in viewers)
        {
            interest.GetChanges(viewer.Player, entered, left);
        }
    }

    private static void Update(InterestManager interest, float farDistance, float creatureDistance, FrameRef subjectRegion)
    {
        InterestSubject[] subjects =
        [
            new(_far, subjectRegion, new Vector3(farDistance, 0, 0), 0f, 500f),
            new(_creature, subjectRegion, new Vector3(0, creatureDistance, 0), 0f, 120f),
        ];
        interest.Update(subjects, [new InterestViewer(_viewer, _planet, Vector3.Zero)]);
    }
}
