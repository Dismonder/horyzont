using System.Buffers;

namespace Horyzont.Constructs.Meshing;

/// <summary>Growable list backed by <see cref="ArrayPool{T}.Shared"/>; never shrinks below its high-water mark.</summary>
internal sealed class PooledBuffer<T> : IDisposable
{
    private T[] _array = [];

    public int Count { get; private set; }

    public ReadOnlySpan<T> AsSpan() => new(_array, 0, Count);

    public void Clear() => Count = 0;

    public void Add(T value)
    {
        if (Count == _array.Length)
        {
            Grow(Count + 1);
        }

        _array[Count++] = value;
    }

    public void Dispose()
    {
        if (_array.Length > 0)
        {
            ArrayPool<T>.Shared.Return(_array);
            _array = [];
        }

        Count = 0;
    }

    private void Grow(int minimum)
    {
        var bigger = ArrayPool<T>.Shared.Rent(Math.Max(minimum, Math.Max(256, _array.Length * 2)));
        Array.Copy(_array, bigger, Count);
        if (_array.Length > 0)
        {
            ArrayPool<T>.Shared.Return(_array);
        }

        _array = bigger;
    }
}
