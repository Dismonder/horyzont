using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Net.Session;

/// <summary>
/// Short sorted buffer of player inputs: only numbers greater than the last taken one, no duplicates, at most the
/// capacity; when full, the newest inputs are dropped so the server processes input at a constant rate.
/// Sequence numbers do not wrap (2^32 inputs is years of play).
/// </summary>
internal sealed class InputBuffer
{
    private readonly uint[] _seqs;
    private readonly PlayerInput[] _inputs;
    private int _count;
    private bool _hasTaken;
    private uint _lastTaken;

    public InputBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _seqs = new uint[capacity];
        _inputs = new PlayerInput[capacity];
    }

    /// <summary>Gets the number of buffered inputs.</summary>
    public int Count => _count;

    /// <summary>Adds consecutive inputs numbered from <paramref name="firstSeq"/>.</summary>
    /// <param name="firstSeq">Number of the first input.</param>
    /// <param name="inputs">The inputs.</param>
    public void Add(uint firstSeq, IReadOnlyList<PlayerInput> inputs)
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
    public bool TryTake(out PlayerInput input, out uint seq)
    {
        if (_count == 0)
        {
            input = default;
            seq = 0;
            return false;
        }

        input = _inputs[0];
        seq = _seqs[0];
        _count--;
        Array.Copy(_seqs, 1, _seqs, 0, _count);
        Array.Copy(_inputs, 1, _inputs, 0, _count);
        _hasTaken = true;
        _lastTaken = seq;
        return true;
    }

    private void Insert(uint seq, PlayerInput input)
    {
        if (_hasTaken && seq <= _lastTaken)
        {
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
