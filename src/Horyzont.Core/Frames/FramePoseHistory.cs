using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>
/// World poses of moving frames over the last <see cref="HistoryTicks"/> ticks (500 ms at 60 Hz, K5-20 decision 9): a fixed number
/// of frame slots, each a ring of samples. Lookups interpolate position linearly and rotation spherically between recorded ticks.
/// Allocation-free after construction. Main thread only.
/// </summary>
public sealed class FramePoseHistory : IFrameHistoryView
{
    /// <summary>Samples per frame: 500 ms at 60 Hz plus one.</summary>
    public const int HistoryTicks = 31;

    /// <summary>Default number of frame slots.</summary>
    public const int DefaultCapacity = 64;

    private readonly int _samples;
    private readonly Dictionary<FrameRef, int> _slots;
    private readonly int[] _free;
    private readonly ulong[] _ticks;
    private readonly PoseD[] _poses;
    private readonly int[] _head;
    private readonly int[] _count;
    private int _freeCount;

    /// <summary>Creates an empty history.</summary>
    /// <param name="capacity">Number of frame slots.</param>
    /// <param name="samples">Samples kept per frame (the server keeps <see cref="HistoryTicks"/>; a client covers its interpolation delay).</param>
    public FramePoseHistory(int capacity = DefaultCapacity, int samples = HistoryTicks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);
        _samples = samples;
        Capacity = capacity;
        _slots = new Dictionary<FrameRef, int>(capacity);
        _free = new int[capacity];
        _ticks = new ulong[capacity * _samples];
        _poses = new PoseD[capacity * _samples];
        _head = new int[capacity];
        _count = new int[capacity];
        for (var i = 0; i < capacity; i++)
        {
            _free[i] = capacity - 1 - i;
        }

        _freeCount = capacity;
    }

    /// <summary>Gets the number of samples kept per frame.</summary>
    public int Samples => _samples;

    /// <summary>Gets the number of frame slots.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of recorded frames.</summary>
    public int Count => _slots.Count;

    /// <summary>True when the frame has samples.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>Whether the frame is recorded.</returns>
    public bool Contains(FrameRef frame) => _slots.ContainsKey(frame);

    /// <summary>Records the world pose of a frame at a tick; the same tick as the newest sample replaces it.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="worldPose">The world pose.</param>
    /// <returns>False for a tick older than the newest sample or a new frame when every slot is taken.</returns>
    public bool Record(Tick tick, FrameRef frame, in PoseD worldPose)
    {
        if (!_slots.TryGetValue(frame, out var slot))
        {
            if (_freeCount == 0)
            {
                return false;
            }

            slot = _free[--_freeCount];
            _slots.Add(frame, slot);
            _head[slot] = 0;
            _count[slot] = 0;
        }

        var start = slot * _samples;
        if (_count[slot] > 0)
        {
            var newest = start + ((_head[slot] + _samples - 1) % _samples);
            if (tick.Value < _ticks[newest])
            {
                return false;
            }

            if (tick.Value == _ticks[newest])
            {
                _poses[newest] = worldPose;
                return true;
            }
        }

        var index = start + _head[slot];
        _ticks[index] = tick.Value;
        _poses[index] = worldPose;
        _head[slot] = (_head[slot] + 1) % _samples;
        _count[slot] = Math.Min(_count[slot] + 1, _samples);
        return true;
    }

    /// <inheritdoc />
    public bool TryGetWorldPose(FrameRef frame, double tick, out PoseD pose)
    {
        pose = PoseD.Identity;
        if (!_slots.TryGetValue(frame, out var slot) || _count[slot] == 0 || !double.IsFinite(tick))
        {
            return false;
        }

        // Walk from the newest sample back to the oldest one.
        var start = slot * _samples;
        var count = _count[slot];
        var later = -1;
        for (var k = 1; k <= count; k++)
        {
            var index = start + ((_head[slot] + _samples - k) % _samples);
            var sampleTick = (double)_ticks[index];
            if (sampleTick == tick)
            {
                pose = _poses[index];
                return true;
            }

            if (sampleTick < tick)
            {
                if (later < 0)
                {
                    return false;
                }

                var laterTick = (double)_ticks[later];
                var t = (tick - sampleTick) / (laterTick - sampleTick);
                pose = new PoseD(
                    Vector3D.Lerp(_poses[index].Position, _poses[later].Position, t),
                    QuaternionD.Slerp(_poses[index].Rotation, _poses[later].Rotation, t));
                return true;
            }

            later = index;
        }

        return false;
    }

    /// <summary>Drops every sample of a frame and frees its slot.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>False when the frame was not recorded.</returns>
    public bool Forget(FrameRef frame)
    {
        if (!_slots.Remove(frame, out var slot))
        {
            return false;
        }

        _free[_freeCount++] = slot;
        return true;
    }

    /// <summary>Drops every frame.</summary>
    public void Clear()
    {
        foreach (var slot in _slots.Values)
        {
            _free[_freeCount++] = slot;
        }

        _slots.Clear();
    }
}
