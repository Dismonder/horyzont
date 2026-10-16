using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;

namespace Horyzont.Server.Crafting;

/// <summary>
/// Tells a player what the player discovered and which progress keys the world has (K6-32 D11): <see cref="PlayerProgressState"/> when the player
/// is ready (after the container states; items held from before the discoveries existed are discovered first) and after every operation that
/// discovers an item for the player (to the owner only) or grants a progress key (to every player online). Recognizes the operations by
/// <see cref="Horyzont.Core.Operations.OperationProgress"/>, because both are components of compound operations. Main thread only.
/// </summary>
public sealed class ProgressReplication : IServerPlayerObserver
{
    /// <summary>Initializes a new instance of the <see cref="ProgressReplication"/> class.</summary>
    /// <param name="simulation">The simulation of the running world (null before the world has started).</param>
    /// <param name="send">Sends a message to one player.</param>
    public ProgressReplication(Func<ServerSimulation?> simulation, Action<PlayerId, Message> send)
    {
        // K6-32 C4
    }

    /// <summary>Gets the number of <see cref="PlayerProgressState"/> messages sent.</summary>
    public int StatesSent { get; private set; }

    /// <summary>Creates the replication for a runtime that has not started yet and registers it as a player observer.</summary>
    /// <param name="runtime">The runtime.</param>
    /// <returns>The replication.</returns>
    public static ProgressReplication Register(ServerRuntime runtime)
    {
        throw new NotImplementedException("K6-32 C4");
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player)
    {
        throw new NotImplementedException("K6-32 C4");
    }

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        throw new NotImplementedException("K6-32 C4");
    }
}
