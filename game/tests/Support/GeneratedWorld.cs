using Godot;

using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server;
using Horyzont.Game.Tests.Terrain;
using Horyzont.Persistence;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Game.Tests.Support;

/// <summary>A server terrain over the four-body star system of the real generator (K6-31 C5): only the home planet has an active terrain.</summary>
public sealed class GeneratedWorld : IAsyncDisposable
{
    private GeneratedWorld(WorldState state, VoxelServerTerrain terrain)
    {
        State = state;
        Terrain = terrain;
    }

    public WorldState State { get; }

    public VoxelServerTerrain Terrain { get; }

    /// <summary>Gets a planet of the world that is not the home planet, so it has no active terrain.</summary>
    public PlanetParams Inactive => State.Planets.All.First(p => p.Id != State.HomePlanet);

    public PlanetParams Home => State.Planets.All.First(p => p.Id == State.HomePlanet);

    public static GeneratedWorld Create(EngineTestScene scene, ulong seed)
    {
        var time = new FakeTimeProvider();
        var created = StarSystemGenesis.CreateWorldBodies(seed, GeneratorProfiles.Current);
        var state = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, seed, created.System, created.Bodies, [WorldFactory.StoneBlockKey], TerrainMaterials.Keys);
        var path = TerrainTestWorld.NewPath();
        WorldCreator.Create(path, state, TerrainTestWorld.Options, time);
        var parent = scene.Add(new Node3D { Name = "Terrain" });
        var factory = new VoxelServerTerrainFactory(parent, PlanetTerrainOptions.Server, time, NullLoggerFactory.Instance);
        return new GeneratedWorld(state, (VoxelServerTerrain)factory.Create(path, state));
    }

    public async ValueTask DisposeAsync() => await Terrain.ShutdownAsync();
}
