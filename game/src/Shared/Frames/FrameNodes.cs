using Godot;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

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
    private FrameGraph? _graph;
    private Func<FrameRef, PoseD?>? _worldAt;

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

    /// <summary>A global Godot position in the frame of a planet (through the planet node, then <see cref="PlanetFrames"/>).</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="global">Global Godot position.</param>
    /// <returns>The planet-frame position.</returns>
    public NumVector3 ToPlanetLocal(PlanetId planet, Godot.Vector3 global) =>
        PlanetFrames.ToPlanetLocal(planet, TryGetNode(FrameRef.Of(planet), out var node) ? node.GlobalTransform.AffineInverse() * global : global);

    /// <summary>A planet-frame position as a global Godot position.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="local">Planet-frame position.</param>
    /// <returns>The global Godot position.</returns>
    public Godot.Vector3 ToGlobal(PlanetId planet, NumVector3 local)
    {
        var position = PlanetFrames.ToGodot(planet, local);
        return TryGetNode(FrameRef.Of(planet), out var node) ? node.GlobalTransform * position : position;
    }

    private bool TryGetNode(FrameRef frame, int depth, out Node3D node)
    {
        if (_frames.TryGetValue(frame, out node!) && IsInstanceValid(node))
        {
            return true;
        }

        node = null!;
        if (_source is null || depth <= 0 || !_source.TryGetParent(frame, out var parentFrame))
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
        if (_source is null || !_source.TryGetLocalPose(frame, out var parent, out var pose))
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
