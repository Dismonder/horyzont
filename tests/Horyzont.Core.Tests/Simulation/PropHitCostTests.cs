using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// The cost of refused <see cref="PropHitIntent"/>s (K6-31 review fix). A refused hit does not use a hit token, so a client can name many
/// valid identifiers; the server must not sample the generator for every one of them. Identifiers whose anchor cannot be in reach are
/// refused from the hash alone, and the rest share a budget of generator lookups that is paid before the lookup.
/// </summary>
public sealed class PropHitCostTests
{
    private const int Wanted = 40;

    /// <summary>A reach wide enough for dozens of props, so many identifiers pass the cheap distance check (the density of props is low).</summary>
    private static readonly GameRules _wideReach = new() { ReachMeters = 60f };

    /// <summary>Distinct props within a sphere around a point, as a hit at one tick would name them.</summary>
    private static List<PropId> IdsAround(PropWorldFixture f, Vector3 center, float radius)
    {
        var found = new List<PropInstance>();
        f.Field.CollectInSphere(PropWorldFixture.Planet, center, radius, found);
        Assert.True(found.Count >= Wanted, $"Only {found.Count} props around {center}; widen the sphere.");
        return found.Take(Wanted).Select(p => p.Id).ToList();
    }

    [Fact]
    public void Hit_AnchorsFarFromPlayer_RefusedWithoutSamplingTheGenerator()
    {
        var f = new PropWorldFixture();
        var ids = IdsAround(f, new Vector3(PropWorldFixture.Surface, 0, 0), 150f);
        var before = f.Terrain.GeneratedSurfaceCalls;

        foreach (var id in ids)
        {
            // All in one tick: none of them uses a token, so the hit bucket never answers. Existence is not known without sampling.
            Assert.Equal(RejectReason.InvalidParameters, f.Submit(new PropHitIntent(PropWorldFixture.Planet, id, 0)).Reason);
        }

        Assert.Equal(before, f.Terrain.GeneratedSurfaceCalls);
    }

    [Fact]
    public void Hit_ManyNearbyIdentifiers_SamplesAreLimitedByTheProbeBudget()
    {
        var f = new PropWorldFixture(rules: _wideReach);
        f.Sim.MovePlayer(PropWorldFixture.Player(), new Vector3(0, PropWorldFixture.Surface - 3f, 0));
        var ids = IdsAround(f, f.Sim.Eye(PropWorldFixture.Player()), 50f);
        var burst = f.Sim.Sim.Rules.PropProbeBurst;
        Assert.True(ids.Count > burst);
        var before = f.Terrain.GeneratedSurfaceCalls;

        var reasons = ids.Select(id => f.Submit(new PropHitIntent(PropWorldFixture.Planet, id, 0)).Reason).ToList();

        // Refused hits (the terrain hides the trunk) pay a probe each; once the budget is spent the rest wait.
        Assert.Equal(burst, f.Terrain.GeneratedSurfaceCalls - before);
        Assert.Equal(burst, reasons.Count(r => r == RejectReason.NoLineOfSight));
        Assert.Equal(ids.Count - burst, reasons.Count(r => r == RejectReason.RateLimited));
    }

    [Fact]
    public void Hit_ProbeBudget_RefillsWithTicks()
    {
        var f = new PropWorldFixture(rules: _wideReach);
        f.Sim.MovePlayer(PropWorldFixture.Player(), new Vector3(0, PropWorldFixture.Surface - 3f, 0));
        var ids = IdsAround(f, f.Sim.Eye(PropWorldFixture.Player()), 50f);
        foreach (var id in ids)
        {
            f.Submit(new PropHitIntent(PropWorldFixture.Planet, id, 0));
        }

        var last = ids[^1];
        Assert.Equal(RejectReason.RateLimited, f.Submit(new PropHitIntent(PropWorldFixture.Planet, last, 0)).Reason);

        f.Sim.Ticks(60);

        Assert.Equal(RejectReason.NoLineOfSight, f.Submit(new PropHitIntent(PropWorldFixture.Planet, last, 0)).Reason);
    }

    [Fact]
    public void Hit_SameIdentifierAgain_UsesTheAnchorCacheNotTheBudget()
    {
        var f = new PropWorldFixture(rules: _wideReach);
        f.Sim.MovePlayer(PropWorldFixture.Player(), new Vector3(0, PropWorldFixture.Surface - 3f, 0));
        var id = IdsAround(f, f.Sim.Eye(PropWorldFixture.Player()), 50f)[0];
        var before = f.Terrain.GeneratedSurfaceCalls;

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(RejectReason.NoLineOfSight, f.Submit(new PropHitIntent(PropWorldFixture.Planet, id, 0)).Reason);
        }

        Assert.Equal(1, f.Terrain.GeneratedSurfaceCalls - before);
    }
}
