using System.Diagnostics;

namespace Horyzont.Game.Constructs;

/// <summary>Main-thread time spent applying mesher results, for frame-time diagnostics (engine tests print it); one instance per <see cref="ConstructWorld"/>, touched only on the main thread. Cheap: two timestamps per unit.</summary>
public sealed class ApplyTimings
{
    /// <summary>Milliseconds spent in mesh uploads and collision point copies.</summary>
    public double UploadMilliseconds { get; private set; }

    /// <summary>Milliseconds spent building collision shapes.</summary>
    public double CollisionMilliseconds { get; private set; }

    /// <summary>Longest single upload unit in milliseconds.</summary>
    public double MaxUploadMilliseconds { get; private set; }

    /// <summary>Longest single collision unit in milliseconds.</summary>
    public double MaxCollisionMilliseconds { get; private set; }

    /// <summary>Number of upload units.</summary>
    public int Uploads { get; private set; }

    /// <summary>Number of collision units.</summary>
    public int Collisions { get; private set; }

    /// <summary>Clears the counters.</summary>
    public void Reset()
    {
        UploadMilliseconds = CollisionMilliseconds = MaxUploadMilliseconds = MaxCollisionMilliseconds = 0;
        Uploads = Collisions = 0;
    }

    internal void AddUpload(long started)
    {
        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        UploadMilliseconds += ms;
        MaxUploadMilliseconds = Math.Max(MaxUploadMilliseconds, ms);
        Uploads++;
    }

    internal void AddCollision(long started)
    {
        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        CollisionMilliseconds += ms;
        MaxCollisionMilliseconds = Math.Max(MaxCollisionMilliseconds, ms);
        Collisions++;
    }
}
