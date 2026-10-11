using Horyzont.Core.Frames;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;

namespace Horyzont.Core.Tests.Ships.Flight;

/// <summary>Spec 02:77: the speed limit uses the height over the surface of the nearest body, whatever the parent frame.</summary>
public sealed class FlightEnvironmentsTests
{
    private static readonly FlightRules _rules = FlightRules.Default;

    [Fact]
    public void InPlanet_WithSystem_OtherBodyNearer_AltitudeOverNearestBodyAsInSystemFrame()
    {
        var created = StarSystemGenesis.CreateWorldBodies(1, "core:planet/1");
        var model = new StarSystemModel(created.System, created.Bodies);
        var home = model.GetBody(0);
        var moon = model.GetBody(1);
        var towardHome = Vector3D.Normalize(home.Position - moon.Position);
        var point = moon.Position + (towardHome * (moon.RadiusMeters + 1_000));

        var inPlanet = FlightEnvironments.InPlanet(created.Bodies[0], 0, point - home.Position, _rules, model);
        var inSystem = FlightEnvironments.InSystem(model, point, _rules);
        var ownOnly = FlightEnvironments.InPlanet(created.Bodies[0], 0, point - home.Position, _rules);

        Assert.Equal(700, inSystem.Altitude, 3);
        Assert.Equal(inSystem.Altitude, inPlanet.Altitude, 6);
        Assert.True(ownOnly.Altitude > inPlanet.Altitude);
        Assert.Equal(ownOnly with { Altitude = inPlanet.Altitude }, inPlanet);
    }

    [Fact]
    public void InPlanet_WithSystem_OwnPlanetNearest_Unchanged()
    {
        var created = StarSystemGenesis.CreateWorldBodies(1, "core:planet/1");
        var model = new StarSystemModel(created.System, created.Bodies);
        var home = created.Bodies[0];
        var local = new Vector3D(0, home.RadiusMeters + 2_000, 0);

        Assert.Equal(FlightEnvironments.InPlanet(home, 0, local, _rules), FlightEnvironments.InPlanet(home, 0, local, _rules, model));
    }
}
