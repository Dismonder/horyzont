namespace Horyzont.Client.Crafting;

/// <summary>What <see cref="CraftingSession.Craft"/> did.</summary>
public enum CraftOutcome : byte
{
    /// <summary>A craft intent went to the server.</summary>
    Sent = 0,

    /// <summary>The recipe belongs to another station kind than the one that is open; nothing was sent.</summary>
    WrongStation = 1,

    /// <summary>The count is outside 1 to the limit of the rules; nothing was sent.</summary>
    InvalidCount = 2,

    /// <summary>The session cannot send now.</summary>
    CannotSend = 3,

    /// <summary>Too many craft intents wait for the server.</summary>
    Waiting = 4,
}
