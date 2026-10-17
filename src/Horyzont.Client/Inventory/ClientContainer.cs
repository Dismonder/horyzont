using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Client.Inventory;

/// <summary>The client copy of one container that the server replicates to the local player.</summary>
public sealed class ClientContainer
{
    private readonly ItemStack[] _slots;

    internal ClientContainer(ContainerId id, ContainerKind kind, ContainerAccess access, int size, uint version)
    {
        Id = id;
        Kind = kind;
        Access = access;
        Version = version;
        _slots = new ItemStack[size];
    }

    /// <summary>Gets the container id.</summary>
    public ContainerId Id { get; }

    /// <summary>Gets the kind.</summary>
    public ContainerKind Kind { get; }

    /// <summary>Gets the access mode.</summary>
    public ContainerAccess Access { get; }

    /// <summary>Gets the number of slots.</summary>
    public int Size => _slots.Length;

    /// <summary>Gets the version of the latest state or delta that was applied.</summary>
    public uint Version { get; internal set; }

    /// <summary>Gets the slots; an empty slot is <see cref="ItemStack.Empty"/>.</summary>
    public IReadOnlyList<ItemStack> Slots => _slots;

    /// <summary>Gets one slot, or the empty stack for an index outside the container.</summary>
    /// <param name="slot">Slot index.</param>
    /// <returns>The stack.</returns>
    public ItemStack Get(int slot) => (uint)slot < (uint)_slots.Length ? _slots[slot] : ItemStack.Empty;

    internal bool TrySet(int slot, ItemStack stack)
    {
        if ((uint)slot >= (uint)_slots.Length)
        {
            return false;
        }

        _slots[slot] = stack;
        return true;
    }
}
