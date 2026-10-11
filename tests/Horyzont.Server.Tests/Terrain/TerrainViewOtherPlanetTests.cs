using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Terrain;
using Horyzont.Server.Tests.Frames;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Terrain;

/// <summary>K5-22 decision 8: a client may view the terrain of a planet its body is near, through the frame graph.</summary>
public sealed class TerrainViewOtherPlanetTests
{
    private const ulong Seed = 21;
    private static readonly EntityId _ship = new(10);
    private static readonly FrameRef _system = FrameRef.Of(new SystemId(1));
    private static readonly Vector3D _outward = new(1, 0, 0);

    [Fact]
    public void TerrainView_NearbyPlanetFromSystemFrame_Accepted()
    {
        var moon = Model().GetBody(1);
        var rig = Rig(moon.Position + (_outward * (3.5 * moon.RadiusMeters)), out var player, out var home, out var feet);

        var result = TerrainViewFeet.Resolve(rig.Graph, rig.State, player.Frame, home, feet, moon.Planet, out var inMoon, out var extra);

        Assert.Equal(TerrainFeet.Found, result);
        Assert.InRange(inMoon.Length(), (3.5 * moon.RadiusMeters) - 20, (3.5 * moon.RadiusMeters) + 20);
        Assert.True(extra > 0f);
    }

    [Fact]
    public void TerrainView_FarPlanet_Rejected()
    {
        var bodies = Model();
        var moon = bodies.GetBody(1);
        var rig = Rig(moon.Position + (_outward * (4.6 * moon.RadiusMeters)), out var player, out var home, out var feet);

        Assert.Equal(TerrainFeet.TooFar, TerrainViewFeet.Resolve(rig.Graph, rig.State, player.Frame, home, feet, moon.Planet, out _, out _));
        Assert.Equal(TerrainFeet.TooFar, TerrainViewFeet.Resolve(rig.Graph, rig.State, player.Frame, home, feet, bodies.GetBody(2).Planet, out _, out _));
        Assert.Equal(TerrainFeet.TooFar, TerrainViewFeet.Resolve(rig.Graph, rig.State, player.Frame, home, feet, new PlanetId(999), out _, out _));
    }

    [Fact]
    public void TerrainView_PassengerCollisionViewerAtShipCentre_Accepted()
    {
        var home = Model().GetBody(0);
        var probe = new FrameRig(TestShips.CreateStarSystemWorld(Seed, new TestShip(_ship, FrameRef.Of(home.Planet), new Vector3(0f, (float)home.RadiusMeters + 50f, 0f), Quaternion.Identity, Size: 8)));
        var player = probe.Join();
        Assert.True(probe.Transitions.TryReparent(player.Entity, FrameRef.Of(_ship), new CellPos(1, 0, 1), out _, out _));
        Assert.True(probe.Graph.TryExpress(player.Frame, player.Position, FrameRef.Of(home.Planet), out var feetD));
        var feet = new Vector3((float)feetD.X, (float)feetD.Y, (float)feetD.Z);

        var result = TerrainViewFeet.Resolve(probe.Graph, probe.State, player.Frame, home.Planet, feet, home.Planet, out var inHome, out var extra);

        // An 8 x 1 x 8 floor: half its diagonal is the extra tolerance, so a collision viewer at the ship centre is accepted.
        Assert.Equal(TerrainFeet.Found, result);
        Assert.Equal(feet, inHome);
        Assert.Equal(new Vector3(8f, 1f, 8f).Length() / 2f, extra, 3);
        var centre = new Vector3(4f, (float)home.RadiusMeters + 50f, 4f);
        Assert.True(Vector3.Distance(centre, inHome) <= TerrainReplicationPolicy.ViewerTolerance + extra);
    }

    private static FrameRig Rig(Vector3D shipPosition, out PlayerState player, out PlanetId home, out Vector3 feet)
    {
        // The ship starts in the system frame where the joining player spawns (spawns are deterministic), takes the player aboard,
        // then moves to the tested position with its passenger.
        var probe = new FrameRig(TestShips.CreateStarSystemWorld(Seed));
        var spawn = probe.WorldPose(probe.Join()).Position;
        var rig = new FrameRig(TestShips.CreateStarSystemWorld(
            Seed, new TestShip(_ship, _system, new Vector3((float)spawn.X, (float)spawn.Y, (float)spawn.Z), Quaternion.Identity)));
        player = rig.Join();
        Assert.True(rig.Transitions.TryReparent(player.Entity, FrameRef.Of(_ship), new CellPos(1, 0, 1), out _, out _));
        Assert.True(rig.State.Constructs.TryGet(_ship, out var ship));
        Assert.True(rig.Sim.UpdateConstructKinematics(
            _ship, _system, ship.ParentSeq, new Vector3((float)shipPosition.X, (float)shipPosition.Y, (float)shipPosition.Z), Quaternion.Identity));
        home = rig.State.HomePlanet;

        // A body in a ship in the system frame stays on its last planet, posed globally (K5-20 decision 15).
        Assert.True(rig.Graph.TryExpress(player.Frame, player.Position, FrameRef.Of(home), out var feetD));
        feet = new Vector3((float)feetD.X, (float)feetD.Y, (float)feetD.Z);
        return rig;
    }

    private static StarSystemModel Model()
    {
        var created = StarSystemGenesis.CreateWorldBodies(Seed, "core:test/1");
        return new StarSystemModel(created.System, created.Bodies);
    }
}
