using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Survival;
using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Survival;

/// <summary>
/// A client inventory with the food test content (the item ids 1..8 of <see cref="SurvivalContentFactory.FoodItemKeys"/>: tool, gem, stone, soil, berries, jam, broth,
/// bread), a bag (id 1, 40 slots) and a hotbar (id 2, 10 slots) with berries in slot 0 (3), the tool in slot 1 and stone in slot 2, the progress of the player
/// and a settable tick of the server (K6-33).
/// </summary>
internal sealed class FoodClientFixture
{
    public static readonly ContainerId BagId = new(1);
    public static readonly ContainerId HotbarId = new(2);

    /// <summary>Ticks per second of the rules the checks use (the default of <see cref="Horyzont.Core.Simulation.GameRules"/>).</summary>
    public const int TickRate = 60;

    public FoodClientFixture()
    {
        Content = SurvivalContentFactory.CreateTestWithFood();
        Inventory = new ClientInventory(Content.Items, _ => { }, NullLogger<ClientInventory>.Instance);
        Inventory.OnMessage(new ItemRegistry([.. SurvivalContentFactory.FoodItemKeys.Select((key, i) => new RegistryEntry((ushort)(i + 1), key))]));
        Inventory.OnMessage(new ContainerState(BagId, RejectReason.None, ContainerKind.PlayerBag, ContainerAccess.Shared, 40, 1, []));
        Inventory.OnMessage(
            new ContainerState(
                HotbarId, RejectReason.None, ContainerKind.Hotbar, ContainerAccess.Shared, 10, 1,
                [
                    new SlotContent(0, new ItemStack(Item(SurvivalContentFactory.BerriesKey), 3)),
                    new SlotContent(1, new ItemStack(Item(SurvivalContentFactory.ToolKey), 1)),
                    new SlotContent(2, new ItemStack(Item(SurvivalContentFactory.StoneKey), 5)),
                ]));
        Progress = new ClientProgress();
        Tools = new ItemToolMap(new Dictionary<string, ToolKind> { [SurvivalContentFactory.ToolKey] = ToolKind.TerrainManipulator });
        Use = new FoodUse(Inventory, Selection, Tools, Progress, ReadTick, tickRate: TickRate);
    }

    public SurvivalContent Content { get; }

    public ClientInventory Inventory { get; }

    public ClientProgress Progress { get; }

    public ItemToolMap Tools { get; }

    public HotbarSelection Selection { get; } = new();

    public FoodUse Use { get; }

    /// <summary>The tick of the server the checks read; <see cref="TickKnown"/> false makes it unavailable.</summary>
    public ulong Tick { get; set; } = 10_000;

    public bool TickKnown { get; set; } = true;

    public static ItemTypeId Berries => WorldFactory.Item(SurvivalContentFactory.BerriesKey);

    public static ItemTypeId Jam => WorldFactory.Item(SurvivalContentFactory.JamKey);

    public static ItemTypeId Item(string key) => WorldFactory.Item(key);

    /// <summary>Berries ran 600 s = 36 000 ticks, jam 1200 s = 72 000 ticks.</summary>
    public const ulong BerryTicks = 36_000;

    /// <summary>Tells the client the food slots the server holds.</summary>
    public void Eaten(params (ItemTypeId Type, ulong Tick)[] foods)
    {
        var slots = FoodSlots.Empty;
        for (var i = 0; i < foods.Length; i++)
        {
            slots = slots.With(i, new FoodSlot(foods[i].Type, new Tick(foods[i].Tick)));
        }

        Progress.Apply(new PlayerProgressState(ItemTypeBitSet.Empty, [], slots));
    }

    private bool ReadTick(out ulong tick)
    {
        tick = Tick;
        return TickKnown;
    }
}
