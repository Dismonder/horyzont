namespace Horyzont.Core.World.Blocks;

/// <summary>What a block does on a ship. Game content, not a save or wire format (numbers are stable for readability only).</summary>
public enum BlockFunctionKind : byte
{
    /// <summary>A plain block (hull, window, building blocks).</summary>
    None = 0,

    /// <summary>Cockpit: the pilot seat and the switch for the ship mode.</summary>
    Cockpit = 1,

    /// <summary>A seat for a passenger or a navigator.</summary>
    Seat = 2,

    /// <summary>An engine: thrust opposite to its nozzle face.</summary>
    Engine = 3,

    /// <summary>A stabilizer: adds rotation torque.</summary>
    Stabilizer = 4,

    /// <summary>A fuel tank.</summary>
    FuelTank = 5,

    /// <summary>A cargo bay (without a container).</summary>
    Cargo = 6,

    /// <summary>Landing gear.</summary>
    LandingGear = 7,

    /// <summary>A pulse drive for interplanetary flight.</summary>
    PulseDrive = 8,

    /// <summary>A door; <c>FunctionState</c> 0 is closed.</summary>
    Door = 9,

    /// <summary>A refuel station: refuels a parked ship nearby for free until subproject 3 (K5-24).</summary>
    RefuelStation = 10,

    /// <summary>A chest with a container hosted by the block (K6-30).</summary>
    Chest = 11,

    /// <summary>A bed that sets the respawn point (K6-30).</summary>
    Bed = 12,

    /// <summary>A lamp; <c>FunctionState</c> 1 is on (K6-30).</summary>
    Light = 13,

    /// <summary>A crafting station; the station kind and, for stations with time, the slots are parameters of the function (K6-32).</summary>
    Station = 14,

    /// <summary>An upgrade of a station (a workbench level) when it stands within the upgrade range (K6-32).</summary>
    StationUpgrade = 15,
}
