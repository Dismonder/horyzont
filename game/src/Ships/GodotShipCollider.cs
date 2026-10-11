using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Constructs;
using Horyzont.Game.Frames;
using Horyzont.Game.Physics;
using Horyzont.Server.Ships;

namespace Horyzont.Game.Ships;

/// <summary>
/// Engine collision of ship hulls (K5-24 decision 9): one hidden kinematic body per ship (layer 0, mask terrain | constructs) with a
/// <see cref="PhysicsServer3D"/> box shape per <see cref="ShipHullBox"/>, swept by <see cref="PhysicsServer3D.BodyTestMotion"/>
/// with the chunk bodies of the ship itself excluded (the concave chunk shapes cannot be motion-tested). The sweep translates the hull
/// with the rotation of the end pose. Players are not in the mask. Main thread only.
/// </summary>
public sealed class GodotShipCollider : IShipCollider, IDisposable
{
    /// <summary>Margin of the motion test (metres).</summary>
    public const float Margin = 0.02f;

    /// <summary>Mask of the hull proxy: the terrain and the construct chunks.</summary>
    public const uint Mask = CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask;

    private readonly WorldState _state;
    private readonly ShipStatsCache _stats;
    private readonly Func<FrameRef, IFrameSpace?> _spaces;
    private readonly Action<EntityId, Godot.Collections.Array<Rid>> _ownBodies;
    private readonly Dictionary<EntityId, Proxy> _proxies = [];
    private readonly PhysicsTestMotionParameters3D _parameters = new() { Margin = Margin, MaxCollisions = 1, RecoveryAsCollision = true };
    private readonly PhysicsTestMotionResult3D _result = new();
    private readonly Godot.Collections.Array<Rid> _excluded = [];

    /// <summary>Initializes a new instance of the <see cref="GodotShipCollider"/> class.</summary>
    /// <param name="state">The server world (construct mode and parent).</param>
    /// <param name="stats">Hull boxes of the ships.</param>
    /// <param name="spaces">The space of a frame (physics space and placement in the Godot world); null when unknown.</param>
    /// <param name="ownBodies">Adds the collision bodies of a construct to exclude (its chunk bodies in every construct world of the space).</param>
    public GodotShipCollider(
        WorldState state, ShipStatsCache stats, Func<FrameRef, IFrameSpace?> spaces, Action<EntityId, Godot.Collections.Array<Rid>> ownBodies)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces));
        _ownBodies = ownBodies ?? throw new ArgumentNullException(nameof(ownBodies));
    }

    /// <summary>Gets the number of ships with a proxy body.</summary>
    public int ProxyCount => _proxies.Count;

    /// <summary>Adds the chunk bodies of a construct shown by a construct world.</summary>
    /// <param name="world">The construct world.</param>
    /// <param name="construct">The construct.</param>
    /// <param name="into">Receives the body ids.</param>
    public static void AddChunkBodies(ConstructWorld world, EntityId construct, Godot.Collections.Array<Rid> into)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(into);
        if (!world.TryGetConstruct(construct, out var node))
        {
            return;
        }

        foreach (var chunk in node.Chunks)
        {
            if (chunk.Body is { } body && GodotObject.IsInstanceValid(body))
            {
                into.Add(body.GetRid());
            }
        }
    }

    /// <inheritdoc />
    public ShipSweepResult Sweep(EntityId ship, in PoseD startPose, in PoseD endPose)
    {
        if (!_state.Constructs.TryGet(ship, out var construct) || construct.Mode != ConstructMode.Ship
            || !_stats.TryGetHullBoxes(ship, out var boxes) || boxes.Length == 0
            || _spaces(construct.Parent) is not { } space || !space.PhysicsSpace.IsValid)
        {
            return ShipSweepResult.Free;
        }

        var proxy = ProxyFor(ship, boxes, space.PhysicsSpace);
        var frame = space.GlobalTransform;
        var start = frame * new Transform3D(new Basis(GodotFrames.ToGodot(endPose.Rotation)), GodotFrames.ToGodot(startPose.Position));
        var end = frame * GodotFrames.ToGodot(endPose.Position);
        PhysicsServer3D.BodySetState(proxy.Body, PhysicsServer3D.BodyState.Transform, start);
        _excluded.Clear();
        _ownBodies(ship, _excluded);
        _parameters.From = start;
        _parameters.Motion = end - start.Origin;
        _parameters.ExcludeBodies = _excluded;
        if (!PhysicsServer3D.BodyTestMotion(proxy.Body, _parameters, _result))
        {
            return ShipSweepResult.Free;
        }

        var normal = (frame.Basis.Inverse() * _result.GetCollisionNormal()).Normalized();
        return new ShipSweepResult(true, Math.Clamp(_result.GetCollisionSafeFraction(), 0, 1), GodotFrames.ToCore(normal));
    }

    /// <inheritdoc />
    public void Forget(EntityId ship)
    {
        if (_proxies.Remove(ship, out var proxy))
        {
            proxy.Free();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var proxy in _proxies.Values)
        {
            proxy.Free();
        }

        _proxies.Clear();
        _parameters.Dispose();
        _result.Dispose();
    }

    private Proxy ProxyFor(EntityId ship, ShipHullBox[] boxes, Rid space)
    {
        if (_proxies.TryGetValue(ship, out var proxy) && ReferenceEquals(proxy.Boxes, boxes) && proxy.Space == space)
        {
            return proxy;
        }

        proxy?.Free();
        proxy = new Proxy(boxes, space);
        _proxies[ship] = proxy;
        return proxy;
    }

    private sealed class Proxy
    {
        private readonly Rid[] _shapes;

        public Proxy(ShipHullBox[] boxes, Rid space)
        {
            Boxes = boxes;
            Space = space;
            Body = PhysicsServer3D.BodyCreate();
            PhysicsServer3D.BodySetMode(Body, PhysicsServer3D.BodyMode.Kinematic);
            PhysicsServer3D.BodySetCollisionLayer(Body, 0);
            PhysicsServer3D.BodySetCollisionMask(Body, Mask);
            _shapes = new Rid[boxes.Length];
            for (var i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                var size = new Godot.Vector3(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y, box.Max.Z - box.Min.Z);
                var center = new Godot.Vector3(box.Min.X, box.Min.Y, box.Min.Z) + (size / 2);
                _shapes[i] = PhysicsServer3D.BoxShapeCreate();
                PhysicsServer3D.ShapeSetData(_shapes[i], size / 2);
                PhysicsServer3D.BodyAddShape(Body, _shapes[i], new Transform3D(Basis.Identity, center));
            }

            PhysicsServer3D.BodySetSpace(Body, space);
        }

        public ShipHullBox[] Boxes { get; }

        public Rid Space { get; }

        public Rid Body { get; }

        public void Free()
        {
            PhysicsServer3D.FreeRid(Body);
            foreach (var shape in _shapes)
            {
                PhysicsServer3D.FreeRid(shape);
            }
        }
    }
}
