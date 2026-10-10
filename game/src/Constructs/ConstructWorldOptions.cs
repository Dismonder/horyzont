namespace Horyzont.Game.Constructs;

/// <summary>Settings of a <see cref="ConstructWorld"/>.</summary>
public sealed record ConstructWorldOptions
{
    /// <summary>What to build; <see cref="ConstructWorldMode.CollisionOnly"/> for the dedicated server.</summary>
    public ConstructWorldMode Mode { get; init; } = ConstructWorldMode.RenderAndCollision;

    /// <summary>Mesher threads.</summary>
    public int WorkerCount { get; init; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);

    /// <summary>Bound of the job and result queues (jobs started and not yet applied).</summary>
    public int MaxInFlightJobs { get; init; } = 8;

    /// <summary>Time budget of applying results per frame; at least one result is applied each frame.</summary>
    public double MaxApplyMillisecondsPerFrame { get; init; } = 4;

    /// <summary>Constructs closer than this to a viewer are rendered.</summary>
    public float RenderRadiusMeters { get; init; } = 1000;

    /// <summary>Constructs closer than this to a viewer get collision bodies.</summary>
    public float CollisionRadiusMeters { get; init; } = 128;

    /// <summary>A loaded construct is unloaded only after it is this much farther than its radius.</summary>
    public float HysteresisMeters { get; init; } = 64;

    /// <summary>Explicit tools mode: load all constructs when there are no viewers.</summary>
    public bool LoadAllWithoutViewers { get; init; }

    /// <summary>Clock used for worker shutdown deadlines.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>How long <see cref="ConstructWorld.ShutdownAsync"/> waits for the workers.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
