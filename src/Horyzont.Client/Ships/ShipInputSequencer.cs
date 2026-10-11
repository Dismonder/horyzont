using System.Collections;

using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships.Flight;

namespace Horyzont.Client.Ships;

/// <summary>
/// Numbers the pilot inputs (from 1, one per client tick; the number is the slot the server steps the ship with — K5-25 R1) and
/// packs them for the network (decision 11): every tick one <see cref="ShipInputBatch"/> with the new input and up to
/// <see cref="RepeatsPerBatch"/> earlier ones as repeats; after a hitch two new inputs per batch until the backlog is sent. The host
/// calls <see cref="TryTakeBatch"/> in a loop until it returns false. Allocation-free after construction.
/// </summary>
public sealed class ShipInputSequencer
{
    /// <summary>Earlier inputs repeated in every batch (spec 02:164).</summary>
    public const int RepeatsPerBatch = 3;

    /// <summary>New inputs per batch while a backlog is waiting.</summary>
    public const int NewPerBatchWhenBehind = ProtocolConstants.MaxInputsPerBatch - RepeatsPerBatch;

    private const int RingSize = 256;
    private const int MaxBacklog = RingSize - RepeatsPerBatch - 1;

    private readonly ShipInput[] _ring = new ShipInput[RingSize];
    private readonly ShipInput[] _batch = new ShipInput[ProtocolConstants.MaxInputsPerBatch];
    private readonly Window _window;
    private uint _nextUnsent = 1;

    /// <summary>Initializes a new instance of the <see cref="ShipInputSequencer"/> class.</summary>
    public ShipInputSequencer() => _window = new Window(_batch);

    /// <summary>Gets the number of the latest input; 0 before the first.</summary>
    public uint LastSeq { get; private set; }

    /// <summary>Starts again from 1 (a new seat or session); the server re-anchors its slots on the next input.</summary>
    public void Reset()
    {
        LastSeq = 0;
        _nextUnsent = 1;
        Array.Clear(_ring);
        _window.Count = 0;
    }

    /// <summary>Appends the control of this tick.</summary>
    /// <param name="clientTick">Client tick of the input.</param>
    /// <param name="control">The control (axes in −127..127, as the codec carries them).</param>
    /// <returns>The slot number of the input.</returns>
    public uint Record(uint clientTick, in ShipControl control)
    {
        LastSeq++;
        _ring[LastSeq % RingSize] = new ShipInput(clientTick, control);
        return LastSeq;
    }

    /// <summary>
    /// Returns the next batch: the next unsent input (two while a backlog waits) and up to three earlier ones. <b>The list is a view of a
    /// buffer the next call overwrites</b> — encode it immediately.
    /// </summary>
    /// <param name="firstSeq">Number of the first input in the batch.</param>
    /// <param name="inputs">The inputs in order of number.</param>
    /// <returns>True when a batch is due.</returns>
    public bool TryTakeBatch(out uint firstSeq, out IReadOnlyList<ShipInput> inputs)
    {
        inputs = _window;
        if (_nextUnsent > LastSeq)
        {
            firstSeq = 0;
            return false;
        }

        if (LastSeq + 1 - _nextUnsent > MaxBacklog)
        {
            _nextUnsent = LastSeq + 1 - MaxBacklog;
        }

        var fresh = LastSeq + 1 - _nextUnsent > 1 ? NewPerBatchWhenBehind : 1;
        var last = _nextUnsent + (uint)fresh - 1;
        var count = (int)Math.Min(last, (uint)(fresh + RepeatsPerBatch));
        firstSeq = last - (uint)count + 1;
        for (var i = 0; i < count; i++)
        {
            _batch[i] = _ring[(firstSeq + (uint)i) % RingSize];
        }

        _window.Count = count;
        _nextUnsent = last + 1;
        return true;
    }

    private sealed class Window(ShipInput[] items) : IReadOnlyList<ShipInput>
    {
        public int Count { get; set; }

        public ShipInput this[int index] =>
            (uint)index < (uint)Count ? items[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<ShipInput> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return items[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
