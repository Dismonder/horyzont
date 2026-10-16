using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Survival;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// <see cref="OxygenCoverage.IsCovered"/> (K6-34 D12): a point is covered when a generator that runs on the same planet has its cell centre within 10 m (inclusive) of the point. The point is the origin of the
/// player as in the shelter (the feet plus the local up times <see cref="HazardRules.ShelterOriginHeightMeters"/>, review B1, P3-9). "Runs" comes from the pure
/// <see cref="OxygenGeneratorProcess.IsRunning"/> at the tick, never from the last settlement. The generator stands in the cell (5,1,0) of the construct at (0, 4000, 0), so its centre is (5.5, 4001.5, 0.5) and the
/// points along +X from it are exact in float. Every test of a refusal first checks the twin that is covered.
/// </summary>
public sealed class OxygenCoverageTests
{
    /// <summary>The centre of the cell of the generator in the frame of the planet.</summary>
    private static readonly Vector3 _centre = new(5.5f, 4001.5f, 0.5f);

    private static bool Covered(HazardWorld w, Vector3 point, PlanetId? planet = null, HazardRules? rules = null) =>
        OxygenCoverage.IsCovered(w.State, planet ?? WorldFactory.HomePlanet, point, HazardWorld.Fuel, HazardTestContent.TicksPerCell, w.Now, rules ?? HazardRules.Default);

    /// <summary>A generator that started burning now with <paramref name="queued"/> more cells behind the burning one.</summary>
    private static HazardWorld Running(int queued = 0, bool secondPlanet = false)
    {
        var w = new HazardWorld(secondPlanet: secondPlanet);
        var input = w.SetUpGenerator(HazardWorld.GeneratorCell, w.Now);
        if (queued > 0)
        {
            w.FeedGenerator(input, 0, queued);
        }

        return w;
    }

    [Fact]
    public void RunningWithin10m_Covered()
    {
        var w = Running();

        Assert.True(Covered(w, _centre));
        Assert.True(Covered(w, _centre + new Vector3(3f, 0f, 4f)));
        Assert.True(Covered(w, _centre + new Vector3(0f, 0f, -9f)));
    }

    [Fact]
    public void At10m_Covered()
    {
        var w = Running();
        Assert.True(Covered(w, _centre + new Vector3(9.99f, 0f, 0f)));

        // The range is inclusive (the same rule as StationLevels and the rest range): exactly 10.0 m is covered.
        Assert.True(Covered(w, _centre + new Vector3(10f, 0f, 0f)));
    }

    [Fact]
    public void At10_01m_NotCovered()
    {
        var w = Running();
        Assert.True(Covered(w, _centre + new Vector3(10f, 0f, 0f)));

        Assert.False(Covered(w, _centre + new Vector3(10.01f, 0f, 0f)));
    }

    [Fact]
    public void At10_5m_NotCovered()
    {
        var w = Running();
        Assert.True(Covered(w, _centre + new Vector3(9.5f, 0f, 0f)));

        Assert.False(Covered(w, _centre + new Vector3(10.5f, 0f, 0f)));
        Assert.False(Covered(w, _centre + new Vector3(0f, 0f, 20f)));
    }

    [Fact]
    public void RangeFromTheRules_Used()
    {
        var w = Running();
        var point = _centre + new Vector3(10.5f, 0f, 0f);
        Assert.False(Covered(w, point));

        Assert.True(Covered(w, point, rules: HazardRules.Default with { GeneratorRangeMeters = 11f }));
    }

    [Fact]
    public void IdleOrNoCells_NotCovered()
    {
        var running = Running();
        Assert.True(Covered(running, _centre));

        // Idle with cells in the input: it has not been started (the settlement after the move starts it), so it protects nothing yet.
        var idle = new HazardWorld();
        var input = idle.SetUpGenerator(HazardWorld.GeneratorCell);
        idle.FeedGenerator(input, 0, 2);
        Assert.False(Covered(idle, _centre));

        // Idle and empty.
        Assert.False(Covered(new HazardWorld().Also(w => w.SetUpGenerator(HazardWorld.GeneratorCell)), _centre));

        // No generator at all.
        Assert.False(Covered(new HazardWorld(), _centre));
    }

    [Fact]
    public void AfterLastCellEnds_NotCoveredWithoutSettlement()
    {
        // Started now with one cell queued: the cell that burns and the queued one are paid for, 2 periods of protection.
        var w = Running(queued: 1);
        w.AdvanceTicks((int)(2 * HazardTestContent.TicksPerCell) - 1);
        Assert.True(Covered(w, _centre));

        // One tick later the last cell has burned out. Nobody settled the generator (no operation was applied since the start), and it protects no more.
        w.AdvanceTicks(1);
        Assert.False(Covered(w, _centre));
        Assert.Equal(1, w.GeneratorAt(HazardWorld.GeneratorCell) is { } state ? OxygenGeneratorProcess.CellsIn(w.Blocks.Container(state.Input).Slots, HazardWorld.Fuel) : -1);
    }

    [Fact]
    public void OtherPlanet_NotCovered()
    {
        var w = Running(secondPlanet: true);

        // The control: the generator is on the home planet. The same coordinates in the frame of the second planet are not near it.
        Assert.True(Covered(w, _centre, WorldFactory.HomePlanet));
        Assert.False(Covered(w, _centre, WorldFactory.SecondPlanet));
    }
}

internal static class HazardWorldExtensions
{
    /// <summary>Runs <paramref name="action"/> on the world and returns it (a setup in one expression).</summary>
    public static HazardWorld Also(this HazardWorld world, Action<HazardWorld> action)
    {
        action(world);
        return world;
    }
}
