using System.Numerics;

using Horyzont.Core.Frames;

namespace Horyzont.Core.Ships.Flight;

/// <summary>
/// Motion state of a ship advanced by <see cref="ShipFlightModel.Step"/> through <c>ref</c> (K5-24 decision 4). The pose is the
/// pose of the construct frame origin in the parent frame (double); the linear velocity is in the parent frame, the angular velocity
/// in construct axes. The rotation turns about the centre of mass of <see cref="ShipStats"/>.
/// </summary>
public struct ShipMotion
{
    /// <summary>Gets or sets the pose of the construct origin in the parent frame.</summary>
    public PoseD Pose { get; set; }

    /// <summary>Gets or sets the linear velocity in the parent frame, m/s.</summary>
    public Vector3 LinearVelocity { get; set; }

    /// <summary>Gets or sets the angular velocity in construct axes, rad/s.</summary>
    public Vector3 AngularVelocity { get; set; }

    /// <summary>Gets or sets the fuel units.</summary>
    public float Fuel { get; set; }

    /// <summary>Gets or sets the flags.</summary>
    public ShipFlightFlags Flags { get; set; }
}
