namespace Horyzont.Persistence.Faults;

/// <summary>
/// Failure injection for tests. Production uses <see cref="NoFaults"/>. A test implementation throws
/// <see cref="SimulatedCrashException"/> (process death) or <see cref="IOException"/> (disk error).
/// </summary>
public interface IPersistenceFaults
{
    /// <summary>Called by the writer thread when it reaches <paramref name="point"/>.</summary>
    /// <param name="point">The point.</param>
    void Hit(FaultPoint point);
}
