using Horyzont.Core.Frames;
using Horyzont.Core.Ids;

namespace Horyzont.Client.Ships;

/// <summary>
/// The pose a ship is drawn in when it is not the render cursor of the frame graph (K5-25 decision 18, R7): the own ship of the pilot is
/// drawn in its prediction. The presentation of ships (K5-26) asks it before <c>ClientFrameGraph</c>; seated bodies and the seat camera
/// use the same pose as the drawn ship (<see cref="SeatView.ShipPose"/>).
/// </summary>
public interface IClientShipPoseOverride
{
    /// <summary>The pose of <paramref name="ship"/> in its parent frame.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="pose">Receives the pose.</param>
    /// <returns>False when the ship is drawn at the render cursor.</returns>
    bool TryGetPose(EntityId ship, out PoseD pose);
}

/// <summary>The pilot draws its own ship in the prediction (smoothed render pose; K5-25 R7).</summary>
/// <param name="prediction">The pilot prediction.</param>
public sealed class PilotShipPose(ShipPrediction prediction) : IClientShipPoseOverride
{
    /// <summary>Gets or sets the ship the local player pilots; default when none.</summary>
    public EntityId Ship { get; set; }

    /// <inheritdoc />
    public bool TryGetPose(EntityId ship, out PoseD pose)
    {
        if (ship != default && ship == Ship && prediction.HasState)
        {
            pose = prediction.RenderPose();
            return true;
        }

        pose = PoseD.Identity;
        return false;
    }
}
