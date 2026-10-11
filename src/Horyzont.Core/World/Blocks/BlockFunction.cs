namespace Horyzont.Core.World.Blocks;

/// <summary>The function of a block type: a kind plus the parameters that kind uses (others stay zero).</summary>
/// <param name="Kind">Kind.</param>
/// <param name="Seat">Seat role for <see cref="BlockFunctionKind.Cockpit"/> and <see cref="BlockFunctionKind.Seat"/>.</param>
/// <param name="Tier">Tier of an engine or a cargo bay.</param>
/// <param name="ThrustNewtons">Engine thrust, N.</param>
/// <param name="TorqueNewtonMeters">Stabilizer torque, N·m.</param>
/// <param name="FuelCapacity">Fuel tank capacity, units.</param>
/// <param name="SpeedCapMetersPerSecond">Pulse drive speed cap, m/s.</param>
public readonly record struct BlockFunction(
    BlockFunctionKind Kind, SeatRole Seat = SeatRole.None, byte Tier = 0, float ThrustNewtons = 0f,
    float TorqueNewtonMeters = 0f, float FuelCapacity = 0f, float SpeedCapMetersPerSecond = 0f)
{
    /// <summary>A plain block.</summary>
    public static BlockFunction None => default;

    /// <summary>A cockpit (pilot seat).</summary>
    public static BlockFunction Cockpit => new(BlockFunctionKind.Cockpit, SeatRole.Pilot);

    /// <summary>Landing gear.</summary>
    public static BlockFunction LandingGear => new(BlockFunctionKind.LandingGear);

    /// <summary>A door.</summary>
    public static BlockFunction Door => new(BlockFunctionKind.Door);

    /// <summary>Gets the function of a refuel station (K5-24).</summary>
    public static BlockFunction RefuelStation => new(BlockFunctionKind.RefuelStation);

    /// <summary>A seat block.</summary>
    /// <param name="role">Passenger or navigator.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateSeat(SeatRole role) => new(BlockFunctionKind.Seat, role);

    /// <summary>An engine.</summary>
    /// <param name="thrustNewtons">Thrust, N.</param>
    /// <param name="tier">Tier.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateEngine(float thrustNewtons, byte tier) =>
        new(BlockFunctionKind.Engine, Tier: tier, ThrustNewtons: thrustNewtons);

    /// <summary>A stabilizer.</summary>
    /// <param name="torqueNewtonMeters">Torque, N·m.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateStabilizer(float torqueNewtonMeters) =>
        new(BlockFunctionKind.Stabilizer, TorqueNewtonMeters: torqueNewtonMeters);

    /// <summary>A fuel tank.</summary>
    /// <param name="capacity">Capacity, units.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateFuelTank(float capacity) => new(BlockFunctionKind.FuelTank, FuelCapacity: capacity);

    /// <summary>A cargo bay.</summary>
    /// <param name="tier">Tier.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateCargo(byte tier) => new(BlockFunctionKind.Cargo, Tier: tier);

    /// <summary>A pulse drive.</summary>
    /// <param name="speedCapMetersPerSecond">Speed cap, m/s.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreatePulseDrive(float speedCapMetersPerSecond) =>
        new(BlockFunctionKind.PulseDrive, SpeedCapMetersPerSecond: speedCapMetersPerSecond);
}
