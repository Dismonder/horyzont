using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Net.Transport;

namespace Horyzont.Client.Platform;

/// <summary>Session platform seams; configure before runtime construction, notify on the main thread.</summary>
/// <remarks>Implementations handle and log platform failures internally; notifications must not throw.</remarks>
public interface ICompositionPlatform
{
    /// <summary>Extends the server transports and matching listeners before ownership passes to the runtime.</summary>
    /// <param name="transports">Existing transports; added transports are owned by the final composite.</param>
    /// <param name="listen">Listeners for the final transport.</param>
    void ConfigureServerTransports(IList<ITransport> transports, IList<TransportEndpoint> listen);

    /// <summary>Chooses a remote client transport without eagerly creating the UDP fallback.</summary>
    /// <param name="fallback">Creates the default transport only if needed.</param>
    /// <returns>Transport owned by the composition.</returns>
    ITransport CreateClientTransport(Func<ITransport> fallback);

    /// <summary>Starts or schedules a platform connection for a remote client.</summary>
    /// <param name="client">Client to connect with the platform endpoint and identity when ready.</param>
    /// <returns>True when the platform owns the path, including an asynchronous lobby join; false for UDP fallback.</returns>
    bool TryConnectClient(ClientRuntime client);

    /// <summary>Notifies a successful writable world start, after the server listeners are open.</summary>
    /// <param name="world">World identity.</param>
    /// <param name="capacity">Configured player capacity.</param>
    /// <param name="passwordRequired">Whether a password is required, without exposing it.</param>
    void OnWorldStarted(WorldId world, int capacity, bool passwordRequired);

    /// <summary>Marks the session unavailable before shutdown begins; also cancels a pending connection.</summary>
    void OnShutdownRequested();

    /// <summary>Releases session platform state after all transports were successfully disposed.</summary>
    void OnTransportsReleased();
}
