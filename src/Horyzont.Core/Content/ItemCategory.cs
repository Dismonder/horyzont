namespace Horyzont.Core.Content;

/// <summary>Category of an item; it gives the default stack limit (<see cref="ItemCatalog.DefaultMaxStack"/>).</summary>
public enum ItemCategory : byte
{
    /// <summary>Raw material (stone, soil, ore); stack 500.</summary>
    Resource = 1,

    /// <summary>Tool (manipulator, build tool); stack 1.</summary>
    Tool = 2,

    /// <summary>Food; stack 20.</summary>
    Food = 3,

    /// <summary>Weapon; stack 1.</summary>
    Weapon = 4,

    /// <summary>Armor; stack 1.</summary>
    Armor = 5,

    /// <summary>Ammunition; stack 100.</summary>
    Ammo = 6,

    /// <summary>Placeable object; stack 50.</summary>
    Placeable = 7,

    /// <summary>Crafting component; stack 100.</summary>
    Component = 8,
}
