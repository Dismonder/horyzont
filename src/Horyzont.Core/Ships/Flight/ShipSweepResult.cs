using Horyzont.Core.Frames;

namespace Horyzont.Core.Ships.Flight;

/// <summary>Result of sweeping a ship from one pose to another (K5-24 decision 9); computed by the engine collider.</summary>
/// <param name="Hit">True when the ship hits something on the way.</param>
/// <param name="Fraction">Safe fraction of the motion in [0, 1]; 1 without a hit.</param>
/// <param name="Normal">Unit contact normal in the parent frame (pointing away from the obstacle).</param>
public readonly record struct ShipSweepResult(bool Hit, double Fraction, Vector3D Normal)
{
    /// <summary>Gets the result of a free motion.</summary>
    public static ShipSweepResult Free => new(false, 1, Vector3D.Zero);
}
