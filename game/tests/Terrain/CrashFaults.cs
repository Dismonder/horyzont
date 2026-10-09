using Horyzont.Persistence.Faults;

namespace Horyzont.Game.Tests.Terrain;

/// <summary>Simulated process death at the n-th hit of one fault point (writer thread).</summary>
internal sealed class CrashFaults(FaultPoint point, int hit) : IPersistenceFaults
{
    private int _hits;
    private volatile bool _crashed;

    public bool Crashed => _crashed;

    public void Hit(FaultPoint at)
    {
        if (at == point && Interlocked.Increment(ref _hits) == hit)
        {
            _crashed = true;
            throw new SimulatedCrashException(at);
        }
    }
}
