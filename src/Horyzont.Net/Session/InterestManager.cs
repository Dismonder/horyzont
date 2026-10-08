using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Net.Session;

/// <summary>
/// Which subjects each player sees. A subject enters when it is in the viewer's region and
/// <c>distance − Radius ≤ ViewRange</c>, and leaves above <c>ViewRange + hysteresis</c> or when the regions differ.
/// Subjects are bucketed in a spatial hash with 64 m cells in planet regions and 2048 m cells in system regions;
/// in the steady state <see cref="Update"/> does not allocate.
/// </summary>
public sealed class InterestManager
{
    private const float PlanetCellSize = 64f;
    private const float SystemCellSize = 2048f;

    private readonly float _hysteresis;
    private readonly Dictionary<CellKey, int> _cellHeads = [];
    private readonly List<CellKey> _occupiedCells = [];
    private readonly Dictionary<FrameRef, float> _regionReach = [];
    private readonly Dictionary<PlayerId, ViewerState> _viewers = [];
    private readonly Stack<ViewerState> _viewerPool = new();
    private readonly List<PlayerId> _staleViewers = [];
    private InterestSubject[] _subjects = [];
    private int[] _nextInCell = [];
    private int _subjectCount;

    /// <summary>Initializes a new instance of the <see cref="InterestManager"/> class.</summary>
    /// <param name="hysteresis">Extra distance before a visible subject leaves, in metres.</param>
    public InterestManager(float hysteresis)
    {
        if (!float.IsFinite(hysteresis) || hysteresis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hysteresis), hysteresis, "Hysteresis must be finite and not negative.");
        }

        _hysteresis = hysteresis;
    }

    /// <summary>
    /// Recomputes the visible set of every viewer. Viewers missing from <paramref name="viewers"/> are forgotten;
    /// subjects with non-finite positions or ranges are ignored.
    /// </summary>
    /// <param name="subjects">All subjects.</param>
    /// <param name="viewers">All viewers.</param>
    public void Update(ReadOnlySpan<InterestSubject> subjects, ReadOnlySpan<InterestViewer> viewers)
    {
        BuildGrid(subjects);

        foreach (var state in _viewers.Values)
        {
            state.Seen = false;
        }

        foreach (var viewer in viewers)
        {
            if (!_viewers.TryGetValue(viewer.Player, out var state))
            {
                state = _viewerPool.Count > 0 ? _viewerPool.Pop() : new ViewerState();
                _viewers.Add(viewer.Player, state);
            }

            state.Seen = true;
            ComputeVisible(viewer, state);
        }

        _staleViewers.Clear();
        foreach (var pair in _viewers)
        {
            if (!pair.Value.Seen)
            {
                _staleViewers.Add(pair.Key);
            }
        }

        foreach (var player in _staleViewers)
        {
            var state = _viewers[player];
            _viewers.Remove(player);
            state.Reset();
            _viewerPool.Push(state);
        }
    }

    /// <summary>
    /// Fills the subjects that entered and left the viewer's set since the previous call for that viewer.
    /// Both lists are cleared first.
    /// </summary>
    /// <param name="viewer">The viewer.</param>
    /// <param name="entered">Receives entered subjects.</param>
    /// <param name="left">Receives subjects that left.</param>
    public void GetChanges(PlayerId viewer, List<EntityId> entered, List<EntityId> left)
    {
        ArgumentNullException.ThrowIfNull(entered);
        ArgumentNullException.ThrowIfNull(left);
        entered.Clear();
        left.Clear();
        if (!_viewers.TryGetValue(viewer, out var state))
        {
            return;
        }

        foreach (var entity in state.Visible)
        {
            if (!state.Reported.Contains(entity))
            {
                entered.Add(entity);
            }
        }

        foreach (var entity in state.Reported)
        {
            if (!state.Visible.Contains(entity))
            {
                left.Add(entity);
            }
        }

        state.Reported.Clear();
        foreach (var entity in state.Visible)
        {
            state.Reported.Add(entity);
        }
    }

    /// <summary>Whether <paramref name="subject"/> is in the viewer's set after the last <see cref="Update"/>.</summary>
    /// <param name="viewer">The viewer.</param>
    /// <param name="subject">The subject.</param>
    /// <returns>True when visible.</returns>
    public bool IsVisible(PlayerId viewer, EntityId subject) =>
        _viewers.TryGetValue(viewer, out var state) && state.Visible.Contains(subject);

    private static float CellSize(FrameRef region) => region.Kind == FrameKind.System ? SystemCellSize : PlanetCellSize;

    private static int CellIndex(float coordinate, float cellSize) =>
        (int)Math.Clamp(Math.Floor((double)coordinate / cellSize), int.MinValue, int.MaxValue);

    private static bool IsUsable(in InterestSubject subject) =>
        float.IsFinite(subject.Position.X) && float.IsFinite(subject.Position.Y) && float.IsFinite(subject.Position.Z)
        && float.IsFinite(subject.Radius) && float.IsFinite(subject.ViewRange);

    private void BuildGrid(ReadOnlySpan<InterestSubject> subjects)
    {
        _cellHeads.Clear();
        _occupiedCells.Clear();
        _regionReach.Clear();
        if (_subjects.Length < subjects.Length)
        {
            var capacity = Math.Max(subjects.Length, _subjects.Length * 2);
            _subjects = new InterestSubject[capacity];
            _nextInCell = new int[capacity];
        }

        subjects.CopyTo(_subjects);
        _subjectCount = subjects.Length;
        for (var i = 0; i < _subjectCount; i++)
        {
            ref readonly var subject = ref _subjects[i];
            if (!IsUsable(subject))
            {
                continue;
            }

            var size = CellSize(subject.Region);
            var key = new CellKey(
                subject.Region, CellIndex(subject.Position.X, size), CellIndex(subject.Position.Y, size), CellIndex(subject.Position.Z, size));
            if (_cellHeads.TryGetValue(key, out var head))
            {
                _nextInCell[i] = head;
            }
            else
            {
                _nextInCell[i] = -1;
                _occupiedCells.Add(key);
            }

            _cellHeads[key] = i;
            var reach = Math.Max(0, subject.ViewRange) + Math.Max(0, subject.Radius);
            if (!_regionReach.TryGetValue(subject.Region, out var current) || reach > current)
            {
                _regionReach[subject.Region] = reach;
            }
        }
    }

    private void ComputeVisible(in InterestViewer viewer, ViewerState state)
    {
        state.Next.Clear();
        var position = viewer.Position;
        if (_regionReach.TryGetValue(viewer.Region, out var reach)
            && float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z))
        {
            var size = CellSize(viewer.Region);
            var range = reach + _hysteresis;
            int minX = CellIndex(position.X - range, size), maxX = CellIndex(position.X + range, size);
            int minY = CellIndex(position.Y - range, size), maxY = CellIndex(position.Y + range, size);
            int minZ = CellIndex(position.Z - range, size), maxZ = CellIndex(position.Z + range, size);
            var cubeCells = ((double)maxX - minX + 1) * ((double)maxY - minY + 1) * ((double)maxZ - minZ + 1);
            if (cubeCells > _occupiedCells.Count)
            {
                foreach (var key in _occupiedCells)
                {
                    if (key.Region == viewer.Region && key.X >= minX && key.X <= maxX && key.Y >= minY && key.Y <= maxY
                        && key.Z >= minZ && key.Z <= maxZ)
                    {
                        VisitCell(_cellHeads[key], position, state);
                    }
                }
            }
            else
            {
                for (long x = minX; x <= maxX; x++)
                {
                    for (long y = minY; y <= maxY; y++)
                    {
                        for (long z = minZ; z <= maxZ; z++)
                        {
                            if (_cellHeads.TryGetValue(new CellKey(viewer.Region, (int)x, (int)y, (int)z), out var head))
                            {
                                VisitCell(head, position, state);
                            }
                        }
                    }
                }
            }
        }

        (state.Visible, state.Next) = (state.Next, state.Visible);
    }

    private void VisitCell(int head, Vector3 viewerPosition, ViewerState state)
    {
        for (var i = head; i >= 0; i = _nextInCell[i])
        {
            ref readonly var subject = ref _subjects[i];
            var distance = Vector3.Distance(viewerPosition, subject.Position) - subject.Radius;
            var limit = state.Visible.Contains(subject.Entity) ? subject.ViewRange + _hysteresis : subject.ViewRange;
            if (distance <= limit)
            {
                state.Next.Add(subject.Entity);
            }
        }
    }

    private readonly record struct CellKey(FrameRef Region, int X, int Y, int Z);

    private sealed class ViewerState
    {
        public HashSet<EntityId> Visible { get; set; } = [];

        public HashSet<EntityId> Next { get; set; } = [];

        public HashSet<EntityId> Reported { get; } = [];

        public bool Seen { get; set; }

        public void Reset()
        {
            Visible.Clear();
            Next.Clear();
            Reported.Clear();
            Seen = false;
        }
    }
}
