namespace Horyzont.Persistence.Faults;

/// <summary>Production implementation of <see cref="IPersistenceFaults"/>: never fails.</summary>
public sealed class NoFaults : IPersistenceFaults
{
    /// <summary>The shared instance.</summary>
    public static NoFaults Instance { get; } = new();

    /// <inheritdoc />
    public void Hit(FaultPoint point)
    {
    }
}
