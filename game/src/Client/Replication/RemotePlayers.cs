using Godot;

using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Frames;

namespace Horyzont.Game.Client.Replication;

/// <summary>
/// Lifecycle of the nodes of the other players (K3-15): created by <c>EntitySpawn</c>, refreshed by a repeated spawn (respawn or
/// resync), freed by <c>EntityDespawn</c> or when the session ends. A node has no physics body and no collision layer; until the
/// presentation of K3-14 replaces it, it is a simple marker placed with the newest sample of <see cref="EntityMirror"/>.
/// </summary>
public sealed partial class RemotePlayers : Node3D, IClientSessionListener
{
    private const float BodyHeight = 1.8f;
    private const float BodyRadius = 0.35f;

    private readonly Dictionary<EntityId, Node3D> _nodes = [];
    private ClientRuntime? _client;
    private EntityMirror? _mirror;
    private bool _render;
    private PlayerId _self;
    private Mesh? _mesh;
    private Material? _material;

    /// <summary>Gets the number of player nodes.</summary>
    public int Count => _nodes.Count;

    /// <summary>Looks up the node of a player body.</summary>
    /// <param name="entity">Body entity.</param>
    /// <param name="node">The node when present.</param>
    /// <returns>True when a node exists.</returns>
    public bool TryGetNode(EntityId entity, out Node3D? node) => _nodes.TryGetValue(entity, out node);

    /// <summary>Gives the node its dependencies; call before it enters the tree.</summary>
    /// <param name="client">Client runtime whose hello and disconnect drive the node.</param>
    /// <param name="mirror">Mirror of the entities.</param>
    /// <param name="render">Whether a marker is drawn (false: the node exists without visuals, for headless runs).</param>
    public void Initialize(ClientRuntime client, EntityMirror mirror, bool render)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mirror);
        _client = client;
        _mirror = mirror;
        _render = render;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_client is null || _mirror is null)
        {
            throw new InvalidOperationException("RemotePlayers.Initialize was not called before the node entered the tree.");
        }

        _client.AddListener(this);
        _mirror.Spawned += OnSpawned;
        _mirror.Despawned += OnDespawned;
        foreach (var id in _mirror.Ids)
        {
            if (_mirror.TryGet(id, out var spawn) && spawn is not null)
            {
                OnSpawned(spawn);
            }
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _client?.RemoveListener(this);
        if (_mirror is not null)
        {
            _mirror.Spawned -= OnSpawned;
            _mirror.Despawned -= OnDespawned;
        }

        Clear();
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _self = hello.YourPlayer;
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => Clear();

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_mirror is null)
        {
            return;
        }

        _mirror.RetryResyncs();
        foreach (var pair in _nodes)
        {
            if (_mirror.TryGet(pair.Key, out var spawn) && spawn is not null)
            {
                Place(pair.Value, spawn);
            }
        }
    }

    private static void Place(Node3D node, EntitySpawn spawn)
    {
        if (spawn.Frame.Frame.Kind != FrameKind.Planet)
        {
            return;
        }

        var planet = new PlanetId(spawn.Frame.Frame.Id);
        node.Position = PlanetFrames.ToGodot(planet, spawn.Initial.Position);
        node.Basis = PlanetFrames.ToGodotBasis(planet, spawn.Initial.Rotation);
    }

    private void OnSpawned(EntitySpawn spawn)
    {
        if (spawn.Kind != EntityKind.Player || (spawn.Owner == _self && _self != default))
        {
            return;
        }

        if (!_nodes.TryGetValue(spawn.Entity, out var node))
        {
            node = new Node3D { Name = $"Player_{spawn.Entity.Value}" };
            if (_render)
            {
                _mesh ??= new CapsuleMesh { Height = BodyHeight, Radius = BodyRadius };
                _material ??= new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.55f, 0.15f) };
                node.AddChild(new MeshInstance3D
                {
                    Name = "Marker",
                    Mesh = _mesh,
                    MaterialOverride = _material,
                    Position = new Vector3(0, BodyHeight * 0.5f, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }

            _nodes.Add(spawn.Entity, node);
            AddChild(node);
        }

        Place(node, spawn);
    }

    private void OnDespawned(EntityId id)
    {
        if (_nodes.Remove(id, out var node))
        {
            node.QueueFree();
        }
    }

    private void Clear()
    {
        foreach (var node in _nodes.Values)
        {
            node.QueueFree();
        }

        _nodes.Clear();
    }
}
