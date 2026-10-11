using System.Numerics;

namespace Horyzont.Core.Ships;

/// <summary>Mass properties and capabilities of a construct computed from its blocks and the block content.</summary>
/// <param name="MassKg">Total mass, kg.</param>
/// <param name="CenterOfMass">Centre of mass in construct space (1 unit = 1 cell; the centre of cell c is c + 0.5).</param>
/// <param name="Inertia">Inertia tensor about the centre of mass, construct axes.</param>
/// <param name="Thrust">Engine thrust toward each construct face, N.</param>
/// <param name="TorqueNewtonMeters">Sum of the stabilizer torques, N·m.</param>
/// <param name="FuelCapacity">Sum of the fuel tank capacities, units.</param>
/// <param name="BlockCount">Number of blocks.</param>
/// <param name="Cockpits">Number of cockpits.</param>
/// <param name="Engines">Number of engines.</param>
/// <param name="Stabilizers">Number of stabilizers.</param>
/// <param name="FuelTanks">Number of fuel tanks.</param>
/// <param name="PulseDrives">Number of pulse drives.</param>
/// <param name="LandingGears">Number of landing gear blocks.</param>
/// <param name="Seats">All seats sorted by cell (x, then y, then z): cockpits as pilot seats and the seat blocks.</param>
public sealed record ShipStats(
    float MassKg, Vector3 CenterOfMass, ShipInertia Inertia, ShipThrust Thrust, float TorqueNewtonMeters, float FuelCapacity,
    int BlockCount, int Cockpits, int Engines, int Stabilizers, int FuelTanks, int PulseDrives, int LandingGears,
    IReadOnlyList<ShipSeat> Seats)
{
    /// <summary>
    /// Gets the control frame (K5-24 decision 2, P2): the rotation from the cockpit axes (+X right, +Y up, −Z forward) to the
    /// construct axes, from the orientation of the first cockpit by cell; identity without a cockpit.
    /// </summary>
    public Quaternion ControlFrame { get; init; } = Quaternion.Identity;

    /// <summary>Gets the radius of a sphere about <see cref="CenterOfMass"/> that contains every block, metres (K5-24).</summary>
    public float BoundingRadius { get; init; }

    /// <summary>
    /// Cell of the control cockpit: the cockpit <see cref="ControlFrame"/> comes from (the smallest cell); its occupant is the
    /// pilot (K5-25 decision 10). Null without a cockpit.
    /// </summary>
    public World.Constructs.CellPos? ControlCell { get; init; }
}
