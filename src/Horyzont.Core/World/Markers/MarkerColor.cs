namespace Horyzont.Core.World.Markers;

/// <summary>Colour of a map marker (K5-27 decision 2). Part of the saved format: values 0..7.</summary>
public enum MarkerColor : byte
{
    /// <summary>White.</summary>
    White = 0,

    /// <summary>Red.</summary>
    Red = 1,

    /// <summary>Green.</summary>
    Green = 2,

    /// <summary>Blue.</summary>
    Blue = 3,

    /// <summary>Yellow.</summary>
    Yellow = 4,

    /// <summary>Cyan.</summary>
    Cyan = 5,

    /// <summary>Magenta.</summary>
    Magenta = 6,

    /// <summary>Orange.</summary>
    Orange = 7,
}
