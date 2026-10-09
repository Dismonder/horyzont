using System.Collections;

using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Movement;

/// <summary>
/// Numbers the inputs of the local player (from 1, one per tick) and packs them for the network: every second tick one batch with the
/// two new inputs and up to three earlier ones as repeats, at most <see cref="ProtocolConstants.MaxInputsPerBatch"/>. The server drops
/// duplicates, so a lost batch is covered by the repeats of the next one. After a frame hitch several inputs may be waiting:
/// the host calls <see cref="TryTakeBatch"/> in a loop until it returns false, so every input is sent at least once, in order, in
/// batches within the protocol limit. Allocation-free after construction.
/// </summary>
public sealed class InputSequencer
{
    /// <summary>Inputs per batch that are new.</summary>
    public const int NewPerBatch = 2;

    /// <summary>Earlier inputs repeated in every batch.</summary>
    public const int RepeatsPerBatch = ProtocolConstants.MaxInputsPerBatch - NewPerBatch;

    // Holds the backlog after a hitch (about 4 s at 60 Hz) plus the repeats; an older backlog is skipped as stale.
    private const int RingSize = 256;
    private const int MaxBacklog = RingSize - RepeatsPerBatch - 1;

    private readonly PlayerInput[] _ring = new PlayerInput[RingSize];
    private readonly Window _window;
    private readonly PlayerInput[] _batch = new PlayerInput[ProtocolConstants.MaxInputsPerBatch];
    private uint _nextUnsent = 1;

    /// <summary>Initializes a new instance of the <see cref="InputSequencer"/> class.</summary>
    public InputSequencer() => _window = new Window(_batch);

    /// <summary>Gets the number of the latest input; 0 before the first.</summary>
    public uint LastSeq { get; private set; }

    /// <summary>Starts again from 1; for a new session. Corrections and a lost link do not renumber within a session.</summary>
    public void Reset()
    {
        LastSeq = 0;
        _nextUnsent = 1;
        Array.Clear(_ring);
        _window.Count = 0;
    }

    /// <summary>
    /// Appends the input of this tick to the history. The stored (and later sent) input is exactly what the server will decode: the axes
    /// are already quantized in <paramref name="input"/> (see <see cref="MotorInput.TryFrom"/>).
    /// </summary>
    /// <param name="clientTick">Client tick of the input.</param>
    /// <param name="input">Validated input.</param>
    /// <returns>The number of the input.</returns>
    public uint Record(uint clientTick, in MotorInput input)
    {
        LastSeq++;
        _ring[LastSeq % RingSize] = new PlayerInput(clientTick, input.MoveX, input.MoveY, input.Yaw, input.Pitch, input.Buttons);
        return LastSeq;
    }

    /// <summary>
    /// Returns the next batch to send: the next two unsent inputs and up to three earlier ones as repeats. Call it in a loop until it
    /// returns false (normally one batch every second tick, several after a hitch). <b>The returned list is a view of a buffer that
    /// the next call overwrites</b>: encode or copy it immediately and never keep it, in particular across frames of the host loop.
    /// A backlog beyond the ring (more than about 250 inputs) drops its oldest inputs.
    /// </summary>
    /// <param name="firstSeq">Number of the first input in the batch (the oldest repeat).</param>
    /// <param name="inputs">The inputs in order of number; reused by the next call.</param>
    /// <returns>True when a batch is due.</returns>
    public bool TryTakeBatch(out uint firstSeq, out IReadOnlyList<PlayerInput> inputs)
    {
        inputs = _window;
        if (LastSeq + 1 - _nextUnsent < NewPerBatch)
        {
            firstSeq = 0;
            return false;
        }

        if (LastSeq + 1 - _nextUnsent > MaxBacklog)
        {
            _nextUnsent = LastSeq + 1 - MaxBacklog;
        }

        var last = _nextUnsent + NewPerBatch - 1;
        var count = (int)Math.Min(last, NewPerBatch + RepeatsPerBatch);
        firstSeq = last - (uint)count + 1;
        for (var i = 0; i < count; i++)
        {
            _batch[i] = _ring[(firstSeq + (uint)i) % RingSize];
        }

        _window.Count = count;
        _nextUnsent += NewPerBatch;
        return true;
    }

    private sealed class Window(PlayerInput[] items) : IReadOnlyList<PlayerInput>
    {
        public int Count { get; set; }

        public PlayerInput this[int index] =>
            (uint)index < (uint)Count ? items[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<PlayerInput> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return items[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
