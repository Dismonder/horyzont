namespace Horyzont.Client.Blocks;

/// <summary>What <see cref="BlockInteraction.Interact"/> did.</summary>
public enum BlockInteractionResult : byte
{
    /// <summary>A <c>BlockInteractIntent</c> went to the server.</summary>
    Sent = 0,

    /// <summary>The subscription to a chest went to the server; the screen opens when the state arrives.</summary>
    ChestRequested = 1,

    /// <summary>The crosshair is on no block that reacts to the key within reach.</summary>
    NothingToInteract = 2,

    /// <summary>The block is an occupied bed, the own bed or a block without a replicated state: nothing was sent.</summary>
    Refused = 3,

    /// <summary>The session cannot send now.</summary>
    CannotSend = 4,

    /// <summary>Too many intents wait for the server.</summary>
    Waiting = 5,
}
