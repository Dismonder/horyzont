namespace Horyzont.Server;

/// <summary>Life cycle of a <see cref="ServerRuntime"/>.</summary>
public enum ServerRuntimeState : byte
{
    /// <summary>Constructed, <see cref="ServerRuntime.StartAsync"/> not called yet.</summary>
    Created = 0,

    /// <summary>Loading the world, replaying the journal and writing the startup checkpoint.</summary>
    Starting = 1,

    /// <summary>Accepting players and intents.</summary>
    Running = 2,

    /// <summary>Alive but refusing changes: a write error, or a journal tail that could not be replayed.</summary>
    SafeMode = 3,

    /// <summary>Running the <see cref="ShutdownSequence"/>.</summary>
    ShuttingDown = 4,

    /// <summary>The shutdown finished or the world did not start; nothing is open any more.</summary>
    Stopped = 5,

    /// <summary>A simulation or internal error stopped the server; the process exits with code 70.</summary>
    Faulted = 6,
}
