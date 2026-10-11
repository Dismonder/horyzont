using Godot;

using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Client.Tools;
using Horyzont.Core.Simulation;
using Horyzont.Game.Client;
using Horyzont.Game.Client.Replication;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Constructs;
using Horyzont.Game.Server.Replication;
using Horyzont.Server.Replication;
using Horyzont.Server.Ships;

namespace Horyzont.Game.Boot;

/// <summary>K3-15: entity and construct replication on both sides of the connection (this partial is owned by that package).</summary>
public sealed partial class GameComposition
{
    private EntityReplicationBinder? _replicationBinder;

    /// <summary>Gets the entity replication of the server once the world has started; null on a client, before the start and with characters off.</summary>
    public EntityReplication? Replication => _replicationBinder?.Replication;

    /// <summary>Gets the reliable send budget the server shares between entity and terrain replication; null on a client.</summary>
    public IReplicationSendBudget? ReplicationBudget => _replicationBinder?.SendBudget;

    /// <summary>Gets the router of replication messages of the client; null in dedicated server mode and with characters off.</summary>
    public ClientReplication? ClientReplication { get; private set; }

    /// <summary>Gets the constructs a remote client sees; null for solo, host and dedicated server.</summary>
    public RemoteConstructs? RemoteConstructs { get; private set; }

    /// <summary>Gets the nodes of the other players; null in dedicated server mode and with characters off.</summary>
    public RemotePlayers? RemotePlayers { get; private set; }

    /// <summary>Gets the tools of a remote client (<c>--connect</c>); null otherwise (solo and host use <see cref="Tools"/>).</summary>
    public ToolNode? RemoteTools { get; private set; }

    /// <summary>Gets the overlay of the block edits of a remote client that the server has not confirmed yet.</summary>
    public ConstructPrediction? BlockPrediction { get; private set; }

    /// <summary>Gets or sets the terrain raycast of a remote client; K3-16 replaces the neutral one before the server hello.</summary>
    public IRemoteTerrainRaycast RemoteTerrain { get; set; } = NoRemoteTerrain.Instance;

    /// <summary>Gets or sets the prediction of terrain edits of a remote client; K3-16 replaces the neutral one before the server hello.</summary>
    public IEditPrediction TerrainPrediction { get; set; } = NoEditPrediction.Instance;

    // The seam is an instance method of the composition so that the owning package reaches the session objects through this.
#pragma warning disable CA1822
    partial void ConfigureReplication()
    {
        ConfigureServerReplication();
        ConfigureClientReplication();
    }
#pragma warning restore CA1822

    private void ConfigureServerReplication()
    {
        if (Server is null || _characters is null)
        {
            return;
        }

        // Registered after the character binder, so the movement step of a tick runs before its snapshots are built.
        // The snapshots draw on the per-client byte budget of K3-14 (20,480 B/s, 2,300 B burst) instead of the fixed 1,150 B.
        MotionBudget = new MotionSnapshotBudget(_env.Time);
        _replicationBinder = new EntityReplicationBinder(
            runtime => _characters.Characters is { } characters ? new ServerReplicationSource(runtime.State, characters) : null,
            _env.Time, _env.Loggers, MotionBudget,
            corrections: runtime => new ShipCorrectionSender(runtime.Simulation, () => Flight?.Flight, () => Flight?.Pilot));
        _replicationBinder.Register(Server);
    }

    private void ConfigureClientReplication()
    {
        if (Client is null || ClientNode is null || LocalPlayer is null)
        {
            return;
        }

        // A process that runs the server shares its world with the local client; only a remote client mirrors the constructs.
        var mirror = Server is null ? new ConstructMirror(_env.Time) : null;
        var replication = new ClientReplication(Client.Session.Send, mirror, _env.Time);
        replication.Decoder.AddSink(LocalPlayer);
        if (_fullSnapshots is not null)
        {
            // The decoder replaces the pass-through of the seam: deltas are rebuilt first, the player only sees full states.
            Client.RemoveListener(_fullSnapshots);
            _fullSnapshots = null;
        }

        Client.AddListener(replication);
        ClientReplication = replication;

        // K5-20: the frame history is fed before the rings, so a reparent rebases samples with the frame poses of the same snapshot.
        ConfigureClientFrames(replication, mirror);

        // K3-14: the remote clock and rings are fed from the decoder (full states), not from the pass-through that it replaced.
        RemoteMotion = new RemoteMotionSampleAdapter(_env.Time);
        LocalPlayer.RemoteMotion = RemoteMotion;
        replication.Decoder.AddSink(RemoteMotion);
        Client.AddListener(RemoteMotion);

        var players = new RemotePlayers { Name = "RemotePlayers" };
        players.Initialize(Client, replication.Entities, _env.Render, mirror, RemoteMotion);
        players.Frames = ClientFrames;
        players.FrameNodes = ClientFrameNodes;
        ClientNode.AddChild(players);
        RemotePlayers = players;
        if (mirror is null)
        {
            return;
        }

        var constructs = new RemoteConstructs { Name = "RemoteConstructs" };
        constructs.Initialize(Client, mirror, LocalPlayer, BlockContentLoader.Load(), _env.Render, _env.Loggers, _env.Time);
        ClientNode.AddChild(constructs);
        RemoteConstructs = constructs;
        RemoteTools = CreateRemoteTools(ClientNode, mirror, replication);
    }

    private ToolNode CreateRemoteTools(ClientNode clientNode, ConstructMirror mirror, ClientReplication replication)
    {
        var scene = GD.Load<PackedScene>(ToolsScene) ?? throw new InvalidOperationException($"Scene {ToolsScene} not found.");
        var tools = scene.Instantiate<ToolNode>();
        tools.FrameNodes = ClientFrameNodes;
        tools.OwnSeat = OwnSeat;
        var prediction = new ConstructPrediction(mirror, () => tools.Ticks);
        BlockPrediction = prediction;
        tools.Initialize(
            Client!,
            hello => new RemoteToolWorldView(hello, RemoteTerrain, mirror, replication.Entities),
            () => new CompositeEditPrediction(prediction, TerrainPrediction),
            LocalPlayer!, new GameRules(), _env.Loggers);
        tools.BlockDefinitions = ShipBlockDefinitions(BlockContentLoader.Load());
        if (_env.Render)
        {
            tools.AddChild(new TerrainToolPreview(tools));
            tools.AddChild(new BlockGhostPreview(tools));
            tools.AddChild(new ToolHud(tools));
            var overlay = new PredictedBlocksView { Name = "PredictedBlocks", FrameNodes = ClientFrameNodes };
            overlay.Initialize(prediction, mirror, LocalPlayer!);
            clientNode.AddChild(overlay);
        }

        clientNode.AddChild(tools);
        return tools;
    }
}
