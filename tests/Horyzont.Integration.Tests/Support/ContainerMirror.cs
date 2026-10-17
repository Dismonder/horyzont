using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Integration.Tests.Support;

/// <summary>What a client knows of its containers, built only from <see cref="ContainerState"/> and <see cref="ContainerDelta"/> messages.</summary>
internal sealed class ContainerMirror
{
    private readonly Dictionary<ContainerId, (ContainerKind Kind, uint Version, ItemStack[] Slots)> _containers = [];
    private int _consumed;

    public IReadOnlyCollection<ContainerId> Ids => _containers.Keys;

    public int DeltasApplied { get; private set; }

    /// <summary>Applies the messages recorded since the last call; a delta must be exactly one version ahead.</summary>
    public void Apply(IReadOnlyList<Message> messages)
    {
        for (; _consumed < messages.Count; _consumed++)
        {
            switch (messages[_consumed])
            {
                case ContainerState { Reason: Core.Intents.RejectReason.None } state:
                    var slots = new ItemStack[state.Size];
                    foreach (var slot in state.Slots)
                    {
                        slots[slot.Slot] = slot.Stack;
                    }

                    _containers[state.Container] = (state.Kind, state.Version, slots);
                    break;
                case ContainerDelta delta:
                    var known = _containers[delta.Container];
                    Assert.Equal(known.Version + 1, delta.Version);
                    foreach (var slot in delta.Changed)
                    {
                        known.Slots[slot.Slot] = slot.Stack;
                    }

                    _containers[delta.Container] = (known.Kind, delta.Version, known.Slots);
                    DeltasApplied++;
                    break;
                default:
                    break;
            }
        }
    }

    public ContainerId IdOf(ContainerKind kind) => _containers.Single(c => c.Value.Kind == kind).Key;

    public ItemStack Slot(ContainerKind kind, int slot) => _containers[IdOf(kind)].Slots[slot];

    public uint VersionOf(ContainerId id) => _containers[id].Version;

    public ItemStack[] SlotsOf(ContainerId id) => _containers[id].Slots;
}
