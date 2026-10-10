using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Session;

/// <summary>
/// The client side of one process: owns the <see cref="ClientSession"/> and passes its events to listeners, in registration order,
/// on the main thread. A listener that throws a bug passes through <see cref="Poll"/>; <c>ProtocolViolationException</c> and
/// <c>InvalidDataException</c> are turned into a ProtocolViolation disconnect by the session, as for any game hook.
/// </summary>
public sealed class ClientRuntime : IClientGameHooks
{
    private readonly ILogger<ClientRuntime> _logger;
    private IClientSessionListener[] _listeners = [];

    /// <summary>Initializes a new instance of the <see cref="ClientRuntime"/> class.</summary>
    /// <param name="transport">The client transport; the caller owns it.</param>
    /// <param name="options">Session options.</param>
    /// <param name="isHostLocal">True for the local client of a solo or host process: the terrain is shared with the server.</param>
    /// <param name="time">Clock of the session.</param>
    /// <param name="loggers">Loggers.</param>
    public ClientRuntime(ITransport transport, ClientOptions options, bool isHostLocal, TimeProvider time, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggers);
        IsHostLocal = isHostLocal;
        _logger = loggers.CreateLogger<ClientRuntime>();
        Session = new ClientSession(transport, options, this, time, loggers.CreateLogger<ClientSession>());
    }

    /// <summary>Gets the session: <c>SubmitIntent</c>, <c>SendReady</c>, <c>Disconnect</c>, state.</summary>
    public ClientSession Session { get; }

    /// <summary>Gets a value indicating whether this is the local client of a solo or host process (shared terrain, no terrain cache).</summary>
    public bool IsHostLocal { get; }

    /// <summary>Gets the hello of the current connection; null before it arrived.</summary>
    public ServerHello? Hello { get; private set; }

    /// <summary>Adds a listener; it is called after the ones added before.</summary>
    /// <param name="listener">The listener.</param>
    public void AddListener(IClientSessionListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        // Copy on write: an event in progress keeps iterating the array it started with, so a listener may add or remove listeners.
        _listeners = [.. _listeners, listener];
    }

    /// <summary>Removes a listener; a no-op when it was not added.</summary>
    /// <param name="listener">The listener.</param>
    public void RemoveListener(IClientSessionListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        _listeners = [.. _listeners.Where(l => !ReferenceEquals(l, listener))];
    }

    /// <summary>Starts connecting.</summary>
    /// <param name="server">The server endpoint.</param>
    /// <param name="identity">Platform identity.</param>
    /// <param name="displayName">Display name.</param>
    /// <param name="password">Server password.</param>
    /// <exception cref="InvalidOperationException">The session is not disconnected.</exception>
    public void Connect(TransportEndpoint server, PlatformIdentity identity, string displayName, string password = "")
    {
        Hello = null;
        _logger.Connecting(displayName, IsHostLocal);
        Session.Connect(server, identity, displayName, password);
    }

    /// <summary>Delivers transport events to the session and, through it, to the listeners; call once per frame on the main thread.</summary>
    public void Poll() => Session.Poll();

    /// <inheritdoc />
    void IClientGameHooks.OnServerHello(ServerHello hello)
    {
        Hello = hello;
        _logger.Joined(hello.World.Value, hello.YourPlayer.Value);
        foreach (var listener in _listeners)
        {
            listener.OnServerHello(hello);
        }
    }

    /// <inheritdoc />
    void IClientGameHooks.OnRejected(HandshakeRejectCode code, string detail)
    {
        _logger.Rejected(code, detail);
        foreach (var listener in _listeners)
        {
            listener.OnRejected(code, detail);
        }
    }

    /// <inheritdoc />
    void IClientGameHooks.OnMessage(Message message)
    {
        if (message is IntentReply reply)
        {
            foreach (var listener in _listeners)
            {
                listener.OnIntentReply(reply);
            }

            return;
        }

        foreach (var listener in _listeners)
        {
            listener.OnMessage(message);
        }
    }

    /// <inheritdoc />
    void IClientGameHooks.OnDisconnected(DisconnectCode code)
    {
        _logger.Disconnected(code);
        foreach (var listener in _listeners)
        {
            listener.OnDisconnected(code);
        }
    }
}
