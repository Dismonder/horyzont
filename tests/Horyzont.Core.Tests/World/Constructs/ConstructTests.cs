using System.Numerics;

using FsCheck.Xunit;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.World.Constructs;

public sealed class ConstructTests
{
    private static Construct NewConstruct(Quaternion rotation = default, Vector3 origin = default)
    {
        var state = WorldFactory.CreateDefault();
        var rot = rotation == default ? Quaternion.Identity : rotation;
        return state.Constructs.Create(new EntityId(1), FrameRef.Of(WorldFactory.HomePlanet), origin, rot);
    }

    [Fact]
    public void SetBlock_SetAndClear_TracksVersionCountAndChunks()
    {
        var construct = NewConstruct();
        var stone = BlockState.Create(1, 0);

        construct.SetBlock(new CellPos(0, 0, 0), stone);
        construct.SetBlock(new CellPos(-1, 0, 0), stone);

        Assert.Equal(2u, construct.Version);
        Assert.Equal(2, construct.BlockCount);
        Assert.Equal(2, construct.ChunkCount);
        Assert.Equal(stone, construct.GetBlock(new CellPos(-1, 0, 0)));

        var previous = construct.SetBlock(new CellPos(-1, 0, 0), BlockState.Empty);

        Assert.Equal(stone, previous);
        Assert.Equal(3u, construct.Version);
        Assert.Equal(1, construct.BlockCount);
        Assert.Equal(1, construct.ChunkCount);
        Assert.True(construct.GetBlock(new CellPos(-1, 0, 0)).IsEmpty);
    }

    [Fact]
    public void GetBlock_OutOfBounds_IsEmpty()
    {
        var construct = NewConstruct();

        Assert.True(construct.GetBlock(new CellPos(5000, 0, 0)).IsEmpty);
    }

    [Property(MaxTest = 300)]
    public void ParentToCell_OfCellCenter_ReturnsCell(int x, int y, int z, float qx, float qy, float qz, float qw)
    {
        var cell = new CellPos(Wrap(x), Wrap(y), Wrap(z));
        var raw = new Quaternion(Finite(qx), Finite(qy), Finite(qz), Finite(qw));
        var rotation = raw.LengthSquared() < 1e-6f ? Quaternion.Identity : Quaternion.Normalize(raw);
        var construct = NewConstruct(rotation, new Vector3(123.25f, 4000f, -77.5f));

        Assert.Equal(cell, construct.ParentToCell(construct.CellCenterToParent(cell)));
    }

    private static int Wrap(int value) => (int)(((long)value % CellPos.Limit + CellPos.Limit) % (2 * CellPos.Limit)) - CellPos.Limit;

    private static float Finite(float value) => float.IsFinite(value) ? value % 10f : 0.5f;
}
