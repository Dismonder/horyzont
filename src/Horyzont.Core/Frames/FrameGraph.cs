using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>
/// Composition of the reference frame tree in double precision (K5-20 decision 4). Holds no state besides its source; allocation-free.
/// A chain longer than <see cref="MaxDepth"/> parent links (cycles included) or a frame without a known pose gives <c>false</c>.
/// The world is the system frame: the system frame has the identity world pose.
/// </summary>
public sealed class FrameGraph
{
    /// <summary>Largest number of parent links from a frame to the system frame (as <c>EntityMirror.MaxFrameDepth</c>).</summary>
    public const int MaxDepth = 8;

    private readonly IFramePoseSource _source;

    /// <summary>Creates the graph over a source.</summary>
    /// <param name="source">Frame poses.</param>
    public FrameGraph(IFramePoseSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Gets the source of frame poses.</summary>
    public IFramePoseSource Source => _source;

    /// <summary>Pose of a frame in the world (system frame).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="pose">Receives the world pose.</param>
    /// <returns>False for an unknown frame, a missing pose or a chain that is too deep.</returns>
    public bool TryGetWorldPose(FrameRef frame, out PoseD pose) => TryGetPoseIn(_source, frame, default, MaxDepth, out pose);

    /// <summary>Pose of frame <paramref name="to"/> expressed in frame <paramref name="from"/>.</summary>
    /// <param name="from">Reference frame.</param>
    /// <param name="to">Frame to express.</param>
    /// <param name="pose">Receives the relative pose.</param>
    /// <returns>False when either world pose is unknown.</returns>
    public bool TryRelative(FrameRef from, FrameRef to, out PoseD pose)
    {
        if (!TryGetWorldPose(from, out var fromWorld) || !TryGetWorldPose(to, out var toWorld))
        {
            pose = PoseD.Identity;
            return false;
        }

        pose = FrameMath.Relative(fromWorld, toWorld);
        return true;
    }

    /// <summary>A point of frame <paramref name="from"/> expressed in frame <paramref name="to"/>.</summary>
    /// <param name="from">Frame of the point.</param>
    /// <param name="local">The point in <paramref name="from"/>.</param>
    /// <param name="to">Target frame.</param>
    /// <param name="point">Receives the point in <paramref name="to"/>.</param>
    /// <returns>False when either world pose is unknown.</returns>
    public bool TryExpress(FrameRef from, Vector3 local, FrameRef to, out Vector3D point)
    {
        if (!TryGetWorldPose(from, out var fromWorld) || !TryGetWorldPose(to, out var toWorld))
        {
            point = Vector3D.Zero;
            return false;
        }

        point = FrameMath.InverseTransformPoint(toWorld, FrameMath.TransformPoint(fromWorld, Vector3D.FromFloat(local)));
        return true;
    }

    /// <summary>The region of a frame: the nearest planet or system frame among the frame and its ancestors.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="region">Receives the region.</param>
    /// <returns>False for an unknown frame or a chain that is too deep.</returns>
    public bool TryGetRegion(FrameRef frame, out FrameRef region)
    {
        for (var links = 0; links <= MaxDepth; links++)
        {
            if (!_source.TryGetParent(frame, out var parent))
            {
                break;
            }

            if (frame.Kind is FrameKind.Planet or FrameKind.System)
            {
                region = frame;
                return true;
            }

            frame = parent;
        }

        region = default;
        return false;
    }

    /// <summary>
    /// Pose of <paramref name="frame"/> in <paramref name="ancestor"/> (<c>default</c> = the world), composed up the parent chain
    /// over at most <paramref name="maxLinks"/> links (the link of the system frame to the root does not count).
    /// </summary>
    internal static bool TryGetPoseIn<TSource>(TSource source, FrameRef frame, FrameRef ancestor, int maxLinks, out PoseD pose)
        where TSource : IFramePoseSource
    {
        if (!source.TryGetLocalPose(frame, out var parent, out pose))
        {
            return false;
        }

        if (frame == ancestor)
        {
            pose = PoseD.Identity;
            return true;
        }

        var links = parent == default ? 0 : 1;
        while (parent != ancestor)
        {
            if (parent == default || links > maxLinks || !source.TryGetLocalPose(parent, out var next, out var parentPose))
            {
                pose = PoseD.Identity;
                return false;
            }

            pose = FrameMath.Compose(parentPose, pose);
            parent = next;
            if (parent != default)
            {
                links++;
            }
        }

        if (links > maxLinks)
        {
            pose = PoseD.Identity;
            return false;
        }

        return true;
    }
}
