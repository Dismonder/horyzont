namespace Horyzont.Core.World;

/// <summary>What a death does to the items of the player (K6-35, D3). There is no permanent death and no hardcore mode.</summary>
public enum DeathPenalty : byte
{
    /// <summary>The player keeps every item.</summary>
    Mild = 1,

    /// <summary>The bag and the hotbar go to a tombstone; the equipment stays.</summary>
    Normal = 2,

    /// <summary>The bag, the hotbar and the equipment go to a tombstone.</summary>
    Hard = 3,
}
