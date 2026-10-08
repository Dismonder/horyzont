namespace Horyzont.Core.Ids;

/// <summary>
/// Allocates persistent <see cref="EntityId"/> values. The resolver only peeks at the next value and the applier
/// moves it, so replaying the journal restores the allocator without a separate record.
/// </summary>
public sealed class EntityIdAllocator
{
    /// <summary>Creates an allocator.</summary>
    /// <param name="nextValue">Next value to hand out; 1 .. 2^32.</param>
    public EntityIdAllocator(ulong nextValue)
    {
        if (nextValue < 1 || nextValue > EntityId.TransientBase)
        {
            throw new ArgumentOutOfRangeException(nameof(nextValue), nextValue, "Next entity id must be in 1 .. 2^32.");
        }

        NextValue = nextValue;
    }

    /// <summary>Next value to hand out (stored in the checkpoint).</summary>
    public ulong NextValue { get; private set; }

    /// <summary>Returns the next identifier without changing the allocator.</summary>
    /// <returns>The next persistent identifier.</returns>
    /// <exception cref="InvalidOperationException">All persistent identifiers are used.</exception>
    public EntityId PeekNext()
    {
        if (NextValue >= EntityId.TransientBase)
        {
            throw new InvalidOperationException("Persistent entity ids are exhausted.");
        }

        return new EntityId(NextValue);
    }

    /// <summary>True when the identifier was never handed out (at least <see cref="NextValue"/>, persistent).</summary>
    internal bool IsUnused(EntityId id) => !id.IsNone && !id.IsTransient && id.Value >= NextValue;

    /// <summary>Marks an identifier as used: <c>NextValue = max(NextValue, id + 1)</c>.</summary>
    internal void MarkUsed(EntityId id)
    {
        if (id.IsNone || id.IsTransient)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id.Value, "Only persistent entity ids can be marked as used.");
        }

        NextValue = Math.Max(NextValue, id.Value + 1);
    }
}
