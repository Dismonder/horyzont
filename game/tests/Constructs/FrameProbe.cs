using System.Diagnostics;

using Godot;

namespace Horyzont.Game.Tests.Constructs;

/// <summary>Records the time between consecutive <c>_Process</c> calls.</summary>
internal sealed partial class FrameProbe : Node
{
    private long _last;

    public List<double> Intervals { get; } = [];

    public void Reset()
    {
        Intervals.Clear();
        _last = 0;
    }

    public override void _Process(double delta)
    {
        var now = Stopwatch.GetTimestamp();
        if (_last != 0)
        {
            Intervals.Add(Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds);
        }

        _last = now;
    }
}
