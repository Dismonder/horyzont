namespace Horyzont.Core.World.Blocks;

/// <summary>Role of a seat block.</summary>
public enum SeatRole : byte
{
    /// <summary>Not a seat.</summary>
    None = 0,

    /// <summary>The pilot (cockpit).</summary>
    Pilot = 1,

    /// <summary>A passenger.</summary>
    Passenger = 2,

    /// <summary>A navigator.</summary>
    Navigator = 3,
}
