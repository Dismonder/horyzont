namespace Horyzont.Server.Space;

/// <summary>Settings of <see cref="PlanetActivityPolicy"/> (K5-22 decision 2, P4).</summary>
public sealed record PlanetActivityOptions
{
    /// <summary>Gets the ticks between two evaluations of the demand (30 = 0.5 s at 60 Hz).</summary>
    public int EvaluationIntervalTicks { get; init; } = 30;

    /// <summary>Gets the ticks a planet without demand stays active (3600 = 60 s at 60 Hz, spec 02:131).</summary>
    public int LingerTicks { get; init; } = 3600;
}
