using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.World.Constructs;

public sealed class CellPosTests
{
    [Fact]
    public void Chunk_NegativeCoordinates_FloorDivision()
    {
        var cell = new CellPos(-1, -16, -17);

        Assert.Equal(new ChunkPos(-1, -1, -2), cell.Chunk);
        Assert.Equal((15 << 8) | (0 << 4) | 15, cell.LocalIndex);
    }

    [Fact]
    public void FromChunk_RoundTripsLocalIndex()
    {
        var cell = new CellPos(-1, 33, -2048);

        Assert.Equal(cell, CellPos.FromChunk(cell.Chunk, cell.LocalIndex));
    }

    [Theory]
    [InlineData(-2048, true)]
    [InlineData(2047, true)]
    [InlineData(-2049, false)]
    [InlineData(2048, false)]
    public void IsInBounds_Limits(int value, bool expected)
    {
        Assert.Equal(expected, new CellPos(value, 0, 0).IsInBounds);
        Assert.Equal(expected, new CellPos(0, value, 0).IsInBounds);
        Assert.Equal(expected, new CellPos(0, 0, value).IsInBounds);
    }

    [Theory]
    [InlineData(Face.PosX, 1, 0, 0)]
    [InlineData(Face.NegX, -1, 0, 0)]
    [InlineData(Face.PosY, 0, 1, 0)]
    [InlineData(Face.NegY, 0, -1, 0)]
    [InlineData(Face.PosZ, 0, 0, 1)]
    [InlineData(Face.NegZ, 0, 0, -1)]
    public void Offset_EachFace(Face face, int dx, int dy, int dz)
    {
        Assert.Equal(new CellPos(3 + dx, -4 + dy, 5 + dz), new CellPos(3, -4, 5).Offset(face));
    }
}
