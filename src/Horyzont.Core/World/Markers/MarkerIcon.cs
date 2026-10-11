namespace Horyzont.Core.World.Markers;

/// <summary>Icon of a map marker (K5-27 decision 2). Part of the saved format: values 0..7.</summary>
public enum MarkerIcon : byte
{
    /// <summary>Plain pin.</summary>
    Default = 0,

    /// <summary>Base.</summary>
    Base = 1,

    /// <summary>Outpost.</summary>
    Outpost = 2,

    /// <summary>Resource deposit.</summary>
    Resource = 3,

    /// <summary>Danger.</summary>
    Danger = 4,

    /// <summary>Ship.</summary>
    Ship = 5,

    /// <summary>Landing site.</summary>
    Landing = 6,

    /// <summary>Point of interest.</summary>
    Poi = 7,
}
