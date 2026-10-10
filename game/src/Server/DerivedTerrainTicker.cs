using Horyzont.Server;

namespace Horyzont.Game.Server;

/// <summary>
/// Steps the persistent mipmaps of the server terrain once per tick and drains their database in step 7a of the shutdown (K3-16).
/// The terrain exists only after the world has loaded, so it is read from the factory on every tick.
/// </summary>
public sealed class DerivedTerrainTicker(VoxelServerTerrainFactory factory) : IServerTickParticipant
{
    private bool _stopped;

    /// <summary>Registers the ticker on a runtime that has not started yet.</summary>
    /// <param name="runtime">The runtime.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.AddTickParticipant(this);
        runtime.AddAfterTerrainShutdown(CloseAsync);
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        if (!_stopped)
        {
            factory.Terrain?.Derived?.Step();
        }
    }

    /// <inheritdoc />
    public void OnShutdown(ServerRuntime runtime) => _stopped = true;

    private async Task CloseAsync()
    {
        _stopped = true;
        if (factory.Terrain?.Derived is { } derived)
        {
            // Dispose only after a successful close: on a timeout the writer still uses the database and the codec.
            await TerrainFailureCleanup.DisposeAfterCloseAsync(derived.CloseAsync, derived.Dispose).ConfigureAwait(true);
        }
    }
}
