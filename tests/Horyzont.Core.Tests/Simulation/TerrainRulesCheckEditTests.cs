using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>The pure part of the terrain edit precondition, shared by OperationApplier and the terrain replay (gate decision D2).</summary>
public sealed class TerrainRulesCheckEditTests
{
    private static readonly PlanetParams _planet = WorldFactory.HomePlanetParams;

    private static float Bound => _planet.RadiusMeters + _planet.AtmosphereHeightMeters + TerrainRules.MaxEditRadiusMeters;

    private static TerrainEditOp Op(
        Vector3? center = null, float radius = 1f, TerrainShape shape = TerrainShape.Sphere, TerrainEditMode mode = TerrainEditMode.Remove) =>
        new(_planet.Id, shape, center ?? new Vector3(0, 4000, 0), radius, mode, mode == TerrainEditMode.Add ? (byte)1 : (byte)0);

    public static TheoryData<TerrainEditOp, string?> Cases => new()
    {
        { Op(), null },
        { Op(mode: TerrainEditMode.Add), null },
        { Op(shape: (TerrainShape)0), "unknown shape or mode" },
        { Op(shape: (TerrainShape)2), "unknown shape or mode" },
        { Op(mode: (TerrainEditMode)0), "unknown shape or mode" },
        { Op(mode: (TerrainEditMode)3), "unknown shape or mode" },
        { Op(center: new Vector3(float.NaN, 4000, 0)), "invalid centre or radius" },
        { Op(center: new Vector3(0, float.PositiveInfinity, 0)), "invalid centre or radius" },
        { Op(center: new Vector3(0, 4000, float.NegativeInfinity)), "invalid centre or radius" },
        { Op(radius: float.NaN), "invalid centre or radius" },
        { Op(radius: float.PositiveInfinity), "invalid centre or radius" },
        { Op(radius: 0f), "invalid centre or radius" },
        { Op(radius: -1f), "invalid centre or radius" },
        { Op(radius: float.Epsilon), null },
        { Op(radius: TerrainRules.MaxEditRadiusMeters), null },
        { Op(radius: MathF.BitIncrement(TerrainRules.MaxEditRadiusMeters)), "radius too large" },
        { Op(center: new Vector3(0, Bound, 0)), null },
        { Op(center: new Vector3(0, MathF.BitIncrement(Bound), 0)), "centre out of range" },
        { Op(center: new Vector3(-MathF.BitIncrement(Bound), 0, 0)), "centre out of range" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void CheckEdit_EachCondition_ReturnsTheApplierMessage(TerrainEditOp op, string? expected)
    {
        var valid = TerrainRules.CheckEdit(op, _planet, out var failure);

        Assert.Equal(expected is null, valid);
        Assert.Equal(expected, failure);
    }

    [Fact]
    public void CheckEdit_ShapeCheckedBeforeRadius()
    {
        TerrainRules.CheckEdit(Op(shape: (TerrainShape)9, radius: float.NaN), _planet, out var failure);

        Assert.Equal("unknown shape or mode", failure);
    }

    [Fact]
    public void CheckEdit_RadiusCheckedBeforeCentreRange()
    {
        TerrainRules.CheckEdit(Op(center: new Vector3(0, 1e9f, 0), radius: 100f), _planet, out var failure);

        Assert.Equal("radius too large", failure);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Replay_SameOperation_RejectedWithTheSameMessage(TerrainEditOp op, string? expected)
    {
        var f = new SimFixture();
        var entry = new JournalEntry(new OpId(1), new Tick(0), null, op);

        if (expected is null)
        {
            f.Sim.Replay(entry);
            Assert.Single(f.Terrain.AppliedEdits);
            return;
        }

        var exception = Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(entry));
        Assert.EndsWith(": " + expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_UnknownPlanet_ReportedBeforeShape()
    {
        var f = new SimFixture();
        var op = new TerrainEditOp(new PlanetId(99), (TerrainShape)9, new Vector3(0, 4000, 0), 1f, TerrainEditMode.Remove, 0);

        var exception = Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(new JournalEntry(new OpId(1), new Tick(0), null, op)));

        Assert.EndsWith(": unknown planet", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_InvalidMaterial_ReportedAfterTheGeometry()
    {
        var f = new SimFixture();
        var op = new TerrainEditOp(SimFixture.Planet, TerrainShape.Sphere, new Vector3(0, 4000, 0), 1f, TerrainEditMode.Remove, 1);

        var exception = Assert.Throws<ReplayFailedException>(() => f.Sim.Replay(new JournalEntry(new OpId(1), new Tick(0), null, op)));

        Assert.EndsWith(": invalid material", exception.Message, StringComparison.Ordinal);
    }
}
