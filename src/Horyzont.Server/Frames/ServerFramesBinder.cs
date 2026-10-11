using Horyzont.Core.Frames;

namespace Horyzont.Server.Frames;

/// <summary>
/// Registers the server frames with a runtime (K5-20): the frame graph over the loaded world, the 500 ms frame history (first
/// participant of the <see cref="ServerTickPhase.Compose"/> phase) and the transitions, created with the world on the first tick.
/// </summary>
/// <param name="placement">Planet poses of a loaded world; by default the first planet of the world is the home planet in identity.</param>
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

    /// <summary>Registers the binder as a tick participant.</summary>
    /// <param name="runtime">The runtime.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.AddTickParticipant(this);
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (_history is null)
        {
            var state = runtime.State;
            var graph = new FrameGraph(new WorldStateFramePoses(state, (placement ?? HomeFirst)(state)));
            _history = new ServerFrameHistory(state, graph);
            _transitions = new FrameTransitions(runtime.Simulation, _history);
        }

        _history.OnTick(runtime);
    }

    private static HomeOnlyPlacement HomeFirst(Horyzont.Core.World.WorldState state) =>
        new HomeOnlyPlacement(state.Planets.All.Count > 0 ? state.Planets.All[0].Id : default);
}

/// <summary>Chooses the planet placement of a loaded world.</summary>
/// <param name="state">The world.</param>
/// <returns>The placement.</returns>
public delegate IPlanetPlacement WorldStatePlacement(Horyzont.Core.World.WorldState state);
