using Horyzont.Persistence.Faults;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>Pauses the writer thread at the <c>hit</c>-th hit of a fault point until the test releases it.</summary>
internal sealed class PausingFaults(FaultPoint point, int hit) : IPersistenceFaults, IDisposable
{
    private int _hits;

    /// <summary>Set when the writer thread is paused at the point.</summary>
    public ManualResetEventSlim Reached { get; } = new();

    /// <summary>Set by the test to let the writer thread go on.</summary>
    public ManualResetEventSlim Release { get; } = new();

    public void Hit(FaultPoint reached)
    {
        if (reached == point && Interlocked.Increment(ref _hits) == hit)
        {
            Reached.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(60)))
            {
                throw new TimeoutException("The test did not release the paused writer.");
            }
        }
    }

    public void Dispose()
    {
        Release.Set();
        Reached.Dispose();
        Release.Dispose();
    }
}
