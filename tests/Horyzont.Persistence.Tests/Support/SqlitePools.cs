namespace Horyzont.Persistence.Tests.Support;

/// <summary>
/// Tests that hold pooled <c>TerrainBlockStore</c> connections, wait on them or close them run one at a time, so a
/// pool closed by one test never meets a read of another and timing gates are not disturbed by parallel I/O.
/// Never use <c>SqliteConnection.ClearAllPools</c> in tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlitePools
{
    public const string Name = "SQLite pools";
}
