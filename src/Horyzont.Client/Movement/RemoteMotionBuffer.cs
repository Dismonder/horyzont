using System.Numerics;

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
    private int _head;

    /// <summary>Creates a ring using the session physics rate and maximum permitted developer speed.</summary>
    /// <param name="tickRate">Server ticks per second.</param>
    /// <param name="maxFlightSpeed">Maximum developer flight speed.</param>
    public RemoteMotionBuffer(double tickRate = 60, float maxFlightSpeed = 150)
    {
        if (!double.IsFinite(tickRate) || tickRate <= 0) { throw new ArgumentOutOfRangeException(nameof(tickRate)); }
        if (!float.IsFinite(maxFlightSpeed) || maxFlightSpeed < 0) { throw new ArgumentOutOfRangeException(nameof(maxFlightSpeed)); }
        _tickRate = tickRate;
        _flightSpeed = maxFlightSpeed;
    }

    /// <summary>Gets the number of stored samples.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the number of requests beyond the bounded extrapolation horizon.</summary>
    public long SampleUnderruns { get; private set; }

    /// <summary>Accepts a newer, finite full sample, resetting on a parent change or teleport.</summary>
    /// <param name="sample">Reconstructed sample.</param>
    /// <returns>False for invalid, stale, duplicated or regressed-parent samples.</returns>
    public bool Push(in RemoteMotionSample sample)
    {
        if (!Finite(sample.Position) || !Finite(sample.Velocity) || !float.IsFinite(sample.Rotation.LengthSquared())
            || MathF.Abs(sample.Rotation.Length() - 1f) > 0.01f) { return false; }
        if (Count > 0)
        {
            var last = At(Count - 1);
            if (sample.Entity != last.Entity || sample.ServerTick <= last.ServerTick || sample.ParentSeq < last.ParentSeq) { return false; }
            var distanceLimit = 3d;
            if (sample.MotionFlags == (byte)MovementMode.DevFlying && last.MotionFlags == sample.MotionFlags)
            {
                distanceLimit = Math.Max(distanceLimit, _flightSpeed * ((sample.ServerTick.Value - last.ServerTick.Value) / _tickRate));
            }
            if (sample.Frame != last.Frame || sample.ParentSeq != last.ParentSeq || Vector3.Distance(sample.Position, last.Position) > distanceLimit)
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
    public void Clear() { _head = 0; Count = 0; SampleUnderruns = 0; }

    private RemoteMotionSample At(int index) => _samples[(_head + index) % Capacity];
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
