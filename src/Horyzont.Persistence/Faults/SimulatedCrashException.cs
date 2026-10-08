namespace Horyzont.Persistence.Faults;

/// <summary>
/// Thrown by a test <see cref="IPersistenceFaults"/> to simulate process death: the writer thread stops at once without any
/// further write and the open transaction is rolled back when the connection closes, as after a real crash in WAL mode.
/// </summary>
/// <param name="point">Where the crash happened.</param>
public sealed class SimulatedCrashException(FaultPoint point) : Exception($"Simulated crash at {point}.")
{
    /// <summary>Where the crash happened.</summary>
    public FaultPoint Point { get; } = point;
}
