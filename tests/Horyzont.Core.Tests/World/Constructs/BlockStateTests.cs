using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.World.Constructs;

public sealed class BlockStateTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 7)]
    [InlineData(9, 3)]
    public void Create_CostRevisionAndFunctionState_RoundTrip(byte costRevision, byte functionState)
    {
        var block = BlockState.Create(513, 23, costRevision, functionState);

        Assert.Equal(513, block.TypeId);
        Assert.Equal(23, block.Orientation);
        Assert.Equal(costRevision, block.CostRevision);
        Assert.Equal(functionState, block.FunctionState);
        Assert.True(block.IsWellFormed);
        Assert.Equal(0u, block.Packed >> 28);
    }

    [Fact]
    public void Create_InvalidFields_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockState.Create(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockState.Create(1, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockState.Create(1, 0, costRevision: 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockState.Create(1, 0, functionState: 8));
    }

    [Fact]
    public void WithFunctionState_ChangesOnlyBits25To27()
    {
        var block = BlockState.Create(77, 5, 11, 2);

        var changed = block.WithFunctionState(6);

        Assert.Equal(6, changed.FunctionState);
        Assert.Equal(block.Packed & ~(0x7u << 25), changed.Packed & ~(0x7u << 25));
    }

    [Fact]
    public void WithFunctionState_EmptyOrTooLarge_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => BlockState.Empty.WithFunctionState(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockState.Create(1, 0).WithFunctionState(8));
    }

    [Fact]
    public void IsWellFormed_FunctionStatesAccepted_ReservedBitsRejected()
    {
        for (byte state = 0; state < BlockState.FunctionStateCount; state++)
        {
            Assert.True(BlockState.Create(1, 0, functionState: state).IsWellFormed);
        }

        for (var bit = 28; bit < 32; bit++)
        {
            Assert.False(new BlockState(BlockState.Create(1, 0).Packed | (1u << bit)).IsWellFormed);
        }

        Assert.True(BlockState.Empty.IsWellFormed);
        Assert.False(new BlockState(24u << 16 | 1).IsWellFormed);
        Assert.False(new BlockState(1u << 16).IsWellFormed);
    }
}
