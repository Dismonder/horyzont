using Horyzont.Persistence.Faults;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>Fails the n-th hit of chosen fault points; counts every hit. Called only by the writer thread.</summary>
internal sealed class ScriptedFaults : IPersistenceFaults
{
    private readonly Dictionary<FaultPoint, (int Hit, Func<FaultPoint, Exception> Error)> _rules = [];
    private readonly int[] _hits = new int[Enum.GetValues<FaultPoint>().Length];
    private volatile bool _crashed;

    public bool Crashed => _crashed;

    public static ScriptedFaults None => new();

    /// <summary>Simulated process death at the <paramref name="hit"/>-th hit of <paramref name="point"/>.</summary>
    public static ScriptedFaults CrashAt(FaultPoint point, int hit = 1) => new ScriptedFaults().Fail(point, hit, p => new SimulatedCrashException(p));

    /// <summary>Simulated disk error at the <paramref name="hit"/>-th hit of <paramref name="point"/>.</summary>
    public static ScriptedFaults DiskErrorAt(FaultPoint point, int hit = 1) =>
        new ScriptedFaults().Fail(point, hit, p => new IOException($"Simulated disk error at {p}."));

    public ScriptedFaults Fail(FaultPoint point, int hit, Func<FaultPoint, Exception> error)
    {
        _rules[point] = (hit, error);
        return this;
    }

    public int Hits(FaultPoint point) => Volatile.Read(ref _hits[(int)point]);

    public void Hit(FaultPoint point)
    {
        var count = Interlocked.Increment(ref _hits[(int)point]);
        if (_rules.TryGetValue(point, out var rule) && rule.Hit == count)
        {
            var error = rule.Error(point);
            if (error is SimulatedCrashException)
            {
                _crashed = true;
            }

            throw error;
        }
    }
}
