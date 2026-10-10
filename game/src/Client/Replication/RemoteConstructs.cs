using Godot;

using Horyzont.Client;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Client.Replication;

/// <summary>
/// The constructs a remote client sees (K3-15): a <see cref="ConstructWorld"/> over the <see cref="ConstructMirror"/>, so meshes and
/// collisions are built from the authoritative copy and never from a prediction. A published state loads the construct, a withdrawn
/// state (new transfer, repair, removal) unloads it and drops queued mesh work of the old generation, an applied operation refreshes
/// the touched chunks. The world is shut down when the session ends, before the client is released (D8). A process that shares the
/// world with its server (solo, host) does not create this node: it has one <see cref="ConstructWorld"/> in <c>HostConstructs</c>.
/// </summary>
public sealed partial class RemoteConstructs : Node, IClientSessionListener
{
    /// <summary>Physics ticks between two updates of the viewer position.</summary>
    public const int ViewerIntervalTicks = 30;

    private ClientRuntime? _client;
    private ConstructMirror? _mirror;
    private ILocalPlayerView? _player;
    private BlockTypeCatalog? _catalog;
    private bool _render;
    private ILoggerFactory? _loggers;
    private TimeProvider _time = TimeProvider.System;
    private ConstructWorld? _world;
    private BlockVisuals? _visuals;
    private Task _shutdown = Task.CompletedTask;
    private int _tick;

    /// <summary>Gets the construct world; null before the server hello and after the session ended.</summary>
    public ConstructWorld? World => _world;

    /// <summary>Gets or sets a test seam run by the mesh workers before each build; set before the server hello.</summary>
    internal Action<MeshJobSlot>? BeforeBuild { get; set; }

    /// <summary>Gets the task of the world shutdown (completed when it never started or has finished).</summary>
    public Task Shutdown => _shutdown;

    /// <summary>Gives the node its dependencies; call before it enters the tree.</summary>
    /// <param name="client">Client runtime whose hello and disconnect drive the node.</param>
    /// <param name="mirror">Mirror of the constructs.</param>
    /// <param name="player">Local player (the viewer position).</param>
    /// <param name="catalog">Block content.</param>
    /// <param name="render">Whether meshes are drawn (false: collision only).</param>
    /// <param name="loggers">Loggers.</param>
    /// <param name="time">Clock for worker shutdown deadlines.</param>
    public void Initialize(
        ClientRuntime client, ConstructMirror mirror, ILocalPlayerView player, BlockTypeCatalog catalog, bool render, ILoggerFactory loggers,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mirror);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _mirror = mirror;
        _player = player;
        _catalog = catalog;
        _render = render;
        _loggers = loggers;
        _time = time;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("RemoteConstructs.Initialize was not called before the node entered the tree.");
        }

        _client.AddListener(this);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _client?.RemoveListener(this);
        Detach();
        _visuals?.Dispose();
        _visuals = null;
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        if (_world is not null || _mirror is null)
        {
            return;
        }

        var table = BlockRenderTable.Build(hello.BlockTypes, _catalog!);
        _visuals = _render ? BlockVisuals.Create(_catalog!) : null;
        var options = new ConstructWorldOptions { Mode = _render ? ConstructWorldMode.RenderAndCollision : ConstructWorldMode.CollisionOnly, TimeProvider = _time };
        _world = new ConstructWorld { Name = "Constructs", BeforeBuild = BeforeBuild };
        AddChild(_world);
        _world.Initialize(_mirror, table, _visuals, options, _loggers!.CreateLogger<ConstructWorld>());
        _mirror.StatePublished += OnPublished;
        _mirror.StateWithdrawn += OnWithdrawn;
        _mirror.EditApplied += OnEdit;
        if (hello.Planets.Count > 0)
        {
            _world.RebuildAll(FrameRef.Of(hello.Planets[0].Id));
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => Detach();

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        if (_world is null || _mirror is null)
        {
            return;
        }

        _mirror.ExpireTransfers();
        if (_tick++ % ViewerIntervalTicks == 0 && _player is { IsActive: true } player)
        {
            _world.SetViewers([player.PredictedFeet]);
        }
    }

    private void Detach()
    {
        if (_mirror is not null)
        {
            _mirror.StatePublished -= OnPublished;
            _mirror.StateWithdrawn -= OnWithdrawn;
            _mirror.EditApplied -= OnEdit;
        }

        if (_world is not null && _shutdown.IsCompleted)
        {
            // Workers are joined and their results dropped before the client and its transport are released.
            _shutdown = _world.ShutdownAsync();
        }
    }

    private void OnPublished(EntityId id)
    {
        if (_world is not null && _mirror!.TryGetPose(id, out var pose))
        {
            // A synthetic creation loads every chunk of the published copy through the normal path of the world.
            _world.OnApplied(
                new ConstructCreateOp(id, pose.Parent, pose.Origin, pose.Rotation, default, BlockState.Create(1, 0)),
                [new ConstructChange(id, pose.Version, false)]);
        }
    }

    private void OnWithdrawn(EntityId id) =>
        _world?.OnApplied(new BlockClearOp(id, default), [new ConstructChange(id, 0, true)]);

    private void OnEdit(Operation operation, IReadOnlyList<ConstructChange> changes) => _world?.OnApplied(operation, changes);
}
