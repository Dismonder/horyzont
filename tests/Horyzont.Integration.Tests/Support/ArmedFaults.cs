using Horyzont.Persistence.Faults;

namespace Horyzont.Integration.Tests.Support;

/// <summary>Simulates process death at the first hit of a fault point after <see cref="Arm"/> (called by the writer thread).</summary>
internal sealed class ArmedFaults : IPersistenceFaults
{
    private int _armed = -1;

    public bool Crashed { get; private set; }

    public void Arm(FaultPoint point) => Volatile.Write(ref _armed, (int)point);

    public void Hit(FaultPoint point)
    {
        if (Volatile.Read(ref _armed) == (int)point)
        {
            Crashed = true;
            throw new SimulatedCrashException(point);
        }
    }
}
