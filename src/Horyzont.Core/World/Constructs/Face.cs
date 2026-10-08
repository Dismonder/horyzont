namespace Horyzont.Core.World.Constructs;

/// <summary>Face of a cell, named by its outward normal in construct space.</summary>
public enum Face : byte
{
    /// <summary>+X.</summary>
    PosX = 0,

    /// <summary>−X.</summary>
    NegX = 1,

    /// <summary>+Y.</summary>
    PosY = 2,

    /// <summary>−Y.</summary>
    NegY = 3,

    /// <summary>+Z.</summary>
    PosZ = 4,

    /// <summary>−Z.</summary>
    NegZ = 5,
}
