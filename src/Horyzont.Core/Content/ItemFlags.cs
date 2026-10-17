namespace Horyzont.Core.Content;

/// <summary>Properties of an item type.</summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name from docs/spec/03-przetrwanie.md.")]
public enum ItemFlags : ushort
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>Bulk raw material.</summary>
    Bulk = 1,

    /// <summary>Edible.</summary>
    Food = 2,

    /// <summary>A tool.</summary>
    Tool = 4,

    /// <summary>A weapon.</summary>
    Weapon = 8,

    /// <summary>Armor.</summary>
    Armor = 16,

    /// <summary>Ammunition.</summary>
    Ammo = 32,

    /// <summary>Can be placed in the world.</summary>
    Placeable = 64,

    /// <summary>Retired: kept only so that worlds that store it still load; needs no source.</summary>
    Retired = 128,
}
