using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Game.Client.Inventory;
using Horyzont.Game.Content;
using Horyzont.Game.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Game.Tests.Inventory;

/// <summary>An <see cref="InventoryNode"/> with the real content and the UI, without a session: the replicated state is fed by hand and intents go to a recorder.</summary>
internal sealed class InventoryRig
{
    public const ulong BagId = 1;
    public const ulong HotbarId = 2;

    private InventoryRig(InventoryNode node, SurvivalContent content)
    {
        Node = node;
        Content = content;
    }

    public InventoryNode Node { get; }

    public SurvivalContent Content { get; }

    public RecordingSender Sender { get; } = new();

    public ScriptedInput Input { get; } = new();

    public long Now { get; set; }

    /// <summary>Creates the node with the UI under the scene, feeds the registry and the containers of a new player.</summary>
    public static InventoryRig Create(EngineTestScene scene)
    {
        var content = SurvivalContentLoader.Load();
        var node = new InventoryNode { Name = "Inventory" };
        var rig = new InventoryRig(node, content);
        node.Initialize(content, ItemVisuals.Load(), rig.Sender, _ => { }, NullLoggerFactory.Instance, render: true, recaptureMouse: null);
        node.InputSource = rig.Input;
        node.Clock = () => rig.Now;
        scene.Add(node);

        var keys = SurvivalContentLoader.ItemKeys(content);
        node.Inventory.OnMessage(new ItemRegistry([.. keys.Select((key, i) => new RegistryEntry((ushort)(i + 1), key))]));
        node.Inventory.OnMessage(State(new(BagId), ContainerKind.PlayerBag, 40, []));
        node.Inventory.OnMessage(
            State(
                new(HotbarId), ContainerKind.Hotbar, 10,
                [new(0, new ItemStack(rig.Type("core:multitool"), 1)), new(1, new ItemStack(rig.Type("core:build_tool"), 1)),
                 new(2, new ItemStack(rig.Type("core:stone"), 30))]));
        return rig;
    }

    public Horyzont.Core.Ids.ItemTypeId Type(string key) =>
        Node.Inventory.TryGetTypeId(key, out var id) ? id : throw new InvalidOperationException($"No item {key}");

    private static ContainerState State(Horyzont.Core.Ids.ContainerId id, ContainerKind kind, ushort size, List<SlotContent> slots) =>
        new(id, RejectReason.None, kind, ContainerAccess.Private, size, 1, slots);

    public sealed class RecordingSender : IIntentSender
    {
        private uint _seq = 900;

        public bool CanSend => true;

        public List<Intent> Intents { get; } = [];

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }

    public sealed class ScriptedInput : IInventoryInputSource
    {
        public InventoryInputFrame Next { get; set; } = InventoryInputFrame.None;

        public InventoryInputFrame Take()
        {
            var frame = Next;
            Next = InventoryInputFrame.None;
            return frame;
        }
    }
}
