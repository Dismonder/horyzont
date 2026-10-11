using Godot;

using Horyzont.Client.Navigation;
using Horyzont.Core.Frames;
using Horyzont.Game.Frames;

namespace Horyzont.Game.Client.Navigation;

/// <summary>
/// The own position for the navigation: the position of the current camera in the scene. The scene is the system frame (K5-20, no
/// floating origin), so the global position is the position in the system frame, in double precision through
/// <see cref="GodotFrames"/>; the camera follows every reparent without a jump.
/// </summary>
/// <param name="anchor">A node of the scene, to reach the viewport.</param>
public sealed class CameraViewpoint(Node anchor) : INavigationViewpoint
{
    private readonly Node _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));

    /// <inheritdoc />
    public bool TryGetWorldPosition(double renderTick, out Vector3D position)
    {
        if (GodotObject.IsInstanceValid(_anchor) && _anchor.IsInsideTree() && _anchor.GetViewport()?.GetCamera3D() is { } camera)
        {
            position = GodotFrames.ToCore(camera.GlobalPosition);
            return true;
        }

        position = default;
        return false;
    }
}
