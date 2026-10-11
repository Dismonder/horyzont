using System.Numerics;

using Horyzont.Client.Frames;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;

namespace Horyzont.Client.Movement;

/// <summary>Bounded motion history for one remote entity; never interprets protocol deltas.</summary>
public sealed class RemoteMotionBuffer
{
    /// <summary>Maximum number of full samples.</summary>
    public const int Capacity = 32;
    private readonly RemoteMotionSample[] _samples = new RemoteMotionSample[Capacity];
    private readonly float _flightSpeed;
    private readonly double _tickRate;
    private readonly IFrameHistoryView? _frames;
    private int _head;

    /// <summary>Creates a ring using the session physics rate and maximum permitted developer speed.</summary>
    /// <param name="tickRate">Server ticks per second.</param>
    /// <param name="maxFlightSpeed">Maximum developer flight speed.</param>
    /// <param name="frames">World poses of frames at server ticks; with it a parent change rebases older samples instead of resetting (K5-20).</param>
    public RemoteMotionBuffer(double tickRate = 60, float maxFlightSpeed = 150, IFrameHistoryView? frames = null)
    {
        if (!double.IsFinite(tickRate) || tickRate <= 0) { throw new ArgumentOutOfRangeException(nameof(tickRate)); }
        if (!float.IsFinite(maxFlightSpeed) || maxFlightSpeed < 0) { throw new ArgumentOutOfRangeException(nameof(maxFlightSpeed)); }
        _tickRate = tickRate;
        _flightSpeed = maxFlightSpeed;
        _frames = frames;
    }

    /// <summary>Gets the number of stored samples.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the number of requests beyond the bounded extrapolation horizon.</summary>
    public long SampleUnderruns { get; private set; }

    /// <summary>Gets the number of parent changes that dropped older samples because a frame had no pose at their tick (or no view).</summary>
    public long ReparentSnaps { get; private set; }

    /// <summary>Accepts a newer, finite full sample: a parent change rebases older samples (resets without a frame view), a teleport resets.</summary>
    /// <param name="sample">Reconstructed sample.</param>
    /// <returns>False for invalid, stale, duplicated or regressed-parent samples.</returns>
    public bool Push(in RemoteMotionSample sample)
    {
        if (!Finite(sample.Position) || !Finite(sample.Velocity) || !float.IsFinite(sample.Rotation.LengthSquared())
            || MathF.Abs(sample.Rotation.Length() - 1f) > 0.01f) { return false; }
        var rebased = false;
        if (Count > 0)
        {
            var last = At(Count - 1);
            if (sample.Entity != last.Entity || sample.ServerTick <= last.ServerTick || sample.ParentSeq < last.ParentSeq) { return false; }
            if (sample.Frame != last.Frame || sample.ParentSeq != last.ParentSeq)
            {
                if (_frames is null) { _head = 0; Count = 0; ReparentSnaps++; }
                else { Rebase(sample.Frame, sample.ParentSeq, _frames); rebased = true; }
            }
        }
        if (Count > 0)
        {
            var last = At(Count - 1);
            var distanceLimit = 3d;
            if (sample.MotionFlags == (byte)MovementMode.DevFlying && last.MotionFlags == sample.MotionFlags)
            {
                distanceLimit = Math.Max(distanceLimit, _flightSpeed * ((sample.ServerTick.Value - last.ServerTick.Value) / _tickRate));
            }
            if (rebased)
            {
                // Rebased samples keep their world motion, which in the new frame may be a ship's speed (K5-20).
                distanceLimit += last.Velocity.Length() * ((sample.ServerTick.Value - last.ServerTick.Value) / _tickRate);
            }
            if (Vector3.Distance(sample.Position, last.Position) > distanceLimit)
            {
                _head = 0;
                Count = 0;
            }
        }
        if (Count == Capacity) { _head = (_head + 1) % Capacity; Count--; }
        _samples[(_head + Count) % Capacity] = sample with { Rotation = Quaternion.Normalize(sample.Rotation) };
        Count++;
        return true;
    }

    /// <summary>
    /// Re-expresses every stored sample in a new parent frame at its own tick, keeping its world pose and velocity (K5-20, decision 13).
    /// When a frame has no pose at a sample's tick, that sample and all older ones are dropped and <see cref="ReparentSnaps"/> grows.
    /// </summary>
    /// <param name="newFrame">The new parent frame.</param>
    /// <param name="newSeq">The new reparent sequence.</param>
    /// <param name="history">World poses of frames at server ticks.</param>
    public void Rebase(FrameRef newFrame, uint newSeq, IFrameHistoryView history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var kept = 0;
        for (var i = Count - 1; i >= 0; i--)
        {
            ref var slot = ref _samples[(_head + i) % Capacity];
            if (!FrameRebase.TryRebase(history, slot.Frame, newFrame, slot.ServerTick.Value, 1 / _tickRate, slot.Position, slot.Rotation,
                slot.Velocity, out var position, out var rotation, out var velocity)) { break; }
            slot = slot with { Frame = newFrame, ParentSeq = newSeq, Position = position, Rotation = rotation, Velocity = velocity };
            kept++;
        }
        if (kept == Count) { return; }
        _head = (_head + Count - kept) % Capacity;
        Count = kept;
        ReparentSnaps++;
    }

    /// <summary>Interpolates at the already delayed server cursor, with at most three ticks of extrapolation.</summary>
    /// <param name="serverTick">Render cursor; caller subtracts the interpolation delay exactly once.</param>
    /// <param name="sample">Interpolated pose, or the bounded last pose.</param>
    /// <returns>Whether a sample exists.</returns>
    public bool TrySample(double serverTick, out RemoteMotionSample sample)
    {
        sample = default;
        if (Count == 0 || !double.IsFinite(serverTick)) { return false; }
        sample = At(0);
        if (serverTick <= sample.ServerTick.Value) { return true; }
        for (var i = 1; i < Count; i++)
        {
            var next = At(i);
            if (serverTick <= next.ServerTick.Value)
            {
                var fraction = (float)((serverTick - sample.ServerTick.Value) / (next.ServerTick.Value - sample.ServerTick.Value));
                sample = sample with
                {
                    Position = Vector3.Lerp(sample.Position, next.Position, fraction),
                    Rotation = Quaternion.Normalize(Quaternion.Slerp(sample.Rotation, next.Rotation, fraction)),
                    Velocity = Vector3.Lerp(sample.Velocity, next.Velocity, fraction),
                };
                return true;
            }
            sample = next;
        }
        var gap = serverTick - sample.ServerTick.Value;
        if (gap > 3) { SampleUnderruns++; }
        if (sample.MotionFlags != (byte)MovementMode.Frozen)
        {
            sample = sample with { Position = sample.Position + sample.Velocity * (float)(Math.Min(3, gap) / _tickRate) };
        }
        return true;
    }

    /// <summary>Forgets samples and counters on disconnect or despawn.</summary>
    public void Clear() { _head = 0; Count = 0; SampleUnderruns = 0; ReparentSnaps = 0; }

    private RemoteMotionSample At(int index) => _samples[(_head + index) % Capacity];
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
