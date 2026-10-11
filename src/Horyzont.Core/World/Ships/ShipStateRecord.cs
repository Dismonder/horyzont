using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Ships;

/// <summary>
/// Flight state of a ship outside the journal and outside the state hash (K5-24 decision 11, spec 02:138–140, 02:194): fuel,
/// velocities and flags. The pose lives in the construct header; the double-precision pose only in the server flight.
/// </summary>
/// <param name="Construct">The ship construct.</param>
/// <param name="Fuel">Fuel units; finite and non-negative.</param>
/// <param name="LinearVelocity">Velocity of the construct origin in the parent frame, m/s.</param>
/// <param name="AngularVelocity">Angular velocity in construct axes, rad/s.</param>
/// <param name="Flags">Flags.</param>
public readonly record struct ShipStateRecord(EntityId Construct, float Fuel, Vector3 LinearVelocity, Vector3 AngularVelocity, ShipStateFlags Flags)
{
    /// <summary>All defined flag bits.</summary>
    public const ShipStateFlags KnownFlags = ShipStateFlags.Parked | ShipStateFlags.GearDown | ShipStateFlags.Assist;

    /// <summary>Gets a value indicating whether every value is finite, the fuel non-negative and no unknown flag bit is set.</summary>
    public bool IsValid =>
        float.IsFinite(Fuel) && Fuel >= 0f && IsFinite(LinearVelocity) && IsFinite(AngularVelocity) && (Flags & ~KnownFlags) == 0;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
