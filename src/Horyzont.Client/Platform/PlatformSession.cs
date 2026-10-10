using Horyzont.Core.Ids;

namespace Horyzont.Client.Platform;

/// <summary>Orders idempotent session notifications and gates process cleanup on successful transport release.</summary>
public sealed class PlatformSession
{
    private readonly ICompositionPlatform _platform;
    private bool _worldStarted;
    private bool _shutdownRequested;
    private bool _transportsReleased;
    private bool _processCleaned;

    /// <summary>Initializes the lifecycle of one session.</summary>
    /// <param name="platform">Platform notifications.</param>
    public PlatformSession(ICompositionPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <summary>Notifies the platform of a successful writable world start once.</summary>
    /// <param name="world">World.</param>
    /// <param name="capacity">Player capacity.</param>
    /// <param name="passwordRequired">Whether a password is required.</param>
    public void OnWorldStarted(WorldId world, int capacity, bool passwordRequired)
    {
        if (_worldStarted || _shutdownRequested || _transportsReleased)
        {
            return;
        }

        _worldStarted = true;
        _platform.OnWorldStarted(world, capacity, passwordRequired);
    }

    /// <summary>Notifies shutdown once, before the server or client is asked to stop.</summary>
    public void OnShutdownRequested()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _platform.OnShutdownRequested();
    }

    /// <summary>Notifies completed successful transport disposal once; a timeout or fault must not call this.</summary>
    public void OnTransportsReleased()
    {
        if (_transportsReleased)
        {
            return;
        }

        _transportsReleased = true;
        _platform.OnTransportsReleased();
    }

    /// <summary>Retries process cleanup only after successful transport disposal; stops after success.</summary>
    /// <param name="cleanup">Process cleanup, absent for K2.</param>
    /// <returns>True when successful cleanup is complete.</returns>
    public bool TryProcessCleanup(Func<bool>? cleanup)
    {
        if (!_transportsReleased)
        {
            return false;
        }

        _processCleaned = _processCleaned || (cleanup?.Invoke() ?? true);
        return _processCleaned;
    }
}
