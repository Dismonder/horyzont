namespace Horyzont.Core.Ids;

/// <summary>Kind of a reference frame.</summary>
public enum FrameKind : byte
{
    /// <summary>Star system frame: origin in the centre of the home planet; the sun is the light direction.</summary>
    System = 1,

    /// <summary>Planet frame: origin in the centre of the planet.</summary>
    Planet = 2,

    /// <summary>Construct frame: origin in the corner of cell (0,0,0).</summary>
    Construct = 3,
}
