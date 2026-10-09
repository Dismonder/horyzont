using System.Reflection;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.World.Constructs;

public sealed class ConstructPlacementTests
{
    [Fact]
    public void OnTerrain_GoldenCases_BitIdenticalToK1()
    {
        var cases = PlacementGolden.ReadTokens("on_terrain.txt");

        Assert.Equal(PlacementGolden.PlacementCases, cases.Length);
        foreach (var t in cases)
        {
            var hit = PlacementGolden.ParseVector(t, 0);
            var look = PlacementGolden.ParseVector(t, 3);
            var ray = PlacementGolden.ParseVector(t, 6);

            PlacementGolden.K1OnTerrain(hit, look, ray, out var origin, out var rotation);

            Assert.Equal(string.Join(' ', t[9..12]), PlacementGolden.F(origin));
            Assert.Equal(string.Join(' ', t[12..16]), PlacementGolden.F(rotation));
        }
    }

    [Fact]
    public void PlayerCapsule_GoldenDecisionsFromK1()
    {
        var cases = PlacementGolden.ReadTokens("player_capsule.txt");
        var f = new SimFixture();
        var player = f.Join();
        var validator = new IntentValidator(f.State, f.Terrain, f.Rules);
        var sphere = typeof(IntentValidator).GetMethod("SphereHitsPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cell = typeof(IntentValidator).GetMethod("CellHitsPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal(PlacementGolden.CapsuleCases, cases.Length);
        var trueCount = 0;
        foreach (var t in cases)
        {
            f.MovePlayer(player, PlacementGolden.ParseVector(t, 1));
            bool actual;
            bool expected;
            if (t[0] == "S")
            {
                actual = (bool)sphere.Invoke(validator, [WorldFactory.HomePlanet, PlacementGolden.ParseVector(t, 4), PlacementGolden.ParseFloat(t[7])])!;
                expected = t[8] == "1";
            }
            else
            {
                var position = new CellPos(int.Parse(t[11]), int.Parse(t[12]), int.Parse(t[13]));
                actual = (bool)cell.Invoke(
                    validator,
                    [WorldFactory.HomePlanet, PlacementGolden.ParseVector(t, 4), PlacementGolden.ParseQuaternion(t, 7), null, position])!;
                expected = t[14] == "1";
            }

            Assert.Equal(expected, actual);
            trueCount += expected ? 1 : 0;
        }

        Assert.InRange(trueCount, 40, 216);
    }
}
