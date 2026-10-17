using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Core.Tests.Protocol;

/// <summary>K6-35 B1: the protocol version of the package and the two rules a client needs because the channels are not ordered against each other (D21).</summary>
public sealed class DeathProtocolTests
{
    private static TombstoneEntry Entry(ulong entity) =>
        new(new EntityId(entity), new ContainerId(entity + 100), FrameRef.Of(new PlanetId(1)), 0, new Vector3(entity, 0f, 0f));

    private static PlayerRespawnState Page(uint revision, int page, int pageCount, params ulong[] entities) =>
        PlayerRespawnState.None with { Revision = revision, Page = (ushort)page, PageCount = (ushort)pageCount, Tombstones = [.. entities.Select(Entry)] };

    [Fact]
    public void ProtocolVersion_Is20() => Assert.Equal(20, ProtocolConstants.ProtocolVersion);

    [Fact]
    public void ProgressPages_Reordered_DoesNotPublishPartialList()
    {
        var assembler = new TombstoneListAssembler();
        Assert.Null(assembler.Revision);
        Assert.Empty(assembler.Tombstones);

        // Pages 2, 0, 1 of revision 5: nothing is published before the third one arrives, and the list is in page order.
        Assert.False(assembler.Offer(Page(5, 2, 3, 30)));
        Assert.Empty(assembler.Tombstones);
        Assert.False(assembler.Offer(Page(5, 0, 3, 10, 11)));
        Assert.Empty(assembler.Tombstones);
        Assert.Null(assembler.Revision);
        Assert.True(assembler.Offer(Page(5, 1, 3, 20)));

        Assert.Equal(5u, assembler.Revision);
        Assert.Equal([10ul, 11, 20, 30], assembler.Tombstones.Select(t => t.Entity.Value));
    }

    [Fact]
    public void ProgressPages_NewerRevisionDuringAssembly_DropsThePartialOne()
    {
        var assembler = new TombstoneListAssembler();
        Assert.True(assembler.Offer(Page(1, 0, 1, 10)));

        // Revision 2 is half there; the published list stays revision 1. Revision 3 arrives and replaces the pending one; the late page 1 of revision 2 is ignored.
        Assert.False(assembler.Offer(Page(2, 0, 2, 20)));
        Assert.Equal([10ul], assembler.Tombstones.Select(t => t.Entity.Value));
        Assert.False(assembler.Offer(Page(3, 0, 2, 30)));
        Assert.False(assembler.Offer(Page(2, 1, 2, 21)));
        Assert.Equal(1u, assembler.Revision);
        Assert.True(assembler.Offer(Page(3, 1, 2, 31)));

        Assert.Equal(3u, assembler.Revision);
        Assert.Equal([30ul, 31], assembler.Tombstones.Select(t => t.Entity.Value));
    }

    [Fact]
    public void ProgressPages_OldOrRepeatedRevision_IsIgnored()
    {
        var assembler = new TombstoneListAssembler();
        Assert.True(assembler.Offer(Page(4, 0, 1, 40)));

        Assert.False(assembler.Offer(Page(3, 0, 1, 30)));
        Assert.False(assembler.Offer(Page(4, 0, 1, 41)));

        Assert.Equal(4u, assembler.Revision);
        Assert.Equal([40ul], assembler.Tombstones.Select(t => t.Entity.Value));
    }

    [Fact]
    public void ProgressPages_EmptyListIsOneEmptyPage_AndReplacesAFullOne()
    {
        var assembler = new TombstoneListAssembler();
        Assert.True(assembler.Offer(Page(0, 0, 1)));
        Assert.Equal(0u, assembler.Revision);
        Assert.Empty(assembler.Tombstones);

        Assert.True(assembler.Offer(Page(1, 0, 1, 10)));
        Assert.True(assembler.Offer(Page(2, 0, 1)));
        Assert.Empty(assembler.Tombstones);
        Assert.Equal(2u, assembler.Revision);
    }

    [Fact]
    public void ProgressPages_DuplicatePageOfThePendingRevision_DoesNotCompleteIt()
    {
        var assembler = new TombstoneListAssembler();

        Assert.False(assembler.Offer(Page(7, 0, 2, 70)));
        Assert.False(assembler.Offer(Page(7, 0, 2, 70)));
        Assert.Null(assembler.Revision);
        Assert.True(assembler.Offer(Page(7, 1, 2, 71)));
        Assert.Equal([70ul, 71], assembler.Tombstones.Select(t => t.Entity.Value));
    }

    [Fact]
    public void OldVitalsEpoch_DoesNotOverwriteRespawn()
    {
        var filter = new VitalsEpochFilter();

        // The body before the death, then the death notice names the counter of the new body (7); a sample of the old body that arrives late is refused.
        Assert.True(filter.Accept(new Tick(100), 6));
        Assert.True(filter.Accept(new Tick(110), 6));
        filter.NoteEpoch(7);
        Assert.False(filter.Accept(new Tick(115), 6));

        // The new body: the first sample, then a sample older by tick is refused, a repeat of the newest is harmless, and the old counter stays refused for good.
        Assert.True(filter.Accept(new Tick(120), 7));
        Assert.False(filter.Accept(new Tick(119), 7));
        Assert.True(filter.Accept(new Tick(120), 7));
        Assert.True(filter.Accept(new Tick(121), 7));
        Assert.False(filter.Accept(new Tick(500), 6));
    }

    [Fact]
    public void OldVitalsEpoch_NewCounterWithoutNotice_IsAcceptedAndLowersTheTickBar()
    {
        var filter = new VitalsEpochFilter();
        Assert.True(filter.Accept(new Tick(1000), 3));

        // A reparent that came without a death (boarding a ship) and a sample with an earlier tick of the new counter: the counter decides first.
        Assert.True(filter.Accept(new Tick(900), 4));
        Assert.False(filter.Accept(new Tick(899), 4));
        Assert.False(filter.Accept(new Tick(2000), 3));

        // The counter wraps around like any reparent counter: 0 after the largest value is newer.
        var wrapping = new VitalsEpochFilter();
        Assert.True(wrapping.Accept(new Tick(5), uint.MaxValue));
        Assert.True(wrapping.Accept(new Tick(1), 0));
        Assert.False(wrapping.Accept(new Tick(9), uint.MaxValue));
    }
}
