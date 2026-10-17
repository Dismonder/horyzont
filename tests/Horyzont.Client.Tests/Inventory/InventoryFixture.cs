using Horyzont.Client.Inventory;
using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Inventory;

/// <summary>A client inventory with the test content, a bag (id 1, 40 slots) and a hotbar (id 2, 10 slots), and a recording sender.</summary>
internal sealed class InventoryFixture
{
    public static readonly ContainerId BagId = new(1);
    public static readonly ContainerId HotbarId = new(2);

    public static readonly ItemTypeId Tool = new(1);
    public static readonly ItemTypeId Gem = new(2);
    public static readonly ItemTypeId Stone = new(3);
    public static readonly ItemTypeId Soil = new(4);

    public InventoryFixture(bool withState = true)
    {
        Content = SurvivalContentFactory.CreateTest();
        Inventory = new ClientInventory(Content.Items, Requested.Add, NullLogger<ClientInventory>.Instance);
        Interaction = new InventoryInteraction(Inventory, Sender, NullLogger<InventoryInteraction>.Instance);
        Inventory.OnMessage(
            new ItemRegistry(
                [new RegistryEntry(1, SurvivalContentFactory.ToolKey), new RegistryEntry(2, SurvivalContentFactory.GemKey),
                 new RegistryEntry(3, SurvivalContentFactory.StoneKey), new RegistryEntry(4, SurvivalContentFactory.SoilKey)]));
        if (withState)
        {
            State(BagId, ContainerKind.PlayerBag, 40, 1);
            State(HotbarId, ContainerKind.Hotbar, 10, 1, (0, Tool, 1), (1, Gem, 3));
        }
    }

    public SurvivalContent Content { get; }

    public List<ContainerId> Requested { get; } = [];

    public RecordingSender Sender { get; } = new();

    public ClientInventory Inventory { get; }

    public InventoryInteraction Interaction { get; }

    public static SlotRef Bag(int slot) => new(BagId, (ushort)slot);

    public static SlotRef Hotbar(int slot) => new(HotbarId, (ushort)slot);

    public void State(ContainerId id, ContainerKind kind, ushort size, uint version, params (int Slot, ItemTypeId Type, int Count)[] slots) =>
        Inventory.OnMessage(
            new ContainerState(id, RejectReason.None, kind, ContainerAccess.Private, size, version, [.. slots.Select(s => new SlotContent((ushort)s.Slot, new ItemStack(s.Type, (ushort)s.Count)))]));

    public void Delta(ContainerId id, uint version, params (int Slot, ItemTypeId Type, int Count)[] slots) =>
        Inventory.OnMessage(new ContainerDelta(id, version, [.. slots.Select(s => new SlotContent((ushort)s.Slot, s.Type.IsNone ? ItemStack.Empty : new ItemStack(s.Type, (ushort)s.Count)))]));

    /// <summary>Puts items into a slot through the next delta of the container.</summary>
    public void Put(SlotRef slot, ItemTypeId type, int count)
    {
        var container = Inventory.TryGetContainer(slot.Container, out var c) ? c : throw new InvalidOperationException("no container");
        Delta(slot.Container, container.Version + 1, (slot.Slot, type, count));
    }

    public sealed class RecordingSender : IIntentSender
    {
        private uint _seq = 500;

        public bool CanSend { get; set; } = true;

        public List<Intent> Intents { get; } = [];

        public uint LastSeq => _seq;

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }
}
