using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35: a <see cref="HazardWorld"/> with the helpers of a death: a full kit for a player (stacks in the bag, the hotbar and the equipment, three food slots, a bed, a discovery,
/// vitals), the death operation built by <see cref="DeathOperationBuilder"/> and applied like the server does, and sums of every item type over every container of the world.
/// With <c>survival</c> the simulation resolves intents and player n stands within reach of a tombstone placed by <see cref="DefaultPlacement"/>.
/// </summary>
internal sealed class DeathWorld
{
    public DeathWorld(int players = 1, bool survival = false)
    {
        Hazard = new HazardWorld(players, survival, recipes: false);
    }

    public static PlayerId Player1 => HazardWorld.Player1;

    public static PlayerId Player2 => HazardWorld.Player2;

    public static ItemTypeId Stone => HazardWorld.Stone;

    public static ItemTypeId Gem => ItemFixture.Gem;

    public static ItemTypeId Tool => ItemFixture.Tool;

    public static ItemTypeId Tank => HazardWorld.Tank;

    public static ItemTypeId Thermal => HazardWorld.Thermal;

    public HazardWorld Hazard { get; }

    public WorldState State => Hazard.State;

    public ServerSimulation Simulation => Hazard.Simulation;

    public static ContainerId BagOf(PlayerId player) => WorldFactory.BagOf((int)player.Value);

    public static ContainerId HotbarOf(PlayerId player) => WorldFactory.HotbarOf((int)player.Value);

    public static ContainerId EquipmentOf(PlayerId player) => WorldFactory.EquipmentOf((int)player.Value);

    public AppliedOperation Apply(Operation op) => Hazard.Apply(op);

    /// <summary>Runs the applier without journaling; only for operations that are expected to fail.</summary>
    public bool TryApplyRaw(Operation op, out AppliedOperation? applied, out string failure, out int componentIndex) =>
        Hazard.Blocks.TryApplyRaw(op, out applied, out failure, out componentIndex);

    public Container Container(ContainerId id) => Hazard.Blocks.Container(id);

    /// <summary>Puts a stack into a slot (server operation, a limit that fits any count).</summary>
    public void Put(ContainerId container, int slot, ItemTypeId type, int count) =>
        Apply(new ItemCreateOp(new SlotRef(container, (ushort)slot), type, (ushort)count, ushort.MaxValue, ItemSource.Admin));

    /// <summary>
    /// The kit of a player: bag 0 = 30 stone, bag 5 = 3 gems, hotbar 0 = a tool, hotbar 1 = 4 gems, equipment 0 and 1 = a tank and a thermal module; food in all three slots; a bed that is the
    /// respawn point; one discovered item type; vitals with a suit. Four stacks in the bag and the hotbar, two in the equipment.
    /// </summary>
    public void GiveKit(PlayerId player)
    {
        Put(BagOf(player), 0, Stone, 30);
        Put(BagOf(player), 5, Gem, 3);
        Put(HotbarOf(player), 0, Tool, 1);
        Put(HotbarOf(player), 1, Gem, 4);
        Put(EquipmentOf(player), 0, Tank, 1);
        Put(EquipmentOf(player), 1, Thermal, 1);
        Apply(new FoodSetOp(player, FoodWorld.Slots(FoodWorld.Slot(FoodWorld.Berries, 100), FoodWorld.Slot(FoodWorld.Jam, 200), FoodWorld.Slot(FoodWorld.Bread, 300))));
        Hazard.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, player);
        Apply(new ItemDiscoverOp(player, Stone));
        Assert.True(Simulation.UpdatePlayerVitals(player, new PlayerVitals(7f, 33f, new Tick(5000), true, new SuitResources(10f, 20f, 30f, 40f, 50f))));
    }

    /// <summary>The planet-frame pose of a player.</summary>
    public (Vector3 Position, Quaternion Rotation) PoseOf(PlayerId player)
    {
        var state = Hazard.Blocks.Sim.PlayerOf(player);
        return (state.Position, state.BodyRotation);
    }

    /// <summary>Tombstones stand one metre beside the player, which is within the reach of 6 m of a container.</summary>
    public TombstonePlacement DefaultPlacement(PlayerId player) =>
        new(WorldFactory.HomePlanet, PoseOf(player).Position + Vector3.UnitX, Quaternion.Identity);

    /// <summary>The respawn: the world start where the player stands, so the player stays within reach in tests that follow.</summary>
    public RespawnPoint StayHere(PlayerId player) =>
        new(RespawnSource.WorldStart, new RespawnPose(WorldFactory.HomePlanet, PoseOf(player).Position, PoseOf(player).Rotation));

    public ushort StackLimit(ItemTypeId type) =>
        State.ItemTypes.TryGetKey(type, out var key) && Simulation.Survival is { } content && content.Items.TryGet(key, out var def) ? def.MaxStack : (ushort)500;

    /// <summary>Builds and applies the death of a player as the server does.</summary>
    public AppliedOperation Die(PlayerId player, DeathPenalty penalty, TombstonePlacement? placement = null, RespawnPoint? respawn = null)
    {
        var op = DeathOperationBuilder.Build(State, player, penalty, placement ?? DefaultPlacement(player), respawn ?? StayHere(player), StackLimit);
        return Apply(op);
    }

    /// <summary>The sum of every item type over every container of the world, tombstones included.</summary>
    public long[] Totals()
    {
        var totals = new long[SurvivalContentFactory.HazardItemKeys.Count + 1];
        foreach (var container in State.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                if (!stack.IsEmpty)
                {
                    totals[stack.Type.Value] += stack.Count;
                }
            }
        }

        return totals;
    }

    /// <summary>The non-empty stacks of the three containers of a player, as (container, slot, stack).</summary>
    public List<(ContainerKind Kind, int Slot, ItemStack Stack)> Held(PlayerId player)
    {
        var list = new List<(ContainerKind, int, ItemStack)>();
        foreach (var kind in new[] { ContainerKind.PlayerBag, ContainerKind.Hotbar, ContainerKind.Equipment })
        {
            State.Containers.TryGetOfPlayer(player, kind, out var container);
            for (var i = 0; i < container!.Size; i++)
            {
                if (!container.GetSlot(i).IsEmpty)
                {
                    list.Add((kind, i, container.GetSlot(i)));
                }
            }
        }

        return list;
    }

    /// <summary>The tombstone containers of the world in id order.</summary>
    public IReadOnlyList<Container> Tombstones() => State.Containers.OfKind(ContainerKind.Tombstone);

    public ulong Hash() => StateHasher.Hash(State);

    /// <summary>The vitals of a player as the vitals service of the server sees them: none means a fresh body whose values the first step fills with the maxima.</summary>
    public PlayerVitals NextStepVitals(PlayerId player)
    {
        var vitals = Hazard.VitalsOf(player) ?? new PlayerVitals(float.NaN, float.NaN, default, false);
        return VitalsStep.Advance(vitals, new VitalsTickInput(false, false, 0f, 0f, default), default, VitalsRules.Default, 60, new Tick(10)).Vitals;
    }
}
