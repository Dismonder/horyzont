using System.Diagnostics;

namespace Horyzont.Game.Constructs;

/// <summary>Main-thread time spent applying mesher results, for frame-time diagnostics (engine tests print it). Cheap: two timestamps per unit.</summary>
public static class ApplyTimings
{
    /// <summary>Milliseconds spent in mesh uploads and collision point copies.</summary>
    public static double UploadMilliseconds { get; private set; }

    /// <summary>Milliseconds spent building collision shapes.</summary>
    public static double CollisionMilliseconds { get; private set; }

    /// <summary>Longest single upload unit in milliseconds.</summary>
    public static double MaxUploadMilliseconds { get; private set; }

    /// <summary>Longest single collision unit in milliseconds.</summary>
    public static double MaxCollisionMilliseconds { get; private set; }

    /// <summary>Number of upload units.</summary>
    public static int Uploads { get; private set; }

    /// <summary>Number of collision units.</summary>
    public static int Collisions { get; private set; }

    /// <summary>Clears the counters.</summary>
    public static void Reset()
    {
        UploadMilliseconds = CollisionMilliseconds = MaxUploadMilliseconds = MaxCollisionMilliseconds = 0;
        Uploads = Collisions = 0;
    }

    internal static void AddUpload(long started)
    {
        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        UploadMilliseconds += ms;
        MaxUploadMilliseconds = Math.Max(MaxUploadMilliseconds, ms);
        Uploads++;
    }

    internal static void AddCollision(long started)
    {
        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        CollisionMilliseconds += ms;
        MaxCollisionMilliseconds = Math.Max(MaxCollisionMilliseconds, ms);
        Collisions++;
    }
}
