namespace Horyzont.Server;

/// <summary>A game system that takes part in the server tick (kinematics, characters, replication); registered with <see cref="ServerRuntime.AddTickParticipant"/>.</summary>
public interface IServerTickParticipant
{
    /// <summary>Gets the phase the participant runs in; <see cref="ServerTickPhase.Characters"/> unless it says otherwise.</summary>
    ServerTickPhase Phase => ServerTickPhase.Characters;

    /// <summary>Called once per tick after the session was polled and before the simulation advances; main thread.</summary>
    /// <param name="runtime">The runtime.</param>
    void OnTick(ServerRuntime runtime);

    /// <summary>Step 3a of the shutdown: players are still online, the session is drained; release what depends on them.</summary>
    /// <param name="runtime">The runtime.</param>
    void OnShutdown(ServerRuntime runtime)
    {
    }
}
