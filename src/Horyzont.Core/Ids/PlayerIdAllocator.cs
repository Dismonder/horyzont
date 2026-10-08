namespace Horyzont.Core.Ids;

/// <summary>
/// Allocates <see cref="PlayerId"/> values; same contract as <see cref="EntityIdAllocator"/>.
/// <see cref="NextValue"/> is stored in the checkpoint as NextPlayerId.
/// </summary>
public sealed class PlayerIdAllocator
{
    /// <summary>Creates an allocator.</summary>
    /// <param name="nextValue">Next value to hand out; 1 .. 2^32.</param>
    public PlayerIdAllocator(ulong nextValue)
    {
        if (nextValue < 1 || nextValue > EntityId.TransientBase)
        {
            throw new ArgumentOutOfRangeException(nameof(nextValue), nextValue, "Next player id must be in 1 .. 2^32.");
        }

        NextValue = nextValue;
    }

    /// <summary>Next value to hand out (stored in the checkpoint).</summary>
    public ulong NextValue { get; private set; }

    /// <summary>Returns the next identifier without changing the allocator.</summary>
    /// <returns>The next player identifier.</returns>
    /// <exception cref="InvalidOperationException">The next number would reach <see cref="EntityId.TransientBase"/>.</exception>
    public PlayerId PeekNext()
    {
        if (NextValue >= EntityId.TransientBase)
        {
            throw new InvalidOperationException("Player ids are exhausted.");
        }

        return new PlayerId(NextValue);
    }

    /// <summary>True when the identifier was never handed out.</summary>
    internal bool IsUnused(PlayerId id) => !id.IsNone && id.Value < EntityId.TransientBase && id.Value >= NextValue;

    /// <summary>Marks an identifier as used: <c>NextValue = max(NextValue, id + 1)</c>.</summary>
    internal void MarkUsed(PlayerId id)
    {
        if (id.IsNone || id.Value >= EntityId.TransientBase)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id.Value, "Player id out of range.");
        }

        NextValue = Math.Max(NextValue, id.Value + 1);
    }
}
