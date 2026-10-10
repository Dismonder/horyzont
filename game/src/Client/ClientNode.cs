using Godot;

using Horyzont.Client.Session;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client;

/// <summary>
/// <c>/Root/Client</c>: polls the client session once per physics frame (after the server node, so a host's server tick always
/// precedes the poll of its local client), prepares the terrain when the server hello arrives and reports the end of the session.
/// Created by <c>GameComposition</c>, which calls <see cref="Initialize"/> before the node enters the tree.
/// </summary>
public sealed partial class ClientNode : Node, IClientSessionListener
{
    private ClientRuntime? _runtime;
    private ILogger<ClientNode>? _logger;

    /// <summary>Raised on the main thread when the session ended (the server closed it, the connection broke or we disconnected).</summary>
    public event Action<DisconnectCode>? SessionEnded;

    /// <summary>Raised on the main thread when the server refused the connection.</summary>
    public event Action<HandshakeRejectCode, string>? ConnectionRejected;

    /// <summary>Gets the client runtime.</summary>
    public ClientRuntime Runtime => _runtime ?? throw new InvalidOperationException("ClientNode.Initialize was not called.");

    /// <summary>Gets or sets the terrain the client sees; must be set before the server hello arrives.</summary>
    public IClientTerrainProvider? TerrainProvider { get; set; }

    /// <summary>
    /// Gets or sets the terrain replication of a remote client (K3-16); null for a host and solo player, who share the terrain of the
    /// server. It reports the cache and the viewers before the client says ready.
    /// </summary>
    public ClientTerrainReplicator? TerrainReplicator { get; set; }

    /// <summary>Gives the node its dependencies.</summary>
    /// <param name="runtime">The client runtime.</param>
    /// <param name="logger">Logger.</param>
    public void Initialize(ClientRuntime runtime, ILogger<ClientNode> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _logger = logger;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_runtime is null)
        {
            throw new InvalidOperationException("ClientNode.Initialize was not called before the node entered the tree.");
        }

        ProcessPhysicsPriority = 0;
        _runtime.AddListener(this);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _runtime?.RemoveListener(this);
        TerrainReplicator?.Dispose();
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        _runtime?.Poll();
        TerrainReplicator?.OnFrame();
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        var runtime = Runtime;
        try
        {
            (TerrainProvider ?? throw new InvalidOperationException("The client has no terrain provider.")).Prepare(hello);
        }
        catch (NotSupportedException ex)
        {
            // A planet the generator of this build cannot make: the server is newer or hostile. Leave instead of showing a wrong planet.
            ClientNodeLog.TerrainPrepareFailed(_logger!, ex);
            runtime.Session.Disconnect();
            return;
        }

        // The cache report and the first viewers go out before ready, so the server answers with the manifest of what changed.
        TerrainReplicator?.Prepare(hello);
        runtime.Session.SendReady();
    }

    /// <inheritdoc />
    public void OnRejected(HandshakeRejectCode code, string detail) => ConnectionRejected?.Invoke(code, detail);

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => SessionEnded?.Invoke(code);
}

internal static partial class ClientNodeLog
{
    [LoggerMessage(EventId = 9101, Level = LogLevel.Error, Message = "The terrain for the server's planets cannot be made; leaving")]
    internal static partial void TerrainPrepareFailed(ILogger logger, Exception exception);
}
