using Horyzont.Core.Ids;

namespace Horyzont.Core.Items;

/// <summary>
/// An item container: fixed number of slots and a version that grows by 1 with every journal operation that changes it. Changed only by
/// <see cref="Simulation.ServerSimulation"/> through item operations.
/// </summary>
public sealed class Container
{
    private readonly ItemStack[] _slots;

    internal Container(ContainerId id, ContainerKind kind, ContainerHost host, PlayerId owner, ContainerAccess access, ushort size, uint version)
    {
        Id = id;
        Kind = kind;
        Host = host;
        Owner = owner;
        Access = access;
        Version = version;
        _slots = new ItemStack[size];
    }

    /// <summary>Identifier.</summary>
    public ContainerId Id { get; }

    /// <summary>Kind.</summary>
    public ContainerKind Kind { get; }

    /// <summary>Owner of the container.</summary>
    public ContainerHost Host { get; }

    /// <summary>Owning player, <see cref="PlayerId.None"/> for none.</summary>
    public PlayerId Owner { get; }

    /// <summary>Access.</summary>
    public ContainerAccess Access { get; }

    /// <summary>Number of slots.</summary>
    public int Size => _slots.Length;

    /// <summary>Version; a new container has 1 after the operation that created it.</summary>
    public uint Version { get; private set; }

    /// <summary>All slots in order.</summary>
    public ReadOnlySpan<ItemStack> Slots => _slots;

    /// <summary>Content of a slot.</summary>
    /// <param name="slot">Slot index, 0..Size-1.</param>
    /// <returns>The stack; <see cref="ItemStack.Empty"/> for an empty slot.</returns>
    public ItemStack GetSlot(int slot) => _slots[slot];

    internal void SetSlot(int slot, ItemStack stack) => _slots[slot] = stack;

    internal void BumpVersion() => Version++;
}
