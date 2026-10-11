using Horyzont.Client.Markers;
using Horyzont.Client.Replication;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Client.Navigation;

/// <summary>
/// The selected navigation target and the vector to it (K5-27 decision 10). Everything is computed in the system frame in double
/// precision from the world pose of the frames, so a reparent of the player or of the target never changes the world position and
/// never makes the guidance jump (README Y4, ZASADY point 2). Only the final direction is converted to single precision by the HUD.
/// A target that disappeared (removed marker, entity out of range) clears the selection. Main thread only; no allocation per call.
/// </summary>
public sealed class NavigationCoordinator
{
    private readonly IWorldPoseSource _poses;
    private readonly ClientMarkerStore _markers;
    private readonly EntityMirror? _entities;
    private readonly INavigationViewpoint _viewpoint;
    private NavigationTarget? _target;

    /// <summary>Initializes a new instance of the <see cref="NavigationCoordinator"/> class.</summary>
    /// <param name="poses">World poses of frames.</param>
    /// <param name="markers">Markers of the client.</param>
    /// <param name="entities">Replicated entities (ships and players); null when the client has none.</param>
    /// <param name="viewpoint">Own position in the world.</param>
    public NavigationCoordinator(IWorldPoseSource poses, ClientMarkerStore markers, EntityMirror? entities, INavigationViewpoint viewpoint)
    {
        ArgumentNullException.ThrowIfNull(poses);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(viewpoint);
        _poses = poses;
        _markers = markers;
        _entities = entities;
        _viewpoint = viewpoint;
    }

    /// <summary>Raised after the selection changed.</summary>
    public event Action? SelectionChanged;

    /// <summary>Gets the selected target, if any.</summary>
    public NavigationTarget? Target => _target;

    /// <summary>Selects a target.</summary>
    /// <param name="target">The target.</param>
    public void Select(NavigationTarget target)
    {
        if (_target == target)
        {
            return;
        }

        _target = target;
        SelectionChanged?.Invoke();
    }

    /// <summary>Clears the selection.</summary>
    public void Clear()
    {
        if (_target is null)
        {
            return;
        }

        _target = null;
        SelectionChanged?.Invoke();
    }

    /// <summary>Computes the guidance to the selected target.</summary>
    /// <param name="renderTick">Server tick being rendered (fractional).</param>
    /// <param name="guidance">Receives the guidance.</param>
    /// <returns>False without a target, while a pose is unknown, or when the target disappeared (the selection is then cleared).</returns>
    public bool TryGetGuidance(double renderTick, out NavigationGuidance guidance)
    {
        guidance = default;
        if (_target is not { } target)
        {
            return false;
        }

        if (!TryGetTargetPosition(target, renderTick, out var targetPosition, out var gone))
        {
            if (gone)
            {
                Clear();
            }

            return false;
        }

        if (!_viewpoint.TryGetWorldPosition(renderTick, out var own))
        {
            return false;
        }

        var delta = targetPosition - own;
        guidance = new NavigationGuidance(delta, delta.Length(), target);
        return true;
    }

    private bool TryGetTargetPosition(NavigationTarget target, double renderTick, out Vector3D position, out bool gone)
    {
        position = default;
        gone = false;
        switch (target.Kind)
        {
            case NavigationTargetKind.Marker:
                if (!_markers.TryGet(new MarkerId(target.Id), out var marker))
                {
                    gone = true;
                    return false;
                }

                if (!_poses.TryGetWorldPose(FrameRef.Of(marker.Planet), renderTick, out var planet))
                {
                    return false;
                }

                position = FrameMath.TransformPoint(planet, Vector3D.FromFloat(marker.Position));
                return true;
            case NavigationTargetKind.Body:
                if (!_poses.TryGetWorldPose(FrameRef.Of(new PlanetId(target.Id)), renderTick, out var body))
                {
                    gone = true;
                    return false;
                }

                position = body.Position;
                return true;
            case NavigationTargetKind.Entity:
                if (_entities is null || !_entities.TryGetPose(new EntityId(target.Id), out var frame, out var state))
                {
                    gone = true;
                    return false;
                }

                if (!_poses.TryGetWorldPose(frame.Frame, renderTick, out var entityFrame))
                {
                    return false;
                }

                position = FrameMath.TransformPoint(entityFrame, Vector3D.FromFloat(state.Position));
                return true;
            default:
                gone = true;
                return false;
        }
    }
}
