using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>Terrain edit operation semantics are constants and a journal entry cannot make Replay do unbounded work.</summary>
public sealed class TerrainEditLimitsTests
{
    private static JournalEntry Edit(float radius, Vector3 center) =>
        new(new OpId(1), new Tick(0), null, new TerrainEditOp(SimFixture.Planet, TerrainShape.Sphere, center, radius, TerrainEditMode.Remove, 0));

    [Fact]
    public void Replay_HugeRadius_RejectedWithoutAllocatingOrLoading()
    {
        var f = new SimFixture();
        var entry = Edit(1e9f, new Vector3(0, 4000, 0));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var exception = Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(entry));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(OperationKind.TerrainEdit, exception.Kind);
        Assert.True(allocated < 64 * 1024, $"allocated {allocated} bytes");
        Assert.Empty(f.Terrain.BlockingLoads);
        Assert.Empty(f.Terrain.AppliedEdits);
        Assert.False(f.Sim.IsFaulted);
        Assert.Equal(OpId.None, f.State.LastOpId);
    }

    [Theory]
    [InlineData(1e9f, 0f, 0f)]
    [InlineData(0f, 1e9f, 0f)]
    [InlineData(0f, 0f, -1e9f)]
    public void Replay_CentreFarOutsidePlanet_Rejected(float x, float y, float z)
    {
        var f = new SimFixture();

        Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(Edit(1f, new Vector3(x, y, z))));

        Assert.Empty(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Replay_MaxRadiusAtTopOfAtmosphere_Accepted()
    {
        var f = new SimFixture();
        f.State.Planets.TryGet(SimFixture.Planet, out var planet);
        var top = new Vector3(0, planet!.RadiusMeters + planet.AtmosphereHeightMeters, 0);

        f.Sim.Replay(Edit(TerrainRules.MaxEditRadiusMeters, top));

        Assert.Single(f.Terrain.AppliedEdits);
    }

    [Fact]
    public void Replay_RadiusAboveMax_Rejected()
    {
        var f = new SimFixture();

        Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(Edit(MathF.BitIncrement(TerrainRules.MaxEditRadiusMeters), new Vector3(0, 4000, 0))));
    }

    [Fact]
    public void Submit_LargestDefaultToolRadius_ProducesAnOperationThatReplays()
    {
        var live = new SimFixture();
        var p = live.Join();
        var result = live.Sim.Submit(p, live.Dig(p, live.Ground(2), live.Rules.MaxToolRadius));
        Assert.True(result.IsAccepted, $"{result.Reason}");
        Assert.True(live.Rules.MaxToolRadius <= TerrainRules.MaxEditRadiusMeters);

        var fresh = new SimFixture();
        foreach (var entry in live.Journal.Entries)
        {
            fresh.Sim.Replay(entry);
        }

        Assert.Equal(live.Terrain.AppliedEdits, fresh.Terrain.AppliedEdits);
    }

    [Fact]
    public void Submit_ToolRadiusAboveProtectedBound_RejectedEvenWhenRulesAllowIt()
    {
        var f = new SimFixture(new GameRules { MaxToolRadius = 1000f });
        var p = f.Join();

        var result = f.Sim.Submit(p, f.Dig(p, f.Ground(2), TerrainRules.MaxEditRadiusMeters + 1f));

        Assert.Equal(RejectReason.InvalidParameters, result.Reason);
    }

    [Fact]
    public void Replay_TickGoingBackwards_RejectedWithoutFault()
    {
        var f = new SimFixture();
        f.Ticks(5);
        var entry = new JournalEntry(new OpId(1), new Tick(2), null, Edit(1f, new Vector3(0, 4000, 0)).Operation);

        var exception = Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(entry));

        Assert.Equal(new OpId(1), exception.OpId);
        Assert.False(f.Sim.IsFaulted);
        Assert.Equal(new Tick(5), f.State.CurrentTick);
    }

    [Fact]
    public void Replay_TerrainLoadFails_FaultsSimulation()
    {
        var f = new SimFixture();
        f.Terrain.ThrowOnLoad = new IOException("disk");

        Assert.Throws<IOException>(() => f.Sim.Replay(Edit(1f, new Vector3(0, 4000, 0))));

        Assert.True(f.Sim.IsFaulted);
    }
}
