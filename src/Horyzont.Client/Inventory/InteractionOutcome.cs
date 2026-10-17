namespace Horyzont.Client.Inventory;

/// <summary>What a click on the inventory screen did.</summary>
public enum InteractionOutcome : byte
{
    /// <summary>Nothing happened.</summary>
    Ignored = 0,

    /// <summary>Items were taken in hand.</summary>
    Picked = 1,

    /// <summary>The items in hand were put back.</summary>
    Cancelled = 2,

    /// <summary>An intent was sent.</summary>
    Sent = 3,
}
