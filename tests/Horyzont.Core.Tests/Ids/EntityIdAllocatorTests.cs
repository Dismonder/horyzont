using Horyzont.Core.Ids;

namespace Horyzont.Core.Tests.Ids;

public sealed class EntityIdAllocatorTests
{
    [Fact]
    public void EntityPeekNext_Repeated_DoesNotChangeState()
    {
        var allocator = new EntityIdAllocator(1);

        Assert.Equal(new EntityId(1), allocator.PeekNext());
        Assert.Equal(new EntityId(1), allocator.PeekNext());
        Assert.Equal(1UL, allocator.NextValue);
    }

    [Fact]
    public void EntityMarkUsed_LowerValue_DoesNotMoveBack()
    {
        var allocator = new EntityIdAllocator(1);

        allocator.MarkUsed(new EntityId(5));
        allocator.MarkUsed(new EntityId(3));

        Assert.Equal(6UL, allocator.NextValue);
        Assert.Equal(new EntityId(6), allocator.PeekNext());
    }

    [Fact]
    public void EntityAllocator_ZeroOrTransient_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityIdAllocator(0));
        var allocator = new EntityIdAllocator(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.MarkUsed(new EntityId(EntityId.TransientBase)));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.MarkUsed(EntityId.None));
    }

    [Fact]
    public void EntityPeekNext_AtTransientBase_Throws()
    {
        var allocator = new EntityIdAllocator(EntityId.TransientBase);

        Assert.Throws<InvalidOperationException>(() => allocator.PeekNext());
    }

    [Fact]
    public void PlayerPeekNext_Repeated_DoesNotChangeState()
    {
        var allocator = new PlayerIdAllocator(1);

        Assert.Equal(new PlayerId(1), allocator.PeekNext());
        Assert.Equal(new PlayerId(1), allocator.PeekNext());
        Assert.Equal(1UL, allocator.NextValue);
    }

    [Fact]
    public void PlayerMarkUsed_LowerValue_DoesNotMoveBack()
    {
        var allocator = new PlayerIdAllocator(1);

        allocator.MarkUsed(new PlayerId(4));
        allocator.MarkUsed(new PlayerId(2));

        Assert.Equal(5UL, allocator.NextValue);
    }

    [Fact]
    public void PlayerPeekNext_ReachingTransientBase_Throws()
    {
        var allocator = new PlayerIdAllocator(EntityId.TransientBase);

        Assert.Throws<InvalidOperationException>(() => allocator.PeekNext());
    }

    [Fact]
    public void OpIdAndTick_ToString_PrintValueWithoutRecursion()
    {
        Assert.Equal("7", new OpId(7).ToString());
        Assert.Equal("12", new Tick(12).ToString());
        Assert.Contains("Id = 3", new Operations.JournalEntry(new OpId(3), new Tick(1), null, new Operations.BlockClearOp(new EntityId(1), default)).ToString(), StringComparison.Ordinal);
    }
}
