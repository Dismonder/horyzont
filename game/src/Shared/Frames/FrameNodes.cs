using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Frames;

/// <summary>
/// The scene tree of reference frames on one side (K5-20, decision 16): <c>System</c> → <c>Planet_&lt;id&gt;</c> (placement) →
/// <c>Construct_&lt;id&gt;</c> (nested like the constructs). Frame nodes are created on demand and posed from a frame source in
/// <see cref="Compose"/>; content nodes are attached under a frame and moved to another frame keeping their global transform,
/// always in the compose step, never inside physics callbacks. <see cref="ToPlanetLocal"/>/<see cref="ToGlobal"/> take global
/// Godot positions through the planet node before <see cref="PlanetFrames"/> (whose input is the local space of a planet node).
/// One instance per side (server <c>/Root/Server/Frames</c>, client <c>/Root/Client/Frames</c>); no static state.
/// </summary>
public sealed partial class FrameNodes : Node3D
{
    private readonly Dictionary<FrameRef, Node3D> _frames = [];
    private readonly List<(Node3D Node, FrameRef Frame)> _pending = [];
    private IFramePoseSource? _source;
    private Func<IFramePoseSource?>? _sourceFactory;
    private FrameGraph? _graph;
    private Func<FrameRef, PoseD?>? _worldAt;

    /// <summary>Process priority of a node that composes every rendered frame: before the content placed in frames (K5-20, client frame order).</summary>
    public const int ComposePriority = -100;

    /// <summary>Gets or sets a value indicating whether the node runs <see cref="Compose"/> itself every rendered frame (the client side).</summary>
    public bool AutoCompose { get; set; }

    /// <summary>Gets a value indicating whether the frame source exists.</summary>
    public bool HasSource => Source is not null;

    /// <summary>Gets the number of reparents waiting for <see cref="Compose"/>.</summary>
    public int PendingReparents => _pending.Count;

    /// <summary>Sets the frame source.</summary>
    /// <param name="source">Frames and their poses in the parent frame (server: the world graph; client: <c>ClientFrameGraph</c>).</param>
    /// <param name="worldAt">Optional world pose of a frame for this frame of rendering (client: interpolated ships); overrides the source pose.</param>
    public void Initialize(IFramePoseSource source, Func<FrameRef, PoseD?>? worldAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _graph = new FrameGraph(source);
        _worldAt = worldAt;
    }

    /// <summary>Sets a frame source that appears later (the client graph exists from the first server hello, the server graph with the world).</summary>
    /// <param name="source">The source once it exists; null before.</param>
    /// <param name="worldAt">Optional world pose of a frame for this frame of rendering.</param>
    public void Initialize(Func<IFramePoseSource?> source, Func<FrameRef, PoseD?>? worldAt = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sourceFactory = source;
        _worldAt = worldAt;
    }

