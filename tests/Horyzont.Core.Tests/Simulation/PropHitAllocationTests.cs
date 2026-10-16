using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Props;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>The refusal path of <see cref="PropHitIntent"/> does not allocate (ZASADY section 6; K6-31 D24).</summary>
public sealed class PropHitAllocationTests
{
    private const int Repeats = 2000;
    private const ushort Hand = 9;

    [Fact]
    public void RejectedHit_DoesNotAllocate()
    {
        var f = new PropWorldFixture(players: 2);
        var tree = f.FindProp(PropKindV1.Pine);
        var unknown = new PropId(tree.Id.Value | (2UL << 16));

        // The valid twin of every refusal below: the same hits are accepted from here, so the refusals are the validator's and not a missing intent.
        // Player 1 is the far one (reach, slot, prop and planet refusals), player 2 stands beside the tree with the bare hand (tool tier).
        f.StandBeside(tree, 3f, 1);
        f.StandBeside(tree, 3f, 2);
        var accepted = f.Hit(tree.Id, 0, 1);
        Assert.True(accepted.IsAccepted, $"{accepted.Reason}");
        accepted = f.Hit(tree.Id, 0, 2);
        Assert.True(accepted.IsAccepted, $"{accepted.Reason}");

        f.StandBeside(tree, 14f, 1);
        var refused = new (PropHitIntent Intent, RejectReason Reason, int Player)[]
        {
            (new PropHitIntent(PropWorldFixture.Planet, tree.Id, 0), RejectReason.OutOfReach, 1),
            (new PropHitIntent(PropWorldFixture.Planet, unknown, 0), RejectReason.InvalidParameters, 1),
            (new PropHitIntent(PropWorldFixture.Planet, tree.Id, 40), RejectReason.InvalidParameters, 1),
            (new PropHitIntent(new PlanetId(2), tree.Id, 0), RejectReason.UnknownPlanet, 1),
            (new PropHitIntent(PropWorldFixture.Planet, tree.Id, Hand), RejectReason.ToolTierTooLow, 2),
        };
        foreach (var (intent, reason, player) in refused)
        {
            f.Sim.Ticks(15);
            Assert.Equal(reason, f.Submit(intent, player).Reason);
        }

        // Warm-up so the JIT and the first lookups of the prop cache do not count, then the measured run. A refusal does not use a token of
        // the bucket, so the same refusals can repeat without waiting; each call is checked against its own reason.
        Run(f, refused);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(f, refused);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RepeatedBandRefusal_SamplesSurfaceOnce_AndDoesNotAllocate()
    {
        var f = new PropWorldFixture();
        var rock = f.FindProp(PropKindV1.Rock);
        f.StandBeside(rock, 3f);

        // The valid twin: the hit is accepted over the normal surface (a rock needs no tier). Another prop is used for the refusals, because
        // the simulation remembers the one that was hit; its candidate exists but the surface is moved above every height band.
        var refusedRock = f.FindProp(PropKindV1.Boulder);
        Assert.True(f.Hit(rock.Id).IsAccepted);
        f.Terrain.GeneratedSurfaceRadius = WorldFactory.HomePlanetParams.RadiusMeters + 1000f;
        var refused = new PropHitIntent(PropWorldFixture.Planet, refusedRock.Id, 0);

        // Within reach of the refused prop, or the reach check would answer first and nothing would be sampled (the test would pass empty).
        f.StandBeside(refusedRock, 3f);
        var sampled = f.Terrain.GeneratedSurfaceCalls;
        Assert.Equal(RejectReason.InvalidParameters, f.Submit(refused).Reason);
        var calls = f.Terrain.GeneratedSurfaceCalls;
        Assert.Equal(1, calls - sampled);
        Assert.Equal(RejectReason.InvalidParameters, f.Submit(refused).Reason);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Repeats; i++)
        {
            if (f.Submit(refused).Reason != RejectReason.InvalidParameters)
            {
                throw new InvalidOperationException("Expected InvalidParameters.");
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(calls, f.Terrain.GeneratedSurfaceCalls);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FarUnknownHit_OutOfReach_DoesNotAllocateNorSample()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);

        // The simulation never saw this tree (the field of the fixture is another cache), and it lies far away: refused from the hash alone,
        // and the refusal is not remembered, so every repeat takes the same path.
        f.StandBeside(tree, 100f);
        var intent = new PropHitIntent(PropWorldFixture.Planet, tree.Id, 0);
        var sampled = f.Terrain.GeneratedSurfaceCalls;
        Assert.Equal(RejectReason.OutOfReach, f.Submit(intent).Reason);
        Assert.Equal(RejectReason.OutOfReach, f.Submit(intent).Reason);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Repeats; i++)
        {
            if (f.Submit(intent).Reason != RejectReason.OutOfReach)
            {
                throw new InvalidOperationException("Expected OutOfReach.");
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(sampled, f.Terrain.GeneratedSurfaceCalls);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ExhaustedProbeBudget_RateLimited_DoesNotAllocateNorSample()
    {
        var f = new PropWorldFixture(rules: new GameRules { ReachMeters = 60f });
        f.Sim.MovePlayer(PropWorldFixture.Player(), new Vector3(0, PropWorldFixture.Surface - 3f, 0));
        var found = new List<PropInstance>();
        f.Field.CollectInSphere(PropWorldFixture.Planet, f.Sim.Eye(PropWorldFixture.Player()), 50f, found);
        var burst = f.Sim.Sim.Rules.PropProbeBurst;
        Assert.True(found.Count > burst + 5, $"Only {found.Count} props around the player.");

        // The first hits pay for their lookups (the terrain hides the trunks); the budget is spent before the ones that follow, and
        // no tick passes, so those wait: no sampling, no allocation.
        for (var i = 0; i < burst; i++)
        {
            Assert.Equal(RejectReason.NoLineOfSight, f.Submit(new PropHitIntent(PropWorldFixture.Planet, found[i].Id, 0)).Reason);
        }

        var waiting = found.Skip(burst).Take(5).Select(p => new PropHitIntent(PropWorldFixture.Planet, p.Id, 0)).ToArray();
        var sampled = f.Terrain.GeneratedSurfaceCalls;
        foreach (var intent in waiting)
        {
            Assert.Equal(RejectReason.RateLimited, f.Submit(intent).Reason);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Repeats; i++)
        {
            foreach (var intent in waiting)
            {
                if (f.Submit(intent).Reason != RejectReason.RateLimited)
                {
                    throw new InvalidOperationException("Expected RateLimited.");
                }
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(sampled, f.Terrain.GeneratedSurfaceCalls);
        Assert.Equal(0, allocated);
    }

    private static void Run(PropWorldFixture f, (PropHitIntent Intent, RejectReason Reason, int Player)[] refused)
    {
        for (var i = 0; i < Repeats; i++)
        {
            foreach (var (intent, reason, player) in refused)
            {
                if (f.Submit(intent, player).Reason != reason)
                {
                    throw new InvalidOperationException($"Expected {reason}.");
                }
            }
        }
    }
}
