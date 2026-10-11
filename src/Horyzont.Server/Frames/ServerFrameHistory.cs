using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Server.Frames;

/// <summary>
/// The server's 500 ms history of moving frames (K5-20 decision 9): every tick, in the <see cref="ServerTickPhase.Compose"/> phase
/// (first in it), records the world pose of every ship-mode construct (from an index rebuilt only when constructs are added or removed) and forgets constructs that are gone or static again. Planet
/// frames do not move in K5-20 and come from the frame graph. Allocation-free in steady state.
/// </summary>
public sealed class ServerFrameHistory : IServerTickParticipant
{
    private readonly WorldState _state;
    private readonly FrameGraph _graph;
    private readonly FramePoseHistory _history;
    private readonly List<FrameRef> _tracked;
    private readonly List<Construct> _ships;
    private readonly int _capacity;
    private ulong _shipsVersion = ulong.MaxValue;
    private bool _hasTick;

    /// <summary>Creates the history.</summary>
    /// <param name="state">World state.</param>
    /// <param name="graph">Frame graph over the state.</param>
    /// <param name="capacity">Number of ship frames recorded at most.</param>
    public ServerFrameHistory(WorldState state, FrameGraph graph, int capacity = FramePoseHistory.DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(graph);
        _state = state;
        _graph = graph;
        _history = new FramePoseHistory(capacity);
        _tracked = new List<FrameRef>(capacity);
        _ships = new List<Construct>(capacity);
        _capacity = capacity;
    }

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Compose;

    /// <summary>Gets the recorded world poses.</summary>
    public IFrameHistoryView History => _history;

    /// <summary>Gets the frame graph the poses come from.</summary>
    public FrameGraph Graph => _graph;

    /// <summary>Gets the number of frames recorded now.</summary>
    public int Count => _history.Count;

    /// <summary>Gets the newest recorded tick (valid when <see cref="HasTick"/>).</summary>
    public Tick LastTick { get; private set; }

    /// <summary>Gets a value indicating whether a tick was recorded.</summary>
    public bool HasTick => _hasTick;

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step(runtime.State.CurrentTick);
    }

    /// <summary>Records the world poses of the ship constructs at a tick.</summary>
    /// <param name="tick">The tick (the current tick of the simulation).</param>
    public void Step(Tick tick)
    {
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            var frame = _tracked[i];
            if (!_state.Constructs.TryGet(new EntityId(frame.Id), out var construct) || construct.Mode != ConstructMode.Ship)
            {
                _history.Forget(frame);
                _tracked.RemoveAt(i);
            }
        }

        RefreshShips();
        for (var i = 0; i < _ships.Count; i++)
        {
            var construct = _ships[i];
            var frame = FrameRef.Of(construct.Id);
            if (!_graph.TryGetWorldPose(frame, out var pose))
            {
                continue;
            }

            var known = _history.Contains(frame);
            if (_history.Record(tick, frame, pose) && !known)
            {
                _tracked.Add(frame);
            }
        }

        LastTick = tick;
        _hasTick = true;
    }

    /// <summary>
    /// The ship-mode constructs, rebuilt only when a construct was added or removed (the mode of a construct object never changes), so
    /// a tick does not scan every static construct. Capped at the capacity of the history: a ship beyond it could not be recorded
    /// anyway, and a slot frees only when a ship is removed, which rebuilds the index.
    /// </summary>
    private void RefreshShips()
    {
        var store = _state.Constructs;
        if (store.StructureVersion == _shipsVersion)
        {
            return;
        }

        _shipsVersion = store.StructureVersion;
        _ships.Clear();
        var all = store.All;
        for (var i = 0; i < all.Count && _ships.Count < _capacity; i++)
        {
            if (all[i].Mode == ConstructMode.Ship)
            {
                _ships.Add(all[i]);
            }
        }
    }

    /// <summary>
    /// World velocity of a point fixed in a frame, by finite difference of the two newest recorded ticks. Frames without two samples
    /// (planets, static constructs, a ship recorded once) give zero.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="point">The point in the frame.</param>
    /// <param name="tickSeconds">Length of a tick.</param>
    /// <returns>The velocity in the world (system frame) axes.</returns>
    public Vector3D PointVelocity(FrameRef frame, Vector3D point, double tickSeconds)
    {
        if (!_hasTick || LastTick.Value == 0
            || !_history.TryGetWorldPose(frame, LastTick.Value, out var now)
            || !_history.TryGetWorldPose(frame, LastTick.Value - 1, out var before))
        {
            return Vector3D.Zero;
        }

        return (FrameMath.TransformPoint(now, point) - FrameMath.TransformPoint(before, point)) / tickSeconds;
    }
}
