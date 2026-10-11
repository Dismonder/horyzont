using Horyzont.Core.Frames;
using Horyzont.Core.Space;

namespace Horyzont.Server.Frames;

/// <summary>
/// Registers the server frames with a runtime (K5-20): the frame graph over the loaded world, the 500 ms frame history (first
/// participant of the <see cref="ServerTickPhase.Compose"/> phase) and the transitions, created with the world on the first tick.
/// </summary>
/// <param name="placement">Planet poses of a loaded world; by default <see cref="WorldStatePlanetPlacement"/> (the star system of the world).</param>
public sealed class ServerFramesBinder(WorldStatePlacement? placement = null) : IServerTickParticipant
{
    private ServerFrameHistory? _history;
    private FrameTransitions? _transitions;

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Compose;

    /// <summary>Gets the frame history; null before the world runs.</summary>
    public ServerFrameHistory? History => _history;

    /// <summary>Gets the frame transitions; null before the world runs.</summary>
    public FrameTransitions? Transitions => _transitions;

    /// <summary>Gets the frame graph of the world; null before the world runs.</summary>
    public FrameGraph? Graph => _transitions?.Graph;

    /// <summary>Registers the binder as a tick participant.</summary>
    /// <param name="runtime">The runtime.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.AddTickParticipant(this);
    }

    /// <summary>
    /// Creates the frames with the running world when they do not exist yet (participants of an earlier phase, such as the server
    /// characters, reach the graph before the first compose step).
    /// </summary>
    /// <param name="runtime">The runtime whose world runs.</param>
    /// <returns>The frame graph.</returns>
    public FrameGraph EnsureWorld(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (_transitions is null)
        {
            var state = runtime.State;
            var graph = new FrameGraph(new WorldStateFramePoses(state, (placement ?? DefaultPlacement)(state)));
            _history = new ServerFrameHistory(state, graph);
            _transitions = new FrameTransitions(runtime.Simulation, _history);
        }

        return _transitions.Graph;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        EnsureWorld(runtime);
        _history!.OnTick(runtime);
    }

    // K5-21 decision 15: the star system of the world (home planet only when it has none).
    private static WorldStatePlanetPlacement DefaultPlacement(Horyzont.Core.World.WorldState state) => new(state);
}

/// <summary>Chooses the planet placement of a loaded world.</summary>
/// <param name="state">The world.</param>
/// <returns>The placement.</returns>
public delegate IPlanetPlacement WorldStatePlacement(Horyzont.Core.World.WorldState state);
