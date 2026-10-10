using System.Numerics;

using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Terrain;

/// <summary>
/// Turns the viewers of the local player into <see cref="TerrainView"/> reports: at most five per second, and only when the block of a
/// viewer or a distance changed (plus the first report of a session, which is not delayed). Sequences increase for the session.
/// </summary>
/// <param name="time">Clock of the rate limit.</param>
public sealed class TerrainViewReporter(TimeProvider time)
{
    private static readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(200);

    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private TerrainViewers _last;
    private long _lastAt;
    private uint _sequence;

    /// <summary>Forgets the session; the next report is the first of a new one.</summary>
    public void Reset()
    {
        _sequence = 0;
        _last = default;
    }

    /// <summary>Creates the next report when one is due.</summary>
    /// <param name="viewers">Current viewers.</param>
    /// <param name="view">The report when due.</param>
    /// <returns>Whether a report is due.</returns>
    public bool TryCreate(in TerrainViewers viewers, out TerrainView view)
    {
        view = null!;
        if (viewers.Planet.Value == 0 || !IsFinite(viewers.VisualPosition) || !IsFinite(viewers.CollisionPosition))
        {
            return false;
        }

        var now = _time.GetTimestamp();
        if (_sequence != 0)
        {
            if (_time.GetElapsedTime(_lastAt, now) < _minInterval || !Changed(_last, viewers))
            {
                return false;
            }
        }

        _last = viewers;
        _lastAt = now;
        _sequence++;
        view = new TerrainView(
            viewers.Planet, _sequence, viewers.VisualPosition, Math.Min(viewers.VisualDistance, 16384u),
            viewers.CollisionPosition, Math.Min(viewers.CollisionDistance, 16384u));
        return true;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Changed(in TerrainViewers a, in TerrainViewers b) =>
        a.Planet != b.Planet || a.VisualDistance != b.VisualDistance || a.CollisionDistance != b.CollisionDistance
        || BlockOf(a.VisualPosition) != BlockOf(b.VisualPosition) || BlockOf(a.CollisionPosition) != BlockOf(b.CollisionPosition);

    private static (int X, int Y, int Z) BlockOf(Vector3 p) =>
        ((int)MathF.Floor(p.X / 16f), (int)MathF.Floor(p.Y / 16f), (int)MathF.Floor(p.Z / 16f));
}
