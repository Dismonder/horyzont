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
}
