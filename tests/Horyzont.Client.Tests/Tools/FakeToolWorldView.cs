using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Tools;

/// <summary>
/// World view over a real <see cref="WorldState"/> (constructs created through a <see cref="ServerSimulation"/>) and a
/// <see cref="FakeTerrainService"/> sphere. The same simulation answers intents, so a test can compare a client hint with the
/// server reason. A raycast result can be forced for tests of tie and reach rules.
/// </summary>
internal sealed class FakeToolWorldView : IToolWorldView
{
    public FakeToolWorldView(float surfaceRadius = 4000f)
    {
        State = WorldFactory.CreateDefault();
        Terrain = new FakeTerrainService(surfaceRadius);
        Rules = new GameRules();
        Sim = new ServerSimulation(State, Terrain, new InMemoryJournal(), Rules, NullLogger<ServerSimulation>.Instance);
        State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone);
        Stone = stone;
    }

    public WorldState State { get; }

    public FakeTerrainService Terrain { get; }

    public GameRules Rules { get; }

    public ServerSimulation Sim { get; }

    public ushort Stone { get; }

    public List<Vector3> OtherFeet { get; } = [];

    public TerrainHit? TerrainOverride { get; set; }

    public ConstructHit? ConstructOverride { get; set; }

    public static PlanetId Planet => WorldFactory.HomePlanet;

    public PlayerId Join(ulong identity = 1)
    {
        var op = Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, identity), "tester");
        Sim.ApplyServerOperation(op);
        return op.Player;
    }

    public void MovePlayer(PlayerId id, Vector3 feet)
    {
        State.Players.TryGet(id, out var player);
        Assert.True(Sim.UpdatePlayerKinematics(id, player!.Frame, player.ParentSeq, feet, Quaternion.Identity, 0f));
    }

    public Vector3 ServerEye(PlayerId id) => State.Players.TryGet(id, out var player) ? player.EyePosition(Rules) : throw new InvalidOperationException();

    public Vector3 Ground(float x, float z = 0f) => new(x, MathF.Sqrt((Terrain.SurfaceRadius * Terrain.SurfaceRadius) - (x * x) - (z * z)), z);

    public EntityId AddConstruct(Vector3 origin, Quaternion? rotation = null, params CellPos[] extraCells)
    {
        var id = State.EntityIds.PeekNext();
        Sim.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(Planet), origin, rotation ?? Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(Stone, 0)));
        foreach (var cell in extraCells)
        {
            Sim.ApplyServerOperation(new BlockSetOp(id, cell, BlockState.Create(Stone, 0)));
        }

        return id;
    }

    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters) => State.Planets.TryGet(planet, out parameters);

    public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        if (TerrainOverride is { } forced)
        {
            hit = forced;
            return forced.Distance <= maxDistance;
        }

        return Terrain.Raycast(planet, origin, direction, maxDistance, out hit);
    }

    public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit)
    {
        if (ConstructOverride is { } forced)
        {
            hit = forced;
            return forced.Distance <= maxDistance;
        }

        return ConstructRaycast.Raycast(State.Constructs, FrameRef.Of(planet), origin, direction, maxDistance, out hit);
    }

    public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value) => State.Constructs.TryGet(construct, out value);

    public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
    {
        origin = default;
        rotation = Quaternion.Identity;
        return State.Constructs.TryGet(construct, out var value)
            && State.TryResolvePlanet(value.Parent, out var planet)
            && BlockOverlap.TryGetConstructPose(State.Constructs, value, FrameRef.Of(planet), out origin, out rotation);
    }

    public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) =>
        BlockOverlap.OverlapsConstruct(State.Constructs, FrameRef.Of(planet), center, rotation, skip);

    public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination)
    {
        var count = Math.Min(destination.Length, OtherFeet.Count);
        for (var i = 0; i < count; i++)
        {
            destination[i] = OtherFeet[i];
        }

        return count;
    }
}
