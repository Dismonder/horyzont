using System.Numerics;

using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Core.Tests;

/// <summary>
/// A world with the default planet (radius 4000 m) whose fake terrain is a sphere. A joined player stands on the north
/// pole at (0, R + 0.1, 0), so the planet axes are the local axes: +Y up, ground near y = R.
/// </summary>
internal sealed class SimFixture
{
    public SimFixture(
        GameRules? rules = null, FakeTerrainService? terrain = null, WorldState? state = null, IJournalSink? journal = null,
        BlockContentTable? content = null, SurvivalContent? survival = null, VitalsRules? vitals = null, HazardRules? hazards = null)
    {
        Rules = rules ?? new GameRules();
        Terrain = terrain ?? new FakeTerrainService();
        State = state ?? WorldFactory.CreateDefault();
        Journal = new InMemoryJournal();
        Sim = new ServerSimulation(State, Terrain, journal ?? Journal, Rules, NullLogger<ServerSimulation>.Instance, content ?? BlockContentTable.Empty, survival, vitals, hazards);
        State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        Stone = stone;
    }

    public GameRules Rules { get; }

    public FakeTerrainService Terrain { get; }

    public WorldState State { get; }

    public InMemoryJournal Journal { get; }

    public ServerSimulation Sim { get; }

    public ushort Stone { get; }

    public static PlanetId Planet => WorldFactory.HomePlanet;

    public float Radius => Terrain.SurfaceRadius;

    public PlayerId Join(ulong identity = 1, string name = "player")
    {
        var op = Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), name);
        Sim.ApplyServerOperation(op);
        return op.Player;
    }

    public PlayerState PlayerOf(PlayerId id) => State.Players.TryGet(id, out var player) ? player : throw new InvalidOperationException();

    public Vector3 Eye(PlayerId id) => PlayerOf(id).EyePosition(Rules);

    /// <summary>Point on the terrain sphere above planet-frame (x, z).</summary>
    public Vector3 Ground(float x, float z = 0) => new(x, MathF.Sqrt((Radius * Radius) - (x * x) - (z * z)), z);

    public static Vector3 Direction(Vector3 from, Vector3 to) => Vector3.Normalize(to - from);

    public TerrainToolIntent Dig(PlayerId player, Vector3 target, float radius = 0.5f) =>
        new(Planet, Eye(player), Direction(Eye(player), target), radius, TerrainEditMode.Remove, 0);

    public TerrainToolIntent Fill(PlayerId player, Vector3 target, float radius = 0.5f, byte material = 1) =>
        new(Planet, Eye(player), Direction(Eye(player), target), radius, TerrainEditMode.Add, material);

    /// <summary>Place on the terrain; LookForward −Z keeps the construct axes aligned with the planet axes.</summary>
    public PlaceBlockIntent PlaceOnTerrain(PlayerId player, Vector3 target) =>
        new(new TerrainTarget(Planet, Eye(player), Direction(Eye(player), target), -Vector3.UnitZ), Stone, 0);

    public EntityId BuildOnTerrain(PlayerId player, float x, float z = 0)
    {
        var result = Sim.Submit(player, PlaceOnTerrain(player, Ground(x, z)));
        Assert.True(result.IsAccepted, $"PlaceOnTerrain rejected: {result.Reason}");
        return ((ConstructCreateOp)result.Applied!.Operation).Construct;
    }

    public void SetBlock(EntityId construct, CellPos cell) =>
        Sim.ApplyServerOperation(new BlockSetOp(construct, cell, BlockState.Create(Stone, 0)));

    public void Ticks(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Sim.AdvanceTick();
        }
    }

    public void MovePlayer(PlayerId id, Vector3 feet)
    {
        var player = PlayerOf(id);
        Assert.True(Sim.UpdatePlayerKinematics(id, player.Frame, player.ParentSeq, feet, Quaternion.Identity, 0f));
    }
}
