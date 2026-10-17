using Horyzont.Client.Inventory;
using Horyzont.Client.Survival;
using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Survival;

/// <summary>
/// A client inventory with the hazard test content (<see cref="SurvivalContentFactory.CreateTestWithHazards"/>, the item ids 1..13 of <see cref="SurvivalContentFactory.HazardItemKeys"/>), a bag (id 1,
/// 40 slots), a hotbar (id 2, 10 slots) with a heat cell in slot 0 (2), the tool in slot 1, stone in slot 2 and berries in slot 3, the equipment (id 3, 7 slots) with a tank in slot 4, and the vitals of the
/// local player (K6-34).
/// </summary>
internal sealed class HazardClientFixture
{
    public static readonly ContainerId BagId = new(1);
    public static readonly ContainerId HotbarId = new(2);
    public static readonly ContainerId EquipmentId = new(3);

    public HazardClientFixture(bool withTank = true)
    {
        Content = SurvivalContentFactory.CreateTestWithHazards();
        Inventory = new ClientInventory(Content.Items, _ => { }, NullLogger<ClientInventory>.Instance);
        Inventory.OnMessage(new ItemRegistry([.. SurvivalContentFactory.HazardItemKeys.Select((key, i) => new RegistryEntry((ushort)(i + 1), key))]));
        Inventory.OnMessage(new ContainerState(BagId, RejectReason.None, ContainerKind.PlayerBag, ContainerAccess.Private, 40, 1, []));
        Inventory.OnMessage(
            new ContainerState(
                HotbarId, RejectReason.None, ContainerKind.Hotbar, ContainerAccess.Private, 10, 1,
                [
                    new SlotContent(0, new ItemStack(Item(SurvivalContentFactory.HeatCellKey), 2)),
                    new SlotContent(1, new ItemStack(Item(SurvivalContentFactory.ToolKey), 1)),
                    new SlotContent(2, new ItemStack(Item(SurvivalContentFactory.StoneKey), 5)),
                    new SlotContent(3, new ItemStack(Item(SurvivalContentFactory.BerriesKey), 3)),
                ]));
        Inventory.OnMessage(
            new ContainerState(
                EquipmentId, RejectReason.None, ContainerKind.Equipment, ContainerAccess.Private, 7, 1,
                withTank ? [new SlotContent(4, new ItemStack(Tank, 1))] : []));
        Tools = new ItemToolMap(new Dictionary<string, ToolKind> { [SurvivalContentFactory.ToolKey] = ToolKind.TerrainManipulator });
        Use = new SuitRefillUse(Inventory, Selection, Tools, Vitals);
    }

    public SurvivalContent Content { get; }

    public ClientInventory Inventory { get; }

    public ClientVitals Vitals { get; } = new();

    public ItemToolMap Tools { get; }

    public HotbarSelection Selection { get; } = new();

    public SuitRefillUse Use { get; }

    public static ItemTypeId Tank => Item(SurvivalContentFactory.TankModuleKey);

    public static ItemTypeId Thermal => Item(SurvivalContentFactory.ThermalModuleKey);

    public static ItemTypeId Stone => Item(SurvivalContentFactory.StoneKey);

    public static ItemTypeId Item(string key) => WorldFactory.Item(key);

    /// <summary>Tells the client the vitals the server sent: the suit, its capacity and the environment.</summary>
    public void Told(SuitResources suit, SuitResources? capacity = null, VitalsEnvironment environment = default) =>
        Vitals.Apply(
            TestMessages.Vitals(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, environment, suit, capacity ?? new SuitResources(100f, 100f, 100f, 100f, 100f)), 1000);

    /// <summary>Puts items into a slot through the next delta of the container.</summary>
    public void Put(ContainerId id, int slot, ItemTypeId type, int count)
    {
        Assert.True(Inventory.TryGetContainer(id, out var container));
        Inventory.OnMessage(new ContainerDelta(id, container.Version + 1, [new SlotContent((ushort)slot, type.IsNone ? ItemStack.Empty : new ItemStack(type, (ushort)count))]));
    }
}
