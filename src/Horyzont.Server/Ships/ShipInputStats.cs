namespace Horyzont.Server.Ships;

/// <summary>Counters of the pilot input (K5-25 decision 13, measurement 2).</summary>
public sealed class ShipInputStats
{
    /// <summary>Gets the slots stepped with a substitute control (input late or lost).</summary>
    public long Substituted { get; internal set; }

    /// <summary>Gets the re-anchorings of the slots (client clock drift).</summary>
    public long Reanchors { get; internal set; }

    /// <summary>Gets the inputs dropped because their sender pilots no ship.</summary>
    public long NotPilotDropped { get; internal set; }

    /// <summary>Gets the slots stepped with the received input.</summary>
    public long Consumed { get; internal set; }
}
