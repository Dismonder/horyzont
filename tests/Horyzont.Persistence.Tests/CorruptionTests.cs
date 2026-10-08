using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class CorruptionTests
{
    [Fact]
    public async Task JournalPayloadByteChanged_ThrowsCorruptedWithOpId()
    {
        using var dir = new TempDirectory();
        await JournalTests.RecordOps(dir.WorldPath, 5);
        var payload = (byte[])RawDb.Scalar(dir.WorldPath, "SELECT payload FROM journal WHERE op_id = 2;")!;
        payload[payload.Length / 2] ^= 0x40;
        RawDb.Execute(dir.WorldPath, "UPDATE journal SET payload = $p WHERE op_id = 2;", ("$p", payload));

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Equal(new OpId(2), error.OpId);
    }

    [Fact]
    public async Task JournalPayloadUndecodable_WithMatchingCrc_ThrowsCorruptedWithOpId()
    {
        using var dir = new TempDirectory();
        await JournalTests.RecordOps(dir.WorldPath, 3);
        byte[] garbage = [0xFF, 0xFF, 0xFF];
        RawDb.Execute(
            dir.WorldPath, "UPDATE journal SET payload = $p, crc = $c WHERE op_id = 3;",
            ("$p", garbage), ("$c", (long)System.IO.Hashing.Crc32.HashToUInt32(garbage)));

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Equal(new OpId(3), error.OpId);
    }

    [Fact]
    public async Task JournalPayloadTrailingByte_WithMatchingCrc_ThrowsCorruptedWithOpId()
    {
        using var dir = new TempDirectory();
        await JournalTests.RecordOps(dir.WorldPath, 3);
        var payload = (byte[])RawDb.Scalar(dir.WorldPath, "SELECT payload FROM journal WHERE op_id = 2;")!;
        byte[] longer = [.. payload, 0];
        RawDb.Execute(
            dir.WorldPath, "UPDATE journal SET payload = $p, crc = $c WHERE op_id = 2;",
            ("$p", longer), ("$c", (long)System.IO.Hashing.Crc32.HashToUInt32(longer)));

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Equal(new OpId(2), error.OpId);
        Assert.IsType<Core.Serialization.ProtocolViolationException>(error.InnerException);
    }

    [Fact]
    public async Task ConstructBlobDamaged_ThrowsCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            var p = h.Join(1);
            h.BuildOnTerrain(p, 2);
            Assert.True(await h.CheckpointAndPump());
        }

        RawDb.Execute(dir.WorldPath, "UPDATE entities SET data = substr(data, 1, 3);");

        var error = Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));

        Assert.Null(error.OpId);
    }

    [Fact]
    public async Task ConstructRowWithOtherConstructBlob_ThrowsCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            var p = h.Join(1);
            h.BuildOnTerrain(p, 2);
            Assert.True(await h.CheckpointAndPump());
        }

        RawDb.Execute(dir.WorldPath, "UPDATE entities SET entity_id = entity_id + 1000;");

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public void DamagedFile_FailsQuickCheck_ThrowsCorrupted()
    {
        using var dir = new TempDirectory();
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [], [WorldFactory.StoneBlockKey], ["stone"]);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());
        var pageSize = (int)RawDb.Long(dir.WorldPath, "PRAGMA page_size;");
        SqliteConnection.ClearAllPools();
        Assert.False(File.Exists(dir.WorldPath + "-wal") && new FileInfo(dir.WorldPath + "-wal").Length > 0);
        using (var file = new FileStream(dir.WorldPath, FileMode.Open, FileAccess.ReadWrite))
        {
            Assert.True(file.Length >= 4L * pageSize);
            file.Position = 2L * pageSize;
            var garbage = new byte[pageSize];
            Array.Fill(garbage, (byte)0xA5);
            file.Write(garbage);
        }

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }

    [Fact]
    public void NotADatabase_ThrowsCorrupted()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.WorldPath, new string('x', 8192));

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }
}
