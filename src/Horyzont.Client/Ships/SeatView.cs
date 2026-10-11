using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Ships;

/// <summary>
/// The seated body and camera (K5-25 decision 18): both use one ship pose — the override of the pilot (prediction) or the rendered pose
/// of the frame graph (passengers) — composed with <see cref="SeatPose"/>; the look is clamped to ±100° yaw and ±80° pitch around the
/// seat. Pure.
/// </summary>
public static class SeatView
{
    /// <summary>Largest look yaw from the seat forward, radians.</summary>
    public const float MaxYaw = 100f * MathF.PI / 180f;

    /// <summary>Largest look pitch, radians.</summary>
    public const float MaxPitch = 80f * MathF.PI / 180f;

    /// <summary>The ship pose seated players are drawn with: the override when it has one, otherwise the rendered pose.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="poseOverride">The override (pilot prediction); null without one.</param>
    /// <param name="rendered">Rendered pose of the ship (render cursor of the frame graph).</param>
    /// <returns>The pose.</returns>
    public static PoseD ShipPose(EntityId ship, IClientShipPoseOverride? poseOverride, in PoseD rendered) =>
        poseOverride is not null && poseOverride.TryGetPose(ship, out var predicted) ? predicted : rendered;

    /// <summary>
    /// The world pose of an own body attached to a ship (K5-20 open item 5, decision 18): the own pose in the ship frame composed with the
    /// ship at the render cursor — the time remote passengers and the drawn ship use — or, for the pilot, with the prediction of the
    /// override placed in the parent of the ship at the render cursor.
    /// </summary>
    /// <param name="history">World poses of frames by tick (client: <c>ClientFrameGraph</c>).</param>
    /// <param name="frames">Parents of frames (client: <c>ClientFrameGraph</c>).</param>
    /// <param name="own">The own state; its frame must be a construct.</param>
    /// <param name="poseOverride">The override (pilot prediction); null without one.</param>
    /// <param name="renderTick">Server tick being rendered.</param>
    /// <param name="pose">Receives the world pose of the body (feet, rotation).</param>
    /// <returns>False outside a construct frame or for an unknown frame.</returns>
    public static bool TryGetSeatedWorldPose(
        IFrameHistoryView history, IFramePoseSource frames, in Horyzont.Client.Movement.OwnState own, IClientShipPoseOverride? poseOverride,
        double renderTick, out PoseD pose)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(frames);
        pose = PoseD.Identity;
        if (own.Frame.Kind != FrameKind.Construct)
        {
            return false;
        }

        PoseD ship;
        if (poseOverride is not null && poseOverride.TryGetPose(new EntityId(own.Frame.Id), out var local)
            && frames.TryGetParent(own.Frame, out var parent) && history.TryGetWorldPose(parent, renderTick, out var parentWorld))
        {
            ship = FrameMath.Compose(parentWorld, local);
        }
        else if (!history.TryGetWorldPose(own.Frame, renderTick, out ship))
        {
            return false;
        }

        pose = FrameMath.Compose(ship, FrameMath.FromFloat(own.Feet, own.Rotation));
        return true;
    }

    /// <summary>The body pose (feet, rotation) of a seated player in the parent frame of the ship.</summary>
    /// <param name="ship">Ship pose.</param>
    /// <param name="seat">Seat cell.</param>
    /// <param name="orientation">Block orientation of the seat.</param>
    /// <param name="rules">Seat rules.</param>
    /// <returns>The body pose.</returns>
    public static PoseD Body(in PoseD ship, CellPos seat, byte orientation, SeatRules rules) =>
        FrameMath.Compose(ship, FrameMath.FromFloat(SeatPose.Feet(seat, orientation, rules), SeatPose.Rotation(orientation)));

    /// <summary>The camera pose: the seated eye, rotated by the clamped look (yaw about the seat up, then pitch).</summary>
    /// <param name="ship">Ship pose.</param>
    /// <param name="seat">Seat cell.</param>
    /// <param name="orientation">Block orientation of the seat.</param>
    /// <param name="rules">Seat rules.</param>
    /// <param name="lookYaw">Look yaw, radians (clamped).</param>
    /// <param name="lookPitch">Look pitch, radians (clamped).</param>
    /// <returns>The camera pose in the parent frame of the ship.</returns>
    public static PoseD Camera(in PoseD ship, CellPos seat, byte orientation, SeatRules rules, float lookYaw, float lookPitch)
    {
        var look = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Math.Clamp(lookYaw, -MaxYaw, MaxYaw))
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, Math.Clamp(lookPitch, -MaxPitch, MaxPitch));
        var local = FrameMath.FromFloat(SeatPose.Eye(seat, orientation, rules), Quaternion.Normalize(SeatPose.Rotation(orientation) * look));
        return FrameMath.Compose(ship, local);
    }
}
