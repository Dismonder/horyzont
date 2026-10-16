using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Client.Tests.Inventory;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Crafting;

/// <summary>
/// A client inventory with the crafting test content (twelve items, ids 1..12 as in <see cref="WorldFactory.CreateWithCrafting"/>, recipes of every station),
/// a bag (id 1, 40 slots) and a hotbar (id 2, 10 slots), the progress of the player and the recipe list (K6-32).
/// </summary>
internal sealed class CraftingClientFixture
{
    public static readonly ContainerId BagId = new(1);
    public static readonly ContainerId HotbarId = new(2);
    public static readonly ContainerId InputId = new(20);
    public static readonly ContainerId OutputId = new(21);

    public CraftingClientFixture()
    {
        Content = SurvivalContentFactory.CreateTestWithRecipes();
        Inventory = new ClientInventory(Content.Items, Requested.Add, NullLogger<ClientInventory>.Instance);
        Inventory.OnMessage(new ItemRegistry([.. SurvivalContentFactory.CraftingItemKeys.Select((key, i) => new RegistryEntry((ushort)(i + 1), key))]));
        State(BagId, ContainerKind.PlayerBag, 40);
        State(HotbarId, ContainerKind.Hotbar, 10);
        Progress = new ClientProgress();
        Recipes = new RecipeList(Content, Inventory, Progress);
    }

    public SurvivalContent Content { get; }

    public List<ContainerId> Requested { get; } = [];

    public InventoryFixture.RecordingSender Sender { get; } = new();

    public ClientInventory Inventory { get; }

    public ClientProgress Progress { get; }

    public RecipeList Recipes { get; }

    public static ItemTypeId Item(string key) => WorldFactory.Item(key);

    public static SlotRef Bag(int slot) => new(BagId, (ushort)slot);

    public RecipeDef Recipe(string key) => Content.TryGetRecipe(key, out var recipe) ? recipe : throw new InvalidOperationException($"no recipe {key}");

    /// <summary>Tells the client what the player discovered and which keys the world has.</summary>
    public void Know(string[] discoveredItems, params string[] worldKeys)
    {
        var bits = ItemTypeBitSet.Empty;
        foreach (var key in discoveredItems)
        {
            bits = bits.With(Item(key));
        }

        Progress.Apply(new PlayerProgressState(bits, [.. worldKeys.Order(StringComparer.Ordinal)]));
    }

    public void State(ContainerId id, ContainerKind kind, ushort size, params (int Slot, string Item, int Count)[] slots) =>
        Inventory.OnMessage(
            new ContainerState(
                id, RejectReason.None, kind, ContainerAccess.Shared, size, 1,
                [.. slots.Select(s => new SlotContent((ushort)s.Slot, new ItemStack(Item(s.Item), (ushort)s.Count)))]));

    /// <summary>Puts items into a slot through the next delta of the container.</summary>
    public void Put(SlotRef slot, string item, int count)
    {
        var container = Inventory.TryGetContainer(slot.Container, out var c) ? c : throw new InvalidOperationException("no container");
        Inventory.OnMessage(new ContainerDelta(slot.Container, container.Version + 1, [new SlotContent(slot.Slot, new ItemStack(Item(item), (ushort)count))]));
    }
}
