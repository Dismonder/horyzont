using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.World.Constructs;

public sealed class ConstructRaycastTests
{
    private static readonly FrameRef _frame = FrameRef.Of(WorldFactory.HomePlanet);

    private static Construct Add(WorldState state, ulong id, Vector3 origin, Quaternion rotation, params CellPos[] cells)
    {
        var construct = state.Constructs.Create(new EntityId(id), _frame, origin, rotation);
        foreach (var cell in cells)
        {
            construct.SetBlock(cell, BlockState.Create(1, 0));
        }

        return construct;
    }

    [Fact]
    public void Raycast_AlongX_HitsFirstCellThroughNegX()
    {
        var state = WorldFactory.CreateDefault();
        Add(state, 1, Vector3.Zero, Quaternion.Identity, new CellPos(0, 0, 0), new CellPos(1, 0, 0));

        var found = ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(-5f, 0.5f, 0.5f), Vector3.UnitX, 20f, out var hit);

        Assert.True(found);
        Assert.Equal(new CellPos(0, 0, 0), hit.Cell);
        Assert.Equal(Face.NegX, hit.Face);
        Assert.Equal(5f, hit.Distance, 4);
        Assert.Equal(new EntityId(1), hit.Construct);
    }

    [Fact]
    public void Raycast_FromAbove_HitsTopFace()
    {
        var state = WorldFactory.CreateDefault();
        Add(state, 1, Vector3.Zero, Quaternion.Identity, new CellPos(0, 0, 0), new CellPos(0, 1, 0), new CellPos(3, 0, 0));

        var found = ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(0.5f, 10f, 0.5f), -Vector3.UnitY, 20f, out var hit);

        Assert.True(found);
        Assert.Equal(new CellPos(0, 1, 0), hit.Cell);
        Assert.Equal(Face.PosY, hit.Face);
        Assert.Equal(8f, hit.Distance, 4);
    }

    [Fact]
    public void Raycast_SkipsEmptyCellsInsideBounds()
    {
        var state = WorldFactory.CreateDefault();
        Add(state, 1, Vector3.Zero, Quaternion.Identity, new CellPos(0, 0, 0), new CellPos(5, 0, 0));

        var found = ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(1.5f, 0.5f, 0.5f), Vector3.UnitX, 20f, out var hit);

        Assert.True(found);
        Assert.Equal(new CellPos(5, 0, 0), hit.Cell);
        Assert.Equal(Face.NegX, hit.Face);
        Assert.Equal(3.5f, hit.Distance, 4);
    }

    [Fact]
    public void Raycast_RayMissingConstruct_ReturnsFalse()
    {
        var state = WorldFactory.CreateDefault();
        Add(state, 1, Vector3.Zero, Quaternion.Identity, new CellPos(0, 0, 0));

        Assert.False(ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(-5f, 3f, 0.5f), Vector3.UnitX, 20f, out _));
        Assert.False(ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(-5f, 0.5f, 0.5f), -Vector3.UnitX, 20f, out _));
        Assert.False(ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(-5f, 0.5f, 0.5f), Vector3.UnitX, 4f, out _));
    }

    [Fact]
    public void Raycast_TwoConstructs_ReturnsNearest()
    {
        var state = WorldFactory.CreateDefault();
        Add(state, 1, new Vector3(10f, 0f, 0f), Quaternion.Identity, new CellPos(0, 0, 0));
        Add(state, 2, new Vector3(4f, 0f, 0f), Quaternion.Identity, new CellPos(0, 0, 0));

        var found = ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(0f, 0.5f, 0.5f), Vector3.UnitX, 50f, out var hit);

        Assert.True(found);
        Assert.Equal(new EntityId(2), hit.Construct);
        Assert.Equal(4f, hit.Distance, 4);
    }

    [Fact]
    public void Raycast_RotatedConstruct_HitsCellInItsSpace()
    {
        var state = WorldFactory.CreateDefault();
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var construct = Add(state, 1, new Vector3(0f, 0f, 0f), rotation, new CellPos(2, 0, 0));
        var center = construct.CellCenterToParent(new CellPos(2, 0, 0));
        var origin = center + new Vector3(0f, 6f, 0f);

        var found = ConstructRaycast.Raycast(state.Constructs, _frame, origin, -Vector3.UnitY, 20f, out var hit);

        Assert.True(found);
        Assert.Equal(new CellPos(2, 0, 0), hit.Cell);
        Assert.Equal(Face.PosY, hit.Face);
        Assert.Equal(5.5f, hit.Distance, 3);
    }

    [Fact]
    public void Raycast_HitsNestedConstructThroughParentChain()
    {
        var state = WorldFactory.CreateDefault();
        var outer = Add(state, 1, new Vector3(10f, 0f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        var inner = state.Constructs.Create(new EntityId(2), FrameRef.Of(outer.Id), new Vector3(0f, 0f, 2f), Quaternion.Identity);
        inner.SetBlock(new CellPos(0, 0, 0), BlockState.Create(1, 0));

        // Outer rotated +90 degrees about Y maps its local +Z to planet +X: the inner cell spans x in [12, 13], z in [-1, 0].
        var found = ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(0f, 0.5f, -0.5f), Vector3.UnitX, 30f, out var hit);

        Assert.True(found);
        Assert.Equal(new EntityId(2), hit.Construct);
        Assert.Equal(new CellPos(0, 0, 0), hit.Cell);
        Assert.Equal(12f, hit.Distance, 3);
    }

    [Fact]
    public void Raycast_NestedConstructUnderAnotherPlanet_IsIgnored()
    {
        var state = WorldFactory.CreateWith(WorldFactory.HomePlanetParams, WorldFactory.HomePlanetParams with { Id = new PlanetId(2) });
        var outer = state.Constructs.Create(new EntityId(1), FrameRef.Of(new PlanetId(2)), Vector3.Zero, Quaternion.Identity);
        var inner = state.Constructs.Create(new EntityId(2), FrameRef.Of(outer.Id), Vector3.Zero, Quaternion.Identity);
        inner.SetBlock(new CellPos(0, 0, 0), BlockState.Create(1, 0));

        var found = ConstructRaycast.Raycast(state.Constructs, _frame, new Vector3(-5f, 0.5f, 0.5f), Vector3.UnitX, 20f, out _);

        Assert.False(found);
    }
}
