namespace Horyzont.Core.Ids;

/// <summary>
/// Allocates <see cref="MarkerId"/> values; same contract as <see cref="EntityIdAllocator"/>: the resolver only peeks at the next
/// value and the applier moves it. <see cref="NextValue"/> is stored in <c>meta.next_marker_id</c>.
/// </summary>
public sealed class MarkerIdAllocator
{
    /// <summary>Largest marker id.</summary>
    public const ulong MaxValue = uint.MaxValue;

    /// <summary>Creates an allocator.</summary>
    /// <param name="nextValue">Next value to hand out; 1 .. 2^32.</param>
    public MarkerIdAllocator(ulong nextValue)
    {
        if (nextValue < 1 || nextValue > MaxValue + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nextValue), nextValue, "Next marker id must be in 1 .. 2^32.");
        }

        NextValue = nextValue;
    }

    /// <summary>Next value to hand out (stored in the checkpoint).</summary>
    public ulong NextValue { get; private set; }

    /// <summary>Returns the next identifier without changing the allocator.</summary>
    /// <returns>The next marker identifier.</returns>
    /// <exception cref="InvalidOperationException">All marker identifiers are used.</exception>
    public MarkerId PeekNext()
    {
        if (NextValue > MaxValue)
        {
            throw new InvalidOperationException("Marker ids are exhausted.");
        }

        return new MarkerId(NextValue);
    }

    /// <summary>True when the identifier was never handed out (at least <see cref="NextValue"/>, in range).</summary>
    internal bool IsUnused(MarkerId id) => !id.IsNone && id.Value <= MaxValue && id.Value >= NextValue;

    /// <summary>Marks an identifier as used: <c>NextValue = max(NextValue, id + 1)</c>.</summary>
    internal void MarkUsed(MarkerId id)
    {
        if (id.IsNone || id.Value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id.Value, "Marker id out of range.");
        }

        NextValue = Math.Max(NextValue, id.Value + 1);
    }

    /// <summary>Sets the counter when a world is loaded.</summary>
    internal void Restore(ulong nextValue)
    {
        if (nextValue < 1 || nextValue > MaxValue + 1)
        {
            throw new InvalidDataException($"Next marker id {nextValue} is out of range.");
        }

        NextValue = Math.Max(NextValue, nextValue);
    }
}
