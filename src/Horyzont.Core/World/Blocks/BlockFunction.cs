namespace Horyzont.Core.World.Blocks;

/// <summary>The function of a block type: a kind plus the parameters that kind uses (others stay zero).</summary>
/// <param name="Kind">Kind.</param>
/// <param name="Seat">Seat role for <see cref="BlockFunctionKind.Cockpit"/> and <see cref="BlockFunctionKind.Seat"/>.</param>
/// <param name="Tier">Tier of an engine or a cargo bay.</param>
/// <param name="ThrustNewtons">Engine thrust, N.</param>
/// <param name="TorqueNewtonMeters">Stabilizer torque, N·m.</param>
/// <param name="FuelCapacity">Fuel tank capacity, units.</param>
/// <param name="SpeedCapMetersPerSecond">Pulse drive speed cap, m/s.</param>
/// <param name="ContainerSlots">Slots of the container of a <see cref="BlockFunctionKind.Chest"/>, 1..256; for a <see cref="BlockFunctionKind.Station"/> with time the input slots.</param>
/// <param name="Station">Station kind of a <see cref="BlockFunctionKind.Station"/> or <see cref="BlockFunctionKind.StationUpgrade"/> (K6-32).</param>
/// <param name="OutputSlots">Output slots of a station with time, 1..256 (K6-32).</param>
public readonly record struct BlockFunction(
    BlockFunctionKind Kind, SeatRole Seat = SeatRole.None, byte Tier = 0, float ThrustNewtons = 0f,
    float TorqueNewtonMeters = 0f, float FuelCapacity = 0f, float SpeedCapMetersPerSecond = 0f, ushort ContainerSlots = 0,
    StationKind Station = StationKind.Hands, ushort OutputSlots = 0)
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

    /// <summary>A bed (K6-30).</summary>
    public static BlockFunction Bed => new(BlockFunctionKind.Bed);

    /// <summary>A lamp (K6-30); the light parameters are visual content, not Core.</summary>
    public static BlockFunction Light => new(BlockFunctionKind.Light);

    /// <summary>A chest (K6-30).</summary>
    /// <param name="slots">Slots of its container, 1..256.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateChest(ushort slots) => new(BlockFunctionKind.Chest, ContainerSlots: slots);

    /// <summary>A crafting station (K6-32).</summary>
    /// <param name="kind">Station kind, not <see cref="StationKind.Hands"/>.</param>
    /// <param name="inputSlots">Input slots of a station with time, else 0.</param>
    /// <param name="outputSlots">Output slots of a station with time, else 0.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateStation(StationKind kind, ushort inputSlots = 0, ushort outputSlots = 0) =>
        new(BlockFunctionKind.Station, ContainerSlots: inputSlots, Station: kind, OutputSlots: outputSlots);

    /// <summary>An upgrade of a station (K6-32).</summary>
    /// <param name="kind">The station kind it upgrades.</param>
    /// <returns>The function.</returns>
    public static BlockFunction CreateStationUpgrade(StationKind kind) => new(BlockFunctionKind.StationUpgrade, Station: kind);

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
