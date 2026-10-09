using Horyzont.Persistence.Terrain;

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

    /// <summary>
    /// Closes the pooled read connections of this directory's world file only (they keep the file open on Windows). Never use
    /// <c>SqliteConnection.ClearAllPools</c> in tests: it would break stores of tests running in parallel.
    /// </summary>
    public void ReleasePools() => new TerrainBlockStore(WorldPath).Close();

    public void Dispose()
    {
        ReleasePools();
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
