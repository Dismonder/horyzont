namespace Horyzont.Client.Blocks;

/// <summary>What the interaction key would do with the block under the crosshair, or why it would not (K6-30).</summary>
public enum BlockPromptKind : byte
{
    /// <summary>Open a chest.</summary>
    OpenChest = 1,

    /// <summary>A private chest of another player: the key does nothing the server would allow.</summary>
    ChestLocked = 2,

    /// <summary>Open a closed door.</summary>
    OpenDoor = 3,

    /// <summary>Close an open door.</summary>
    CloseDoor = 4,

    /// <summary>Switch a lamp on.</summary>
    SwitchLightOn = 5,

    /// <summary>Switch a lamp off.</summary>
    SwitchLightOff = 6,

    /// <summary>Sleep in a free bed and make it the respawn point.</summary>
    Sleep = 7,

    /// <summary>A bed of the local player (it is already the respawn point): nothing to do.</summary>
    OwnBed = 8,

    /// <summary>A bed of another player.</summary>
    BedOccupied = 9,

    /// <summary>Open a station: the crafting panel (workbench, kitchen, fabricator) or the panel with the input and the output (furnace, refinery; K6-32).</summary>
    OpenStation = 10,
}
