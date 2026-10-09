using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Tests.Support;

namespace Horyzont.Persistence.Tests;

public sealed class PersistenceWriterGuardTests
{
    /// <summary>E3: a journal entry that cannot be queued must never vanish silently while the writer is neither crashed nor disposed.</summary>
    [Fact]
    public async Task EnqueueJournal_AfterChannelCompletedWithoutCrash_Throws()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var p = h.Join(1);
        await h.FlushAndPump();
        var leave = new PlayerLeaveOp(p, default, 0, default, System.Numerics.Quaternion.Identity, 0);

        h.Writer.CompleteChannelForTest();

        var next = new OpId(h.State.LastOpId.Value + 1);
        var error = Assert.Throws<InvalidOperationException>(() => h.Writer.EnqueueJournal(new JournalEntry(next, default, null, leave)));
        Assert.Contains("journal entry lost", error.Message, StringComparison.Ordinal);
    }
}
