using Horyzont.Core.Motion;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Movement;

/// <summary>
/// History of the predicted ticks that the server has not acknowledged yet (a ring of <see cref="Capacity"/> entries). When the server
/// reports its state for an input, the buffer decides whether the prediction is still right; if not, the caller moves the body to the
/// server state and replays <see cref="CopyUnacknowledged"/> in order (at most <see cref="Capacity"/> engine steps in one frame).
/// Allocation-free after construction.
/// </summary>
public sealed class PredictionBuffer
{
    /// <summary>Number of entries; also the largest replay.</summary>
    public const int Capacity = 128;

    private readonly CorrectionPolicy _policy;
    private readonly ILogger _logger;
    private readonly PredictionEntry[] _entries = new PredictionEntry[Capacity];
    private int _head;
    private bool _hasAcked;
    private uint _lastAcked;
    private bool _pauseLogged;

    /// <summary>Initializes a new instance of the <see cref="PredictionBuffer"/> class.</summary>
    /// <param name="policy">Correction thresholds; <c>MaxUnacknowledged</c> must be in 1..<see cref="Capacity"/>.</param>
    /// <param name="logger">Logger.</param>
    public PredictionBuffer(CorrectionPolicy policy, ILogger<PredictionBuffer> logger)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxUnacknowledged, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(policy.MaxUnacknowledged, Capacity);
        _policy = policy;
        _logger = logger;
    }

    /// <summary>Gets the number of unacknowledged entries.</summary>
    public int Count { get; private set; }

    /// <summary>Gets a value indicating whether prediction is paused: too many inputs are waiting for the server.</summary>
    public bool IsPaused => Count >= _policy.MaxUnacknowledged;

    /// <summary>Appends a predicted tick. The input is still sent while paused, but it is not recorded.</summary>
    /// <param name="seq">Number of the input; increasing.</param>
    /// <param name="input">The input as the server will see it.</param>
    /// <param name="state">The state predicted after the input.</param>
    /// <returns>False when the ring is full and nothing was recorded.</returns>
    public bool Record(uint seq, in MotorInput input, in PredictedState state)
    {
        if (Count == Capacity)
        {
            return false;
        }

        _entries[(_head + Count) % Capacity] = new PredictionEntry(seq, input, state);
        Count++;
        if (IsPaused && !_pauseLogged)
        {
            _pauseLogged = true;
            _logger.PredictionPaused(Count);
        }

        return true;
    }

    /// <summary>Processes the server state after input <paramref name="seq"/> and drops the acknowledged entries.</summary>
    /// <param name="seq">Last input the server processed.</param>
    /// <param name="server">The server state after that input.</param>
    /// <returns>Whether the prediction was right, or the body must be corrected.</returns>
    public AckOutcome Acknowledge(uint seq, in PredictedState server)
    {
        if (_hasAcked && seq <= _lastAcked)
        {
            return AckOutcome.Stale;
        }

        _hasAcked = true;
        _lastAcked = seq;

        var found = false;
        PredictedState predicted = default;
        while (Count > 0 && _entries[_head].Seq <= seq)
        {
            if (_entries[_head].Seq == seq)
            {
                found = true;
                predicted = _entries[_head].State;
            }

            _head = (_head + 1) % Capacity;
            Count--;
        }

        if (!IsPaused)
        {
            _pauseLogged = false;
        }

        if (found && !_policy.NeedsCorrection(predicted, server))
        {
            return AckOutcome.InSync;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.Corrected(
                seq,
                found ? CorrectionPolicy.PositionError(predicted, server) : float.NaN,
                found ? CorrectionPolicy.VelocityError(predicted, server) : float.NaN,
                Count);
        }

        return AckOutcome.Correct;
    }

    /// <summary>Copies the unacknowledged entries, oldest first: the inputs to replay after a correction.</summary>
    /// <param name="destination">Receives up to <see cref="Count"/> entries.</param>
    /// <returns>The number of entries copied.</returns>
    public int CopyUnacknowledged(Span<PredictionEntry> destination)
    {
        var count = Math.Min(Count, destination.Length);
        for (var i = 0; i < count; i++)
        {
            destination[i] = _entries[(_head + i) % Capacity];
        }

        return count;
    }

    /// <summary>Stores the states produced by a replay, in the order of <see cref="CopyUnacknowledged"/>.</summary>
    /// <param name="states">The new states of the oldest entries.</param>
    public void ReplaceUnacknowledgedStates(ReadOnlySpan<PredictedState> states)
    {
        var count = Math.Min(Count, states.Length);
        for (var i = 0; i < count; i++)
        {
            ref var entry = ref _entries[(_head + i) % Capacity];
            entry = entry with { State = states[i] };
        }
    }

    /// <summary>Forgets everything: after a disconnect, when the numbering starts again.</summary>
    public void Clear()
    {
        _head = 0;
        Count = 0;
        _hasAcked = false;
        _lastAcked = 0;
        _pauseLogged = false;
    }
}
