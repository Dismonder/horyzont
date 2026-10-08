using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.Simulation;

public sealed class IntentResolutionTests
{
    [Fact]
    public void Dig_CenterIsRaycastHit()
    {
        var f = new SimFixture();
        var p = f.Join();
        var dig = f.Dig(p, f.Ground(3, 1));
        Assert.True(f.Terrain.Raycast(SimFixture.Planet, dig.RayOrigin, dig.RayDirection, 8f, out var expected));

        var result = f.Sim.Submit(p, dig);

        var op = Assert.IsType<TerrainEditOp>(result.Applied!.Operation);
        Assert.Equal(expected.Position, op.Center);
        Assert.Equal(TerrainShape.Sphere, op.Shape);
        Assert.Equal(TerrainEditMode.Remove, op.Mode);
        Assert.Equal(0.5f, op.Radius);
        Assert.Single(f.Terrain.AppliedEdits);
        Assert.NotEmpty(result.Applied.TerrainVersions);
    }

    [Fact]
    public void PlaceOnTerrain_CreatesConstructStandingOnHitWithUpAlongNormal()
    {
        var f = new SimFixture();
        var p = f.Join();
        var place = f.PlaceOnTerrain(p, f.Ground(3, 2));
        var target = (TerrainTarget)place.Target;
        Assert.True(f.Terrain.Raycast(SimFixture.Planet, target.RayOrigin, target.RayDirection, 8f, out var hit));
        var expectedId = f.State.EntityIds.PeekNext();

        var result = f.Sim.Submit(p, place);

        var op = Assert.IsType<ConstructCreateOp>(result.Applied!.Operation);
        Assert.Equal(expectedId, op.Construct);
        Assert.Equal(FrameRef.Of(SimFixture.Planet), op.Parent);
        Assert.Equal(new CellPos(0, 0, 0), op.FirstCell);
        Assert.Equal(BlockState.Create(f.Stone, 0), op.Block);
        AssertClose(TerrainMath.UpAt(hit.Position), Vector3.Transform(Vector3.UnitY, op.Rotation), 1e-4f);
        AssertClose(hit.Position, op.Origin + Vector3.Transform(new Vector3(0.5f, 0f, 0.5f), op.Rotation), 1e-3f);
        AssertClose(-Vector3.UnitZ, Vector3.Transform(-Vector3.UnitZ, op.Rotation), 1e-3f);
        Assert.Equal([new ConstructChange(op.Construct, 1, false)], result.Applied.ConstructChanges);
    }

    [Fact]
    public void PlaceOnTerrain_LookForwardAlongUp_FallsBackToRayDirection()
    {
        var f = new SimFixture();
        var p = f.Join();
        var place = f.PlaceOnTerrain(p, f.Ground(3));
        var target = (TerrainTarget)place.Target;
        place = place with { Target = target with { LookForward = -Vector3.UnitY } };

        var result = f.Sim.Submit(p, place);

        var op = Assert.IsType<ConstructCreateOp>(result.Applied!.Operation);
        var forward = Vector3.Transform(-Vector3.UnitZ, op.Rotation);
        Assert.True(forward.X > 0.99f, $"forward {forward}");
    }

    [Fact]
    public void PlaceOnFace_SetsNeighbourCell()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);

        var result = f.Sim.Submit(p, new PlaceBlockIntent(new ConstructFaceTarget(c, new CellPos(0, 0, 0), Face.PosY), f.Stone, 3));

        var op = Assert.IsType<BlockSetOp>(result.Applied!.Operation);
        Assert.Equal(new BlockSetOp(c, new CellPos(0, 1, 0), BlockState.Create(f.Stone, 3)), op);
        Assert.Equal([new ConstructChange(c, 2, false)], result.Applied.ConstructChanges);
    }

    [Fact]
    public void Remove_ProducesBlockClear()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        f.SetBlock(c, new CellPos(0, 1, 0));

        var result = f.Sim.Submit(p, new RemoveBlockIntent(c, new CellPos(0, 1, 0)));

        Assert.Equal(new BlockClearOp(c, new CellPos(0, 1, 0)), result.Applied!.Operation);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, actual {actual}");
}
