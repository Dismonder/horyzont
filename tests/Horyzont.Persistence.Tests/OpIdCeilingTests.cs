using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>Operation ids are stored as signed INTEGER, so the supported ceiling is <see cref="long.MaxValue"/>.</summary>
public sealed class OpIdCeilingTests
{
    private const ulong Ceiling = long.MaxValue;

    [Fact]
    public async Task EntryAboveCeiling_IsRefusedBeforeDurability()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());
        RawDb.Execute(dir.WorldPath, "UPDATE meta SET value = $v WHERE key = 'last_checkpoint_op_id';", ("$v", long.MaxValue));
        await using var h = WorldHarness.Open(dir.WorldPath);
        h.FaultsExpected = true;
        Assert.Equal(new OpId(Ceiling), h.Writer.DurableOpId);

        h.Join(1);

        Assert.Equal(new OpId(Ceiling + 1), h.State.LastOpId);
        Assert.False(await h.Writer.FlushAsync());
        h.Pump();
        Assert.True(h.Writer.IsFaulted);
        Assert.Equal(new OpId(Ceiling), h.Writer.DurableOpId);
        Assert.Empty(RawDb.JournalIds(dir.WorldPath));
        Assert.Contains("ceiling", Assert.Single(h.Faults).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NegativeStoredJournalOpId_IsCorruption()
    {
        using var dir = new TempDirectory();
        await JournalTests.RecordOps(dir.WorldPath, 1);
        RawDb.Execute(dir.WorldPath, "UPDATE journal SET op_id = $op;", ("$op", long.MinValue));
        RawDb.Execute(dir.WorldPath, "UPDATE meta SET value = $v WHERE key = 'last_checkpoint_op_id';", ("$v", long.MaxValue));

        Assert.Throws<WorldCorruptedException>(() => WorldLoader.Load(dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(), NullLogger.Instance));
    }

    [Fact]
    public void NegativeStoredCheckpointBarrier_IsCorruption()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [], [], []), WorldHarness.Options, new FakeTimeProvider());
        RawDb.Execute(dir.WorldPath, "UPDATE meta SET value = -1 WHERE key = 'last_checkpoint_op_id';");

        Assert.Throws<WorldCorruptedException>(() => WorldLoader.Load(dir.WorldPath, WorldHarness.LoadOptions(dir.WorldPath), new FakeTimeProvider(), NullLogger.Instance));
    }
}
