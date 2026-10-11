namespace Horyzont.Core.World.Ships;

/// <summary>Persisted flags of a ship (blob 12). Unknown bits are rejected by the decoder.</summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name fixed by the K5-24 plan; a [Flags] enum of state bits.")]
public enum ShipStateFlags : byte
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The ship stands parked: gear down on the ground and slower than the park speed.</summary>
    Parked = 1,

    /// <summary>The landing gear is down.</summary>
    GearDown = 2,

    /// <summary>The flight assist is on.</summary>
    Assist = 4,
}