    /// <inheritdoc />
    public override void _Ready() => ProcessPriority = ComposePriority;

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (AutoCompose)
        {
            Compose();
        }
    }

    /// <summary>The node of a frame, created under its parent frame node when missing.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="node">Receives the node.</param>
    /// <returns>False for an unknown frame or a chain deeper than <see cref="FrameGraph.MaxDepth"/>.</returns>
    public bool TryGetNode(FrameRef frame, out Node3D node) => TryGetNode(frame, FrameGraph.MaxDepth + 1, out node);

    /// <summary>Adds a new content node under a frame (its transform is then local to the frame).</summary>
    /// <param name="node">The node, not yet in the tree.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>False for an unknown frame.</returns>
    public bool Attach(Node3D node, FrameRef frame)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!TryGetNode(frame, out var parent))
        {
            return false;
        }

        parent.AddChild(node);
        return true;
    }

    /// <summary>Moves a content node under another frame at the next <see cref="Compose"/>, keeping its global transform.</summary>
    /// <param name="node">The node.</param>
    /// <param name="frame">The new frame.</param>
    public void Reparent(Node3D node, FrameRef frame)
    {
        ArgumentNullException.ThrowIfNull(node);
        _pending.Add((node, frame));
    }

    /// <summary>The compose step: poses every frame node from the source, then applies the pending reparents.</summary>
    public void Compose()
    {
        foreach (var (frame, node) in _frames)
        {
            if (frame.Kind != FrameKind.System && TryGetLocalTransform(frame, out var local))
            {
                node.Transform = local;
            }
        }

        foreach (var (node, frame) in _pending)
        {
            if (IsInstanceValid(node) && TryGetNode(frame, out var target) && node.GetParent() != target)
            {
                node.Reparent(target, keepGlobalTransform: true);
            }
        }

        _pending.Clear();
    }

    /// <summary>Removes the node of a frame that no longer exists (its content goes with it).</summary>
    /// <param name="frame">The frame.</param>
    public void Forget(FrameRef frame)
    {
        if (_frames.Remove(frame, out var node) && IsInstanceValid(node))
        {
            node.QueueFree();
        }
    }

    /// <summary>The space of a planet frame: conversions through the planet node (identity while the planet has no node).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="parameters">Parameters of the planet (its gravity); optional.</param>
    /// <returns>The space.</returns>
    public PlanetSpace SpaceOf(PlanetId planet, PlanetParams? parameters = null) =>
        new(planet, TryGetNode(FrameRef.Of(planet), out var node) ? node : null, parameters);

    /// <summary>A global Godot position in the frame of a planet (through the planet node, then <see cref="PlanetFrames"/>).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="global">Global Godot position.</param>
    /// <returns>The planet-frame position.</returns>
    public NumVector3 ToPlanetLocal(PlanetId planet, Godot.Vector3 global) => SpaceOf(planet).ToPlanetLocal(global);

    /// <summary>A planet-frame position as a global Godot position.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="local">Planet-frame position.</param>
    /// <returns>The global Godot position.</returns>
    public Godot.Vector3 ToGlobal(PlanetId planet, NumVector3 local) => SpaceOf(planet).ToGodot(local);

    private bool TryGetNode(FrameRef frame, int depth, out Node3D node)
    {
        if (_frames.TryGetValue(frame, out node!) && IsInstanceValid(node))
        {
            return true;
        }

        node = null!;
        if (Source is not { } source || depth <= 0 || !source.TryGetParent(frame, out var parentFrame))
        {
            return false;
        }

        Node3D parent = this;
        if (parentFrame != default && !TryGetNode(parentFrame, depth - 1, out parent))
        {
            return false;
        }

        node = new Node3D { Name = NameOf(frame) };
        if (TryGetLocalTransform(frame, out var local))
        {
            node.Transform = local;
        }

        parent.AddChild(node);
        _frames[frame] = node;
        return true;
    }

    private bool TryGetLocalTransform(FrameRef frame, out Transform3D local)
    {
        local = Transform3D.Identity;
        if (Source is not { } source || !source.TryGetLocalPose(frame, out var parent, out var pose))
        {
            return false;
        }

        if (_worldAt is not null && frame.Kind == FrameKind.Construct && _worldAt(frame) is { } world)
        {
            var parentWorld = parent == default ? PoseD.Identity : _worldAt(parent) ?? (TryWorld(parent, out var p) ? p : PoseD.Identity);
            pose = FrameMath.Relative(parentWorld, world);
        }

        local = GodotFrames.ToGodot(pose);
        return true;
    }

    private IFramePoseSource? Source
    {
        get
        {
            if (_source is null && _sourceFactory?.Invoke() is { } source)
            {
                _source = source;
                _graph = new FrameGraph(source);
            }

            return _source;
        }
    }

    private bool TryWorld(FrameRef frame, out PoseD pose)
    {
        pose = PoseD.Identity;
        return _graph is not null && _graph.TryGetWorldPose(frame, out pose);
    }

    private static string NameOf(FrameRef frame) => frame.Kind switch
    {
        FrameKind.System => $"System_{frame.Id}",
        FrameKind.Planet => $"Planet_{frame.Id}",
        _ => $"Construct_{frame.Id}",
    };
}

/// <summary>Planet spaces from an optional <see cref="FrameNodes"/> (no nodes = the planet in identity).</summary>
public static class FrameNodesSpaces
{
    /// <summary>The space of a planet frame.</summary>
    /// <param name="nodes">The frame nodes of the side; null before they exist.</param>
    /// <param name="planet">The planet.</param>
    /// <param name="parameters">Parameters of the planet; optional.</param>
    /// <returns>The space.</returns>
    public static PlanetSpace PlanetSpaceOf(this FrameNodes? nodes, PlanetId planet, PlanetParams? parameters = null) =>
        nodes is not null && GodotObject.IsInstanceValid(nodes) ? nodes.SpaceOf(planet, parameters) : new PlanetSpace(planet, null, parameters);
}
