using System.Numerics;

using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>ITerrainService.ApplyEdit receives the block versions the applier bumped (K2-09).</summary>
public sealed class OperationApplierTerrainTests
{
    private static TerrainEditOp Edit(Vector3 center, float radius = 1.5f) =>
        new(SimFixture.Planet, TerrainShape.Sphere, center, radius, TerrainEditMode.Remove, 0);

    [Fact]
    public void ApplyEdit_ReceivesExactlyTheBumpedVersions_SortedAfterBump()
    {
        var f = new SimFixture();
        var seenAtCall = new List<(TerrainBlockVersion Passed, uint InState)>();
        f.Terrain.OnApplyEdit = (_, versions) =>
        {
            foreach (var version in versions)
            {
                seenAtCall.Add((version, f.State.TerrainVersions.Get(version.Key)));
            }
        };

        var applied = f.Sim.ApplyServerOperation(Edit(new Vector3(15.5f, 3999.5f, -0.5f)));

        var passed = Assert.Single(f.Terrain.AppliedVersions);
        Assert.Equal(applied.TerrainVersions, passed);
        var expectedKeys = new List<TerrainBlockKey>();
        TerrainMath.AffectedBlocks(SimFixture.Planet, new Vector3(15.5f, 3999.5f, -0.5f), 1.5f, 1f, TerrainRules.EditMarginVoxels, expectedKeys);
        Assert.Equal(expectedKeys, passed.Select(v => v.Key));
        Assert.Equal(passed.Select(v => v.Key).Order(), passed.Select(v => v.Key));
        Assert.All(passed, v => Assert.Equal(1u, v.Version));
        Assert.All(seenAtCall, s => Assert.Equal(s.Passed.Version, s.InState));
    }

    [Fact]
    public void ApplyEdit_OverlappingEdits_ReceiveIncrementedVersions()
    {
        var f = new SimFixture();
        f.Sim.ApplyServerOperation(Edit(new Vector3(0.5f, 3999.5f, 0.5f)));

        f.Sim.ApplyServerOperation(Edit(new Vector3(0.5f, 3999.5f, 0.5f)));

        Assert.Equal(2, f.Terrain.AppliedVersions.Count);
        Assert.All(f.Terrain.AppliedVersions[1], v => Assert.Equal(2u, v.Version));
        Assert.Equal(f.State.TerrainVersions.All.Count(), f.Terrain.AppliedVersions[1].Count);
    }

    [Fact]
    public void FakeApplyEdit_VersionsDifferFromAffectedBlocks_Throws()
    {
        var terrain = new FakeTerrainService();
        var op = Edit(new Vector3(0.5f, 3999.5f, 0.5f));
        var keys = new List<TerrainBlockKey>();
        TerrainMath.AffectedBlocks(op.Planet, op.Center, op.Radius, 1f, TerrainRules.EditMarginVoxels, keys);
        var versions = keys.Select(k => new TerrainBlockVersion(k, 1)).ToList();

        Assert.Throws<InvalidOperationException>(() => terrain.ApplyEdit(op, versions.Skip(1).ToList()));
        Assert.Throws<InvalidOperationException>(() => terrain.ApplyEdit(op, [.. versions, new TerrainBlockVersion(keys[^1] with { Z = keys[^1].Z + 1 }, 1)]));
        Assert.Throws<InvalidOperationException>(() => terrain.ApplyEdit(op, versions.AsEnumerable().Reverse().ToList()));
        Assert.Empty(terrain.Blocks);

        terrain.ApplyEdit(op, versions);
        Assert.Equal(keys.Count, terrain.Blocks.Count);
    }
}
