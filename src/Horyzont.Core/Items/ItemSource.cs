namespace Horyzont.Core.Items;

/// <summary>Where an item created by <see cref="Operations.ItemCreateOp"/> comes from (spec 03; every value exists from format 1).</summary>
public enum ItemSource : byte
{
    /// <summary>Yield of the terrain or of a generator object.</summary>
    Harvest = 1,

    /// <summary>Result of crafting.</summary>
    Craft = 2,

    /// <summary>Output of a station.</summary>
    Station = 3,

    /// <summary>Crop from a plot.</summary>
    Crop = 4,

    /// <summary>Refund of a block cost.</summary>
    Refund = 5,

    /// <summary>Loot of creatures and bosses (subproject 4).</summary>
    Loot = 6,

    /// <summary>Administrator command.</summary>
    Admin = 7,

    /// <summary>Recovered items (subproject 4).</summary>
    Recovered = 8,

    /// <summary>Start kit of a new player.</summary>
    Starter = 9,
}
