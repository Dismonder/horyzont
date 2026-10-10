namespace Horyzont.Integration.Tests.Support;

/// <summary>
/// Tests that hold pooled <c>TerrainBlockStore</c> connections or reopen a world file run one at a time, so a pool closed by one test
/// never meets a read of another. Never use <c>SqliteConnection.ClearAllPools</c> in tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlitePools
{
    public const string Name = "SQLite pools";
}
