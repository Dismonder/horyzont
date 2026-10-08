using System.IO.MemoryMappedFiles;

using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>
/// Opening a world right after a crash on Windows. A mapped view of the stale <c>-shm</c> file (what a scanner does to a
/// just-written file) makes SQLite's recovery fail to truncate it: SQLITE_IOERR_TRUNCATE (extended 1546), which SQLite does not
/// retry itself. The open must ride it out for a bounded time.
/// </summary>
public sealed class TransientLockTests
{
    private const int IoErrTruncate = 1546;

    [Fact]
    public async Task Open_ShmMappedBriefly_SucceedsOnceReleased()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A mapped view blocks truncating a file only on Windows.");
        using var dir = new TempDirectory();
        CreateCrashedWorld(dir);
        Assert.Equal(IoErrTruncate, ExtendedCodeOfOpenWhileMapped(dir)); // the mechanism itself, without the retry window

        var mapping = MapShm(dir);
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(300, TestContext.Current.CancellationToken);
                mapping.Dispose();
            },
            TestContext.Current.CancellationToken);

        using (var database = WorldDatabase.Open(dir.WorldPath))
        {
            Assert.Equal("ok", database.QuickCheck());
        }

        await release;
    }

    [Fact]
    public void Open_ShmMappedForTheWholeWindow_ThrowsTheSqliteError()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A mapped view blocks truncating a file only on Windows.");
        using var dir = new TempDirectory();
        CreateCrashedWorld(dir);
        using var mapping = MapShm(dir);

        var error = Assert.Throws<SqliteException>(() => WorldDatabase.Open(dir.WorldPath));

        Assert.Equal(IoErrTruncate, error.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData(10, 1546, true)] // IOERR_TRUNCATE
    [InlineData(10, 4618, true)] // IOERR_SHMOPEN
    [InlineData(10, 4874, true)] // IOERR_SHMSIZE
    [InlineData(10, 3850, true)] // IOERR_LOCK
    [InlineData(10, 3338, true)] // IOERR_ACCESS
    [InlineData(10, 2570, true)] // IOERR_DELETE
    [InlineData(10, 266, false)] // IOERR_READ: not a lock conflict
    [InlineData(10, 778, false)] // IOERR_WRITE
    [InlineData(11, 11, false)] // CORRUPT
    [InlineData(26, 26, false)] // NOTADB
    [InlineData(5, 5, false)] // BUSY has its own timeout
    public void IsTransientIo_ClassifiesByExtendedCode(int primary, int extended, bool expected)
    {
        Assert.Equal(expected, WorldDatabase.IsTransientIo(new SqliteException("x", primary, extended)));
    }

    private static int ExtendedCodeOfOpenWhileMapped(TempDirectory dir)
    {
        using var mapping = MapShm(dir);
        using var probe = new SqliteConnection(WorldDatabase.ConnectionString(dir.WorldPath, SqliteOpenMode.ReadWrite, pooling: false));
        probe.Open();
        var error = Assert.Throws<SqliteException>(() => WorldDatabase.ApplyPragmas(probe, readOnly: false));
        return error.SqliteExtendedErrorCode;
    }

    /// <summary>Leaves a world with a stale <c>-wal</c> and <c>-shm</c>, as a killed process does, by copying them from a live connection.</summary>
    private static void CreateCrashedWorld(TempDirectory dir)
    {
        using var source = new TempDirectory();
        WorldCreator.Create(source.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());
        using var connection = new SqliteConnection(WorldDatabase.ConnectionString(source.WorldPath, SqliteOpenMode.ReadWrite, pooling: false));
        connection.Open();
        WorldDatabase.ApplyPragmas(connection, readOnly: false);
        WorldDatabase.Execute(connection, "PRAGMA wal_autocheckpoint = 0; CREATE TABLE scratch (a BLOB); INSERT INTO scratch VALUES (zeroblob(100000));");
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            using var from = new FileStream(source.WorldPath + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var to = new FileStream(dir.WorldPath + suffix, FileMode.Create);
            from.CopyTo(to);
        }

        Assert.True(new FileInfo(dir.WorldPath + "-shm").Length > 0, "The fixture needs a stale -shm.");
    }

    private static MemoryMappedViewAccessor MapShm(TempDirectory dir)
    {
        var stream = new FileStream(dir.WorldPath + "-shm", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var file = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
        var view = file.CreateViewAccessor();
        file.Dispose();
        return view;
    }
}
