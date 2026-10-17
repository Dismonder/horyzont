namespace Horyzont.Core.Items;

/// <summary>Where an item removed by <see cref="Operations.ItemDestroyOp"/> goes (spec 03; every value exists from format 1).</summary>
public enum ItemSink : byte
{
    /// <summary>Crafting ingredient.</summary>
    Craft = 1,

    /// <summary>Station input.</summary>
    Station = 2,

    /// <summary>Eaten food.</summary>
    Eat = 3,

    /// <summary>Fuel and cells.</summary>
    Fuel = 4,

    /// <summary>Cost of a placed block or of filled terrain.</summary>
    BuildCost = 5,

    /// <summary>Suit refill.</summary>
    SuitRefill = 6,

    /// <summary>Planted seed.</summary>
    Plant = 7,

    /// <summary>Despawned (dropped items disappearing; in K6-29W also a discarded item).</summary>
    Despawn = 8,

    /// <summary>Administrator command.</summary>
    Admin = 9,

    /// <summary>Ammunition (subproject 4).</summary>
    Ammo = 10,

    /// <summary>Offering (subproject 5).</summary>
    Offering = 11,
}
