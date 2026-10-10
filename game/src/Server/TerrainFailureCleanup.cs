namespace Horyzont.Game.Server;

/// <summary>Closes resources after failed construction without blocking the scene thread.</summary>
internal static class TerrainFailureCleanup
{
    /// <summary>Disposes resources a failed close may still be using only when the close succeeded (D8).</summary>
    /// <param name="close">Closes and drains the owner.</param>
    /// <param name="dispose">Releases what the owner used.</param>
    /// <returns>True when the close succeeded and the resources were disposed.</returns>
    internal static async Task<bool> DisposeAfterCloseAsync(Func<Task<bool>> close, Action dispose)
    {
        if (!await close().ConfigureAwait(true))
        {
            return false;
        }

        dispose();
        return true;
    }

    internal static async Task RunAsync(Func<Task<bool>> drain, Action closeStore, Action disposeService)
    {
        if (await drain().ConfigureAwait(true))
        {
            closeStore();
            disposeService();
        }
    }
}
