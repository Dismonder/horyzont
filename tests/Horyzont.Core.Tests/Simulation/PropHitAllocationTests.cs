using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>The refusal path of <see cref="PropHitIntent"/> does not allocate (ZASADY section 6; K6-31 D24).</summary>
public sealed class PropHitAllocationTests
{
    private const int Repeats = 2000;

    [Fact]
    public void RejectedHit_DoesNotAllocate()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        var unknown = new PropId(tree.Id.Value | (2UL << 16));
        f.StandBeside(tree, 3f);

        // The valid twin of every refusal below: the same hit is accepted from here, so the refusals are the validator's and not a missing intent.
        var accepted = f.Hit(tree.Id);
        Assert.True(accepted.IsAccepted, $"{accepted.Reason}");

        f.StandBeside(tree, 14f);
        var refused = new (PropHitIntent Intent, RejectReason Reason)[]
        {
            (new PropHitIntent(PropWorldFixture.Planet, tree.Id, 0), RejectReason.OutOfReach),
            (new PropHitIntent(PropWorldFixture.Planet, unknown, 0), RejectReason.InvalidParameters),
            (new PropHitIntent(PropWorldFixture.Planet, tree.Id, 40), RejectReason.InvalidParameters),
            (new PropHitIntent(new PlanetId(2), tree.Id, 0), RejectReason.WrongPlanet),
        };
        foreach (var (intent, reason) in refused)
        {
            Assert.Equal(reason, f.Submit(intent).Reason);
        }

        // Warm-up so the JIT and the first lookups of the prop cache do not count, then the measured run.
        Run(f, refused);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(f, refused);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static void Run(PropWorldFixture f, (PropHitIntent Intent, RejectReason Reason)[] refused)
    {
        for (var i = 0; i < Repeats; i++)
        {
            foreach (var (intent, _) in refused)
            {
                f.Submit(intent);
            }
        }
    }
}
