using System.Numerics;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

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
            ConstructPlacement.OnTerrain(
                PlacementGolden.ParseVector(t, 0), PlacementGolden.ParseVector(t, 3), PlacementGolden.ParseVector(t, 6),
                out var origin, out var rotation);

            Assert.Equal(string.Join(' ', t[9..12]), PlacementGolden.F(origin));
            Assert.Equal(string.Join(' ', t[12..16]), PlacementGolden.F(rotation));
        }
    }

    [Fact]
    public void PlayerCapsule_GoldenDecisionsFromK1()
    {
        var cases = PlacementGolden.ReadTokens("player_capsule.txt");
        var rules = new GameRules();

        Assert.Equal(PlacementGolden.CapsuleCases, cases.Length);
        var trueCount = 0;
        foreach (var t in cases)
        {
            var feet = PlacementGolden.ParseVector(t, 1);
            bool actual;
            bool expected;
            if (t[0] == "S")
            {
                actual = PlayerCapsule.IntersectsSphere(feet, PlacementGolden.ParseVector(t, 4), PlacementGolden.ParseFloat(t[7]), rules);
                expected = t[8] == "1";
            }
            else
            {
                var position = new CellPos(int.Parse(t[11]), int.Parse(t[12]), int.Parse(t[13]));
                actual = PlayerCapsule.IntersectsCell(
                    feet, PlacementGolden.ParseVector(t, 4), PlacementGolden.ParseQuaternion(t, 7), position, rules);
                expected = t[14] == "1";
            }

            Assert.Equal(expected, actual);
            trueCount += expected ? 1 : 0;
        }

        Assert.InRange(trueCount, 40, 216);
    }

    // Feet at (0, 1, 0): up = +Y, capsule axis from y = 1.4 to y = 2.4, radius 0.4 (values written by hand, not from the helper).
    private static readonly Vector3 _feet = new(0f, 1f, 0f);

    [Theory]
    [InlineData(1.4005f, 1.8f, false)] // side: axis distance 1.4005 > 0.4 + 1
    [InlineData(1.3995f, 1.8f, true)]
    [InlineData(0f, 3.801f, false)] // above the top sphere centre (2.4) by 1.401
    [InlineData(0f, 3.799f, true)]
    [InlineData(0f, -0.001f, false)] // below the bottom sphere centre (1.4) by 1.401
    [InlineData(0f, 0.001f, true)]
    public void PlayerCapsule_IndependentGeometry_SphereOfRadiusOne(float x, float y, bool expected)
    {
        Assert.Equal(expected, PlayerCapsule.IntersectsSphere(_feet, new Vector3(x, y, 0f), 1f, new GameRules()));
    }

    [Theory]
    [InlineData(0.401f, false)] // axis-aligned cell 1 mm outside the radius
    [InlineData(0.399f, true)]
    public void PlayerCapsule_IndependentGeometry_AlignedCellOneMillimetreAway(float originX, bool expected)
    {
        var origin = new Vector3(originX, 1.5f, -0.5f);

        Assert.Equal(expected, PlayerCapsule.IntersectsCell(_feet, origin, Quaternion.Identity, new CellPos(0, 0, 0), new GameRules()));
    }

    [Theory]
    [InlineData(0.6671f, false)] // the cell turned 45 degrees: its edge is 0.4 + 0.001 from the axis; a bounding box would hit
    [InlineData(0.6642f, true)]
    public void PlayerCapsule_IndependentGeometry_CellTurned45DegreesGrazesWithEdge(float relativeZ, bool expected)
    {
        // Diamond of the cell in the XZ plane: vertices O, O + (c, -c), O + (c, c), O + (2c, 0) with c = sqrt(2)/2. The axis point
        // lies at (0.1, relativeZ) from O, above the edge z = x, whose distance is (relativeZ - 0.1) / sqrt(2).
        var origin = new Vector3(-0.1f, 1.5f, -relativeZ);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);

        Assert.Equal(expected, PlayerCapsule.IntersectsCell(_feet, origin, rotation, new CellPos(0, 0, 0), new GameRules()));
    }
}
