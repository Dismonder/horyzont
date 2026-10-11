using Godot;

using Horyzont.Client.Movement;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Frames;

namespace Horyzont.Game.Client.Replication;

/// <summary>
/// Lifecycle of the nodes of the other players (K3-15): created by <c>EntitySpawn</c>, refreshed by a repeated spawn (respawn or
/// resync), freed by <c>EntityDespawn</c> or when the session ends. A node has no physics body and no collision layer; until the
/// presentation of K3-14 is available (a render run with a motion adapter), it is a simple marker placed with the newest sample of
/// <see cref="EntityMirror"/>. With the adapter, each rendered player is a <see cref="RemotePlayerPresentation"/> whose ring is
/// registered at spawn and removed at despawn; the node then places itself 100 ms behind the server from full states.
/// </summary>
public sealed partial class RemotePlayers : Node3D, IClientSessionListener
{
    private const float BodyHeight = 1.8f;
    private const float BodyRadius = 0.35f;

    private readonly Dictionary<EntityId, Node3D> _nodes = [];
    private readonly Dictionary<EntityId, RemoteMotionBuffer> _rings = [];
    private ClientRuntime? _client;
    private EntityMirror? _mirror;
    private ConstructMirror? _constructs;
    private RemoteMotionSampleAdapter? _motion;
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
    /// <param name="constructs">Construct copies that place players standing on constructs; without them such markers are not moved.</param>
    /// <param name="motion">Remote motion adapter of K3-14; without it (or without rendering) players are placed with the newest mirror sample.</param>
    public void Initialize(
        ClientRuntime client, EntityMirror mirror, bool render, ConstructMirror? constructs = null, RemoteMotionSampleAdapter? motion = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(mirror);
        _client = client;
        _mirror = mirror;
        _constructs = constructs;
        _motion = motion;
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
            if (pair.Value is not RemotePlayerPresentation)
            {
                Place(pair.Value, pair.Key);
            }
        }
    }

    private void Place(Node3D node, EntityId entity)
    {
        if (_mirror is null || !_mirror.TryGetPlanetPose(entity, _constructs, out var planet, out var position, out var rotation))
        {
            return;
        }

        node.Position = PlanetFrames.ToGodot(planet, position);
        node.Basis = PlanetFrames.ToGodotBasis(planet, rotation);
    }

    private void OnSpawned(EntitySpawn spawn)
    {
        if (spawn.Kind != EntityKind.Player || (spawn.Owner == _self && _self != default))
        {
            return;
        }

        if (_nodes.TryGetValue(spawn.Entity, out var existing) && existing is RemotePlayerPresentation)
        {
            // Respawn or resync: the ring is new (K3-14), the node is kept.
            _motion?.Register(spawn.Entity, _rings[spawn.Entity]);
            return;
        }

        if (!_nodes.TryGetValue(spawn.Entity, out var node))
        {
            if (_render && _motion is not null)
            {
                var ring = new RemoteMotionBuffer();
                var view = new RemotePlayerPresentation { Name = $"Player_{spawn.Entity.Value}" };
                view.Initialize(ring, _motion.Clock);
                _motion.Register(spawn.Entity, ring);
                _rings[spawn.Entity] = ring;
                _nodes.Add(spawn.Entity, view);
                AddChild(view);
                return;
            }

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

        Place(node, spawn.Entity);
    }

    private void OnDespawned(EntityId id)
    {
        if (_nodes.Remove(id, out var node))
        {
            _motion?.Remove(id);
            _rings.Remove(id);
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
        _rings.Clear();
    }
}
