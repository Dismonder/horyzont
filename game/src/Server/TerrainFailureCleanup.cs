namespace Horyzont.Game.Server;

/// <summary>Closes resources after failed construction without blocking the scene thread.</summary>
internal static class TerrainFailureCleanup
{
    internal static async Task RunAsync(Func<Task<bool>> drain, Action closeStore, Action disposeService)
    {
        if (await drain().ConfigureAwait(true))
        {
            closeStore();
            disposeService();
        }
    }
}
