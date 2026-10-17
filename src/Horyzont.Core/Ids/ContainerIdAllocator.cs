namespace Horyzont.Core.Ids;

/// <summary>
/// Allocates <see cref="ContainerId"/> values like <see cref="EntityIdAllocator"/>: the resolver peeks at the next value and the applier
/// moves it, so replay restores it. <see cref="NextValue"/> is stored as <c>next_container_id</c> in <c>meta</c>.
/// </summary>
public sealed class ContainerIdAllocator
{
    /// <summary>Creates an allocator.</summary>
    /// <param name="nextValue">Next value to hand out; at least 1.</param>
    public ContainerIdAllocator(ulong nextValue)
    {
        if (nextValue < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nextValue), nextValue, "Next container id must be at least 1.");
        }

        NextValue = nextValue;
    }

    /// <summary>Next value to hand out (stored in the checkpoint).</summary>
    public ulong NextValue { get; private set; }

    /// <summary>Returns the next identifier without changing the allocator.</summary>
    /// <returns>The next container identifier.</returns>
    /// <exception cref="InvalidOperationException">All identifiers are used.</exception>
    public ContainerId PeekNext()
    {
        if (NextValue == ulong.MaxValue)
        {
            throw new InvalidOperationException("Container ids are exhausted.");
        }

        return new ContainerId(NextValue);
    }

    /// <summary>True when the identifier was never handed out.</summary>
    internal bool IsUnused(ContainerId id) => !id.IsNone && id.Value != ulong.MaxValue && id.Value >= NextValue;

    /// <summary>True when the identifier was handed out (1 .. NextValue - 1).</summary>
    internal bool IsUsedId(ContainerId id) => !id.IsNone && id.Value < NextValue;

    /// <summary>Marks an identifier as used: <c>NextValue = max(NextValue, id + 1)</c>.</summary>
    internal void MarkUsed(ContainerId id)
    {
        if (id.IsNone || id.Value == ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id.Value, "Container id out of range.");
        }

        NextValue = Math.Max(NextValue, id.Value + 1);
    }

    /// <summary>Sets the counter back to a value it had before (undo of a refused compound operation).</summary>
    internal void Restore(ulong nextValue) => NextValue = nextValue;
}
