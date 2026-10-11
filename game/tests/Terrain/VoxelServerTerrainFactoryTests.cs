using System.Reflection;

using GdUnit4;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Server;
using Horyzont.Game.Terrain;
using Horyzont.Game.Tests.Support;
using Horyzont.Persistence;
using Horyzont.Persistence.Terrain;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public class VoxelServerTerrainFactoryTests
{
    [TestCase(false, Timeout = 240000)]
    [TestCase(true, Timeout = 240000)]
    [RequireGodotRuntime]
    public async Task Create_FirstPlanetReadingSecondCreationThrows_ClosesStoreOnlyAfterDrain(bool drainTimesOut)
    {
        await using var scene = EngineTestScene.Create();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var time = new FakeTimeProvider();
        var first = TerrainTestWorld.Planet;
        var second = first with { Id = new PlanetId(2) };
        var state = WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [first, second], [WorldFactory.StoneBlockKey], TerrainMaterials.Keys);
        var path = TerrainTestWorld.NewPath();
        WorldCreator.Create(path, state, TerrainTestWorld.Options, time);
        var simulation = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        simulation.ApplyServerOperation(new TerrainEditOp(first.Id, TerrainShape.Sphere, new System.Numerics.Vector3(0, 4000, 0), 1, TerrainEditMode.Remove, 0));
        var key = state.TerrainVersions.All.First().Key;
        var expected = new InvalidOperationException("Planet terrain creation failed");
        Task<int>? reading = null;
        TerrainStream? stream = null;
        TerrainBlockStore? storeForCleanup = null;
        // K5-22: the server terrain parent must be a Node3D (the fallback parent of planet nodes while there are no frame nodes).
        var parent = scene.Add(new Node3D { Name = "Terrain" });
        var factory = new VoxelServerTerrainFactory(parent, PlanetTerrainOptions.Server, time, NullLoggerFactory.Instance)
        {
            StoreCreated = store =>
            {
                storeForCleanup = store;
                // Reuse the persistence test seam without changing that assembly's visibility or any protected contract.
                var beforeQuery = typeof(TerrainBlockStore).GetProperty("BeforeQuery", BindingFlags.Instance | BindingFlags.NonPublic)!;
                beforeQuery.SetValue(store, (Action<TerrainBlockKey>)(_ =>
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Test did not release the first planet read");
                    }
                }));
            },
            PlanetCreated = node =>
            {
                stream = node.Stream;
                // All planets are now built outside the tree. Exercise an already active stream read explicitly so the
                // factory's actual catch path must still protect the store when the construction fails.
                reading = Task.Run(() =>
                {
                    using var buffer = new VoxelBuffer();
                    buffer.Create(16, 16, 16);
                    return node.Stream._LoadVoxelBlock(buffer, new Vector3I(key.X, key.Y, key.Z), 0);
                });
                if (!entered.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The first planet did not enter its read");
                }

                // K5-22: only the home planet gets a node at start, so the construction fails right after it was made.
                throw expected;
            },
        };

        try
        {
            Exception? failure = null;
            try
            {
                factory.Create(path, state);
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            AssertThat(ReferenceEquals(expected, failure)).IsTrue();
            AssertThat(stream!.IsClosed).IsTrue();
            AssertThat(stream.SourceReads).IsEqual(1);
            AssertThat(factory.FailureCleanup.IsCompleted).IsFalse();
            AssertThat(factory.FailedStoreCount).IsEqual(1);
            AssertThat(parent.GetChildCount()).IsEqual(0);
            if (drainTimesOut)
            {
                time.Advance(TimeSpan.FromSeconds(6));
                await factory.FailureCleanup;
                AssertThat(factory.FailedStoreCount).IsEqual(1);
            }

            release.Set();
            AssertThat(await reading!).IsEqual((int)VoxelStream.ResultCode.Error);
            await factory.FailureCleanup;
            AssertThat(factory.FailedStoreCount).IsEqual(drainTimesOut ? 1 : 0);
        }
        finally
        {
            release.Set();
            if (reading is not null)
            {
                await reading;
            }

            await factory.FailureCleanup;
            storeForCleanup?.Close(); // test cleanup only, after the blocked read has ended and all assertions ran
        }
    }
}
