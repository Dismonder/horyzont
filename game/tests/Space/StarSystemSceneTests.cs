using GdUnit4;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Game.Frames;
using Horyzont.Game.Planet.Sky;
using Horyzont.Game.Tests.Support;
using Horyzont.Testing.Ships;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Space;

/// <summary>K5-21 step 10: the planet nodes stand at the poses of the star system; the sun follows the cycle of the system.</summary>
[TestSuite]
public class StarSystemSceneTests
{
    private const int TickRate = 60;

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task FrameNodes_StarSystem_PlanetNodesAtPlacement()
    {
        await using var scene = EngineTestScene.Create();
        var state = TestShips.CreateStarSystemWorld(21);
        var frames = new FrameNodes { Name = "Frames" };
        frames.Initialize(new WorldStateFramePoses(state, new WorldStatePlanetPlacement(state)));
        scene.Add(frames);

        var system = state.StarSystem!;
        foreach (var body in system.Bodies)
        {
            AssertThat(frames.TryGetNode(FrameRef.Of(body.Planet), out _)).IsTrue();
        }

        frames.Compose();
        foreach (var body in system.Bodies)
        {
            AssertThat(frames.TryGetNode(FrameRef.Of(body.Planet), out var node)).IsTrue();
            var origin = node.GlobalTransform.Origin;
            var expected = body.Pose.Position;
            AssertThat(Vector3D.Distance(new Vector3D(origin.X, origin.Y, origin.Z), expected)).IsLess(1e-9);
        }

        // The home planet stays at the origin, so its terrain does not move.
        AssertThat(frames.TryGetNode(FrameRef.Of(state.HomePlanet), out var home)).IsTrue();
        AssertThat((double)home.GlobalTransform.Origin.Length()).IsLess(1e-9);
        await scene.WaitFrames(1);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task SunLight_FollowsStarSystemCycle()
    {
        await using var scene = EngineTestScene.Create();
        var state = TestShips.CreateStarSystemWorld(21);
        var cycle = state.StarSystem!.SunCycleSeconds;
        var home = state.Planets.TryGet(state.HomePlanet, out var planet) ? planet : throw new InvalidOperationException();
        var light = scene.Add(new SunLight());

        foreach (var tick in new ulong[] { 0, 7000, 27000, 54000, 95555, ulong.MaxValue - 3 })
        {
            // The game formula is the Core one, bit for bit, also through the day of the home planet (V1: cycle = day).
            var sun = StarSystemMath.SunDirection(tick, TickRate, cycle);
            var game = DayNightCycle.SunDirection(tick, TickRate, cycle);
            AssertThat(game).IsEqual(new System.Numerics.Vector3((float)sun.X, (float)sun.Y, (float)sun.Z));
            AssertThat(DayNightCycle.SunDirection(tick, TickRate, home)).IsEqual(game);

            light.Apply(tick, TickRate, cycle);
            var forward = -light.GlobalTransform.Basis.Z;
            AssertThat(Math.Abs(forward.X + sun.X)).IsLess(1e-5);
            AssertThat(Math.Abs(forward.Y + sun.Y)).IsLess(1e-5);
            AssertThat(Math.Abs(forward.Z + sun.Z)).IsLess(1e-5);
        }

        await scene.WaitFrames(1);
    }
}
