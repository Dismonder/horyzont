namespace Horyzont.Net.Session;

/// <summary>
/// Short sorted buffer of numbered inputs: only numbers greater than the last taken one, no duplicates, at most the
/// capacity; when full, the newest inputs are dropped so the server processes input at a constant rate.
/// Sequence numbers do not wrap (2^32 inputs is years of play). K5-25 decision 12 generalises the K3-14 buffer.
/// </summary>
/// <typeparam name="TInput">Input type.</typeparam>
internal class SequencedInputBuffer<TInput>
    where TInput : struct
{
    private readonly uint[] _seqs;
    private readonly TInput[] _inputs;
    private int _count;
    private bool _hasTaken;
    private uint _lastTaken;
    private bool _hasReceived;
    private uint _newestReceived;

    public SequencedInputBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _seqs = new uint[capacity];
        _inputs = new TInput[capacity];
    }

    /// <summary>Gets the number of buffered inputs.</summary>
    public int Count => _count;

    /// <summary>
    /// Gets the number of new inputs (numbered above every input received before) refused because their slot was already taken:
    /// late arrivals of a client behind the server. Repeated copies of earlier inputs (batch redundancy) are not counted.
    /// </summary>
    public int LateCount { get; private set; }

    /// <summary>Adds consecutive inputs numbered from <paramref name="firstSeq"/>.</summary>
    /// <param name="firstSeq">Number of the first input.</param>
    /// <param name="inputs">The inputs.</param>
    public void Add(uint firstSeq, IReadOnlyList<TInput> inputs)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            var seq = (ulong)firstSeq + (ulong)i;
            if (seq > uint.MaxValue)
            {
                return;
            }

            Insert((uint)seq, inputs[i]);
        }
    }

    /// <summary>Takes the oldest buffered input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="seq">Its number.</param>
    /// <returns>False when the buffer is empty.</returns>
    public bool TryTake(out TInput input, out uint seq)
    {
        if (_count == 0)
        {
            input = default;
            seq = 0;
            return false;
        }

        input = _inputs[0];
        seq = _seqs[0];
        RemoveFirst(1);
        _hasTaken = true;
        _lastTaken = seq;
        return true;
    }

    /// <summary>
    /// Consumes slot <paramref name="slot"/> (K5-25 R1): drops inputs numbered below it and returns the input of the slot when it
    /// arrived. The slot counts as taken either way, so a later arrival for it is refused.
    /// </summary>
    /// <param name="slot">The slot number.</param>
    /// <param name="input">The input of the slot.</param>
    /// <returns>True when the input of the slot was buffered.</returns>
    public bool TryTakeSlot(uint slot, out TInput input)
    {
        var drop = 0;
        while (drop < _count && _seqs[drop] < slot)
        {
            drop++;
        }

        var found = drop < _count && _seqs[drop] == slot;
        input = found ? _inputs[drop] : default;
        RemoveFirst(found ? drop + 1 : drop);
        if (!_hasTaken || slot > _lastTaken)
        {
            _hasTaken = true;
            _lastTaken = slot;
        }

        return found;
    }

    /// <summary>The newest buffered number.</summary>
    /// <param name="seq">The number.</param>
    /// <returns>False when the buffer is empty.</returns>
    public bool TryPeekNewest(out uint seq)
    {
        seq = _count > 0 ? _seqs[_count - 1] : 0;
        return _count > 0;
    }

    /// <summary>The newest number received since the last clear, buffered or refused as late (K5-25 R1 re-anchoring).</summary>
    /// <param name="seq">The number.</param>
    /// <returns>False when nothing was received.</returns>
    public bool TryPeekNewestReceived(out uint seq)
    {
        seq = _newestReceived;
        return _hasReceived;
    }

    /// <summary>
    /// Takes back the slots from <paramref name="slot"/> on (K5-25 R1 re-anchoring behind the consumed slots): inputs numbered from
    /// <paramref name="slot"/> are accepted again. Buffered inputs are kept.
    /// </summary>
    /// <param name="slot">The first slot to accept again.</param>
    public void Rewind(uint slot)
    {
        if (!_hasTaken || slot > _lastTaken)
        {
            return;
        }

        if (slot == 0)
        {
            _hasTaken = false;
            _lastTaken = 0;
        }
        else
        {
            _lastTaken = slot - 1;
        }
    }

    /// <summary>Forgets every input and the last taken number (a new sequence may start anywhere).</summary>
    public void Clear()
    {
        _count = 0;
        _hasTaken = false;
        _lastTaken = 0;
        _hasReceived = false;
        _newestReceived = 0;
        LateCount = 0;
    }

    private void RemoveFirst(int n)
    {
        if (n <= 0)
        {
            return;
        }

        _count -= n;
        Array.Copy(_seqs, n, _seqs, 0, _count);
        Array.Copy(_inputs, n, _inputs, 0, _count);
    }

    private void Insert(uint seq, TInput input)
    {
        var isNew = !_hasReceived || seq > _newestReceived;
        if (isNew)
        {
            _hasReceived = true;
            _newestReceived = seq;
        }

        if (_hasTaken && seq <= _lastTaken)
        {
            if (isNew)
            {
                LateCount++;
            }

            return;
        }

        var index = 0;
        while (index < _count && _seqs[index] < seq)
        {
            index++;
        }

        if (index < _count && _seqs[index] == seq)
        {
            return;
        }

        if (_count == _seqs.Length)
        {
            if (index == _count)
            {
                return; // the newest input does not fit
            }

            _count--; // drop the newest buffered input to make room for an older one
        }

        Array.Copy(_seqs, index, _seqs, index + 1, _count - index);
        Array.Copy(_inputs, index, _inputs, index + 1, _count - index);
        _seqs[index] = seq;
        _inputs[index] = input;
        _count++;
    }
}
