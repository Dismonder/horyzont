namespace Horyzont.Core.Content;

/// <summary>Equipment slot an item can be worn in (used from K6-33).</summary>
public enum EquipmentSlot : byte
{
    /// <summary>Not wearable.</summary>
    None = 0,

    /// <summary>Helmet.</summary>
    Helmet = 1,

    /// <summary>Suit.</summary>
    Suit = 2,

    /// <summary>Backpack.</summary>
    Backpack = 3,

    /// <summary>Suit module.</summary>
    Module = 4,

    /// <summary>Legs (K6-34).</summary>
    Legs = 5,
}
