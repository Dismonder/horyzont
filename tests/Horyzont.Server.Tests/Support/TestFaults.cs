using Horyzont.Persistence.Faults;

namespace Horyzont.Server.Tests.Support;

/// <summary>Failure injection for the writer: throws a given exception once at the next hit of a given point.</summary>
internal sealed class TestFaults : IPersistenceFaults
{
    private readonly object _gate = new();
    private FaultPoint _point;
    private Exception? _next;
    private FaultPoint _pausePoint;
    private ManualResetEventSlim? _release;

    /// <summary>Process death at the next hit of <paramref name="point"/>.</summary>
    public void CrashAt(FaultPoint point) => ThrowAt(point, new SimulatedCrashException(point));

    /// <summary>A disk error at the next hit of <paramref name="point"/>.</summary>
    public void DiskErrorAt(FaultPoint point) => ThrowAt(point, new IOException("disk full (test)"));

    /// <summary>Blocks the writer thread at the next hit of <paramref name="point"/> until <paramref name="release"/> is set (a hanging disk).</summary>
    public void PauseAt(FaultPoint point, ManualResetEventSlim release)
    {
        lock (_gate)
        {
            _pausePoint = point;
            _release = release;
        }
    }

    public void Hit(FaultPoint point)
    {
        ManualResetEventSlim? pause = null;
        lock (_gate)
        {
            if (_release is not null && _pausePoint == point)
            {
                pause = _release;
                _release = null;
            }
        }

        pause?.Wait();
        Exception? toThrow = null;
        lock (_gate)
        {
            if (_next is not null && _point == point)
            {
                toThrow = _next;
                _next = null;
            }
        }

        if (toThrow is not null)
        {
            throw toThrow;
        }
    }

    private void ThrowAt(FaultPoint point, Exception exception)
    {
        lock (_gate)
        {
            _point = point;
            _next = exception;
        }
    }
}
