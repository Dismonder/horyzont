using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>A fresh directory under the system temp directory, removed on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string WorldPath => File("world.db");

    public string BackupsPath => File("backups");

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        // Pooled read connections (TerrainBlockStore) keep files open on Windows until the pools are cleared.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Safe to leave: the directory is unique and under the temp directory; a handle held by a killed process may linger.
        }
    }
}
