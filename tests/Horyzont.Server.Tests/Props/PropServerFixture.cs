using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Props;

/// <summary>
/// A simulation over a planet with the prop layout (<see cref="WorldFactory.CreateWithProps"/>), a flat sphere <see cref="Height"/> metres over
/// the planet radius as both the terrain and the generated surface (inside every height band) and the test content of the props: trees and the
/// bush regrow after 10 s (600 ticks), rocks, boulders and crystals never. The tick is driven by hand, in the order of the server loop:
/// the participants first, then <see cref="ServerSimulation.AdvanceTick"/>. Players stand where <see cref="Place"/> puts them (the join
/// spawns them at the north pole, which the tests overwrite).
/// </summary>
internal sealed class PropServerFixture
{
    /// <summary>Height of the surface over the planet radius, metres.</summary>
    public const float Height = 5f;

    /// <summary>Ticks after which the test content regrows a tree: 10 s at 60 ticks per second.</summary>
    public const int RegrowTicks = 600;

    public PropServerFixture(int players = 1, GameRules? rules = null, SurvivalContent? content = null)
    {
        Rules = rules ?? new GameRules();
        Content = content ?? SurvivalContentFactory.CreateTestWithProps();
        Terrain = new FakeTerrainService(Surface) { GeneratedSurfaceRadius = Surface };
        State = WorldFactory.CreateWithProps(players);
        Journal = new InMemoryJournal();
        Sim = new ServerSimulation(State, Terrain, Journal, Rules, Content, NullLogger<ServerSimulation>.Instance);
        Field = new PropField(State, Terrain, Rules);
        for (var n = 1; n <= players; n++)
        {
            Place(Player(n), Pole);
        }
    }

    /// <summary>Radius of the terrain and of the generated surface.</summary>
    public static float Surface => WorldFactory.HomePlanetParams.RadiusMeters + Height;

    public static PlanetId Planet => WorldFactory.HomePlanet;

    /// <summary>The north pole of the surface.</summary>
    public static Vector3 Pole => new(0, Surface, 0);

    /// <summary>The point of the surface opposite to <see cref="Pole"/>, thousands of metres away from every prop near the pole.</summary>
    public static Vector3 Antipode => new(0, -Surface, 0);

    public GameRules Rules { get; }

    public SurvivalContent Content { get; }

    public FakeTerrainService Terrain { get; }

    public WorldState State { get; }

    public InMemoryJournal Journal { get; }

    public ServerSimulation Sim { get; }

    /// <summary>A prop field of the test over the same world and terrain (its own cache; the code under test has its own).</summary>
    public PropField Field { get; }

    public static PlayerId Player(int n = 1) => new((ulong)n);

    /// <summary>The kinds of the layout that regrow with the test content.</summary>
    public static bool Regrows(PropKindV1 kind) => kind is >= PropKindV1.Pine and <= PropKindV1.Bush;

    public void Place(PlayerId player, Vector3 feet)
    {
        Assert.True(State.Players.TryGet(player, out var state));
        Assert.True(Sim.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    /// <summary>The first prop of a kind near the pole.</summary>
    public PropInstance FindProp(PropKindV1 kind) => PropFixtures.FindProp(Field, Planet, Pole, kind);

    /// <summary>All props whose anchor lies within a sphere around the pole, ascending by identifier.</summary>
    public List<PropInstance> PropsAround(float radius)
    {
        var found = new List<PropInstance>();
        Field.CollectInSphere(Planet, Pole, radius, found);
        return found;
    }

    /// <summary>Harvests a prop now (the operation a hit or a dig makes).</summary>
    public AppliedOperation Harvest(PropId prop) => Sim.ApplyServerOperation(new PropHarvestOp(Planet, prop, State.CurrentTick));

    public AppliedOperation Regrow(PropId prop) => Sim.ApplyServerOperation(new PropRegrowOp(Planet, prop));

    public bool IsHarvested(PropId prop) => State.HarvestedProps.Contains(Planet, prop);

    /// <summary>Puts the player at a horizontal distance from the anchor of a prop.</summary>
    public void PlaceBeside(PlayerId player, PropInstance prop, float horizontalMeters) =>
        Place(player, PropFixtures.PointBeside(prop, horizontalMeters, Surface));

    /// <summary>Adds a construct of one stone block with the corner of its cell (0, 0, 0) at <paramref name="origin"/>.</summary>
    public EntityId AddConstruct(Vector3 origin)
    {
        var id = State.EntityIds.PeekNext();
        Sim.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(Planet), origin, Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(1, 0)));
        return id;
    }

    /// <summary>Applies an edit of the terrain around a point (the blocks around it get a version).</summary>
    public void EditTerrainAt(Vector3 point) =>
        Sim.ApplyServerOperation(new TerrainEditOp(Planet, TerrainShape.Sphere, point, 1f, TerrainEditMode.Remove, 0));

    /// <summary>Runs ticks the way the server loop does: participants first, then the simulation advances.</summary>
    public void Run(int ticks, params Action[] participants)
    {
        for (var i = 0; i < ticks; i++)
        {
            foreach (var participant in participants)
            {
                participant();
            }

            Sim.AdvanceTick();
        }
    }
}
