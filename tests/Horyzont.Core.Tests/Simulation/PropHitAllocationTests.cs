using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Tests.Props;

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
