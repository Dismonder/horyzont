using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Props;

/// <summary>
/// A simulation over a planet whose generator profile has a prop layout (<see cref="WorldFactory.CreateWithProps"/>) and a flat sphere
/// <see cref="Height"/> metres above the planet radius, both as the terrain and as the generated surface, so every kind of layout V1 is
/// accepted by its height band. Players stand on the sphere at the north pole until a test moves them; player n holds the test tool in
/// hotbar slot 0 and three gems (not a tool) in slot 1; the tier 2 tool is not given unless asked for.
/// </summary>
internal sealed class PropWorldFixture
{
    /// <summary>Height of the surface over the planet radius, metres: inside the bands of every kind of layout V1.</summary>
    public const float Height = 5f;

    private const ushort NoLimit = 500;
    private const int TicksPerHit = 15;

    /// <summary>
    /// Creates the fixture.
    /// </summary>
    /// <param name="players">Joined players.</param>
    /// <param name="content">Survival content; the test content with props when null.</param>
    /// <param name="state">World; <see cref="WorldFactory.CreateWithProps"/> when null.</param>
    /// <param name="toolKey">Item given to every player in hotbar slot 0; null gives nothing.</param>
    /// <param name="survival">False runs the simulation without survival content, as in K2.</param>
    public PropWorldFixture(
        int players = 1, SurvivalContent? content = null, WorldState? state = null, string? toolKey = SurvivalContentFactory.ToolKey, bool survival = true)
    {
        Content = content ?? SurvivalContentFactory.CreateTestWithProps();
        Terrain = new FakeTerrainService(Surface) { GeneratedSurfaceRadius = Surface };
        Sim = new SimFixture(terrain: Terrain, state: state ?? WorldFactory.CreateWithProps(players), survival: survival ? Content : null);
        Field = new PropField(State, Terrain);
        for (var n = 1; n <= players; n++)
        {
            Sim.MovePlayer(new PlayerId((ulong)n), new Vector3(0, Surface + 0.1f, 0));
            if (toolKey is not null)
            {
                GiveHotbar(n, 0, toolKey, 1, 1);
                if (toolKey == SurvivalContentFactory.ToolKey)
                {
                    GiveHotbar(n, 1, SurvivalContentFactory.GemKey, 3, 5);
                }
            }
        }
    }

    /// <summary>Radius of the terrain and of the generated surface.</summary>
    public static float Surface => WorldFactory.HomePlanetParams.RadiusMeters + Height;

    public static PlanetId Planet => WorldFactory.HomePlanet;

    public SurvivalContent Content { get; }

    public FakeTerrainService Terrain { get; }

    public SimFixture Sim { get; }

    public WorldState State => Sim.State;

    /// <summary>A prop field of the test over the same world and terrain as the simulation (its own cache).</summary>
    public PropField Field { get; }

    public static PlayerId Player(int n = 1) => new((ulong)n);

    public ItemTypeId Item(string key) => State.ItemTypes.TryGetId(key, out var id) ? id : throw new ArgumentException($"No item '{key}' in the world.", nameof(key));

    public static ContainerId Hotbar(int player = 1) => WorldFactory.HotbarOf(player);

    public static ContainerId Bag(int player = 1) => WorldFactory.BagOf(player);

    public AppliedOperation Apply(Operation op) => Sim.Sim.ApplyServerOperation(op);

    public void GiveHotbar(int player, int slot, string itemKey, int count, int limit = NoLimit) =>
        Apply(new ItemCreateOp(new SlotRef(Hotbar(player), (ushort)slot), Item(itemKey), (ushort)count, (ushort)limit, ItemSource.Admin));

    public void GiveBag(int player, int slot, string itemKey, int count, int limit = NoLimit) =>
        Apply(new ItemCreateOp(new SlotRef(Bag(player), (ushort)slot), Item(itemKey), (ushort)count, (ushort)limit, ItemSource.Admin));

    /// <summary>Fills slots <paramref name="from"/>.. of a container with single test tools, so nothing more fits there.</summary>
    public void FillWithTools(ContainerId container, int from, int count)
    {
        var tool = Item(SurvivalContentFactory.ToolKey);
        Apply(new CompoundOp([.. Enumerable.Range(from, count).Select(i => (Operation)new ItemCreateOp(new SlotRef(container, (ushort)i), tool, 1, 1, ItemSource.Admin))]));
    }

    /// <summary>Items of a type in the bag and the hotbar of a player.</summary>
    public int Count(string itemKey, int player = 1)
    {
        var type = Item(itemKey);
        var total = 0;
        foreach (var id in new[] { Bag(player), Hotbar(player) })
        {
            foreach (var stack in State.Containers.TryGet(id, out var container) ? container.Slots.ToArray() : [])
            {
                if (!stack.IsEmpty && stack.Type == type)
                {
                    total += stack.Count;
                }
            }
        }

        return total;
    }

    /// <summary>The first prop of a kind near the north pole.</summary>
    public PropInstance FindProp(PropKindV1 kind) => PropFixtures.FindProp(Field, Planet, new Vector3(0, Surface, 0), kind);

    /// <summary>Puts the player on the surface at a horizontal distance from the anchor of a prop.</summary>
    public void StandBeside(PropInstance prop, float horizontalMeters, int player = 1) =>
        Sim.MovePlayer(Player(player), PropFixtures.PointBeside(prop, horizontalMeters, Surface + 0.05f));

    /// <summary>Puts the player in the air, <paramref name="heightMeters"/> above the anchor and beside the prop's axis.</summary>
    public void StandAbove(PropInstance prop, float horizontalMeters, float heightMeters, int player = 1) =>
        Sim.MovePlayer(Player(player), prop.Anchor + (PropFixtures.SideOf(prop) * horizontalMeters) + (prop.Up * heightMeters));

    /// <summary>Puts the player under the surface beside the prop, so the terrain lies between the eye and the prop.</summary>
    public void StandUnderground(PropInstance prop, float horizontalMeters, int player = 1) =>
        Sim.MovePlayer(Player(player), Vector3.Normalize(prop.Anchor + (PropFixtures.SideOf(prop) * horizontalMeters)) * (Surface - 3f));

    /// <summary>Waits for a token of the prop bucket (a quarter of a second), then submits a hit.</summary>
    public IntentResult Hit(PropId prop, ushort toolSlot = 0, int player = 1)
    {
        Sim.Ticks(TicksPerHit);
        return Submit(new PropHitIntent(Planet, prop, toolSlot), player);
    }

    public IntentResult Submit(Intent intent, int player = 1) => Sim.Sim.Submit(Player(player), intent);

    /// <summary>A dig of the ground near the north pole, within reach of a player standing there.</summary>
    public IntentResult DigGround(int player = 1) => Submit(Sim.Dig(Player(player), Sim.Ground(2)), player);

    /// <summary>A dig aimed at a point of the surface from where the player stands (the radius of the edit is <paramref name="radius"/>).</summary>
    public IntentResult DigAt(Vector3 target, float radius = 1f, int player = 1) => Submit(Sim.Dig(Player(player), target, radius), player);

    /// <summary>A fill aimed at a point of the surface from where the player stands.</summary>
    public IntentResult FillAt(Vector3 target, byte material, float radius = 1f, int player = 1) =>
        Submit(Sim.Fill(Player(player), target, radius, material), player);
}
