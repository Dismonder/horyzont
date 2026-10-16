using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;

namespace Horyzont.Core.Tests.Props;

/// <summary>K6-31 D4, D7: the equal-angle cube-sphere grid (faces 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z).</summary>
public sealed class CubeSphereGridTests
{
    private const float SmallPlanet = 40f;

    [Fact]
    public void CellsPerFace_MultipleOf8_ApproxEightMeters()
    {
        Assert.Equal(784, CubeSphereGrid.CellsPerFace(4000f));
        foreach (var radius in new[] { 1000f, 2500f, 4000f, 12_000f, 50_000f })
        {
            var cells = CubeSphereGrid.CellsPerFace(radius);
            var cellMeters = radius * MathF.PI / 2f / cells;

            Assert.Equal(0, cells % 8);
            Assert.InRange(cellMeters, 7.5f, 8.5f);
        }

        // A tiny planet still has one region per face edge.
        Assert.Equal(8, CubeSphereGrid.CellsPerFace(5f));
        Assert.Equal(8, CubeSphereGrid.CellsPerFace(SmallPlanet));
    }

    [Fact]
    public void DirectionOfCell_InsideFace()
    {
        foreach (var cells in new[] { 8, 784 })
        {
            for (var face = 0; face < 6; face++)
            {
                foreach (var (u, v) in new[] { (0, 0), (cells - 1, 0), (0, cells - 1), (cells - 1, cells - 1), (cells / 2, cells / 2), (3, cells - 4) })
                {
                    var direction = CubeSphereGrid.DirectionOfCell(face, u, v, cells);

                    Assert.InRange(direction.Length(), 0.99999f, 1.00001f);
                    var axis = face / 2;
                    var components = new[] { direction.X, direction.Y, direction.Z };
                    Assert.True(face % 2 == 0 ? components[axis] > 0f : components[axis] < 0f, $"face {face} cell {u},{v}");
                    for (var other = 0; other < 3; other++)
                    {
                        if (other != axis)
                        {
                            Assert.True(MathF.Abs(components[axis]) > MathF.Abs(components[other]), $"face {face} cell {u},{v}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void CellOfDirection_InverseOfDirectionOfCell()
    {
        var random = new Random(31);
        foreach (var cells in new[] { 8, 200, 784 })
        {
            for (var i = 0; i < 3000; i++)
            {
                var face = random.Next(6);
                var u = random.Next(cells);
                var v = random.Next(cells);

                CubeSphereGrid.CellOfDirection(CubeSphereGrid.DirectionOfCell(face, u, v, cells), cells, out var f, out var cu, out var cv);

                Assert.Equal((face, u, v), (f, cu, cv));
            }
        }

        // The length of the direction does not matter.
        CubeSphereGrid.CellOfDirection(CubeSphereGrid.DirectionOfCell(2, 5, 6, 8) * 4000f, 8, out var scaledFace, out var su, out var sv);
        Assert.Equal((2, 5, 6), (scaledFace, su, sv));
    }

    [Fact]
    public void CellsInSphere_ContainsAnchorCell_SortedById()
    {
        const float planet = 4000f;
        var cells = CubeSphereGrid.CellsPerFace(planet);
        var random = new Random(7);
        for (var i = 0; i < 40; i++)
        {
            var direction = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f));
            var center = direction * planet;
            var found = new List<PropId>();

            CubeSphereGrid.CellsInSphere(center, 20f, planet, found);

            CubeSphereGrid.CellOfDirection(direction, cells, out var face, out var u, out var v);
            Assert.Contains(PropId.Create(face, u, v, 0), found);
            Assert.Equal(found.OrderBy(id => id.Value), found);
            Assert.Equal(found.Count, found.Distinct().Count());
            Assert.All(found, id => Assert.Equal(0, id.Slot));
            // 20 m sphere over 8 m cells: at least the 3 x 3 block around the centre, never a whole region.
            Assert.InRange(found.Count, 9, 64);
        }
    }

    [Fact]
    public void CellsInSphere_SmallPlanet_MatchesBruteForce()
    {
        const int cells = 8;
        var center = Vector3.Normalize(new Vector3(1f, 0.6f, -0.3f)) * SmallPlanet;
        var found = new List<PropId>();

        CubeSphereGrid.CellsInSphere(center, 12f, SmallPlanet, found);

        // Every cell whose centre point on the surface lies inside the sphere is listed; no cell farther than a cell diagonal away is.
        var cellMeters = SmallPlanet * MathF.PI / 2f / cells;
        var required = 0;
        for (var face = 0; face < 6; face++)
        {
            for (var u = 0; u < cells; u++)
            {
                for (var v = 0; v < cells; v++)
                {
                    var distance = Vector3.Distance(CubeSphereGrid.DirectionOfCell(face, u, v, cells) * SmallPlanet, center);
                    var listed = found.Contains(PropId.Create(face, u, v, 0));
                    if (distance <= 12f)
                    {
                        required++;
                        Assert.True(listed, $"cell {face},{u},{v} at {distance} m is missing");
                    }

                    if (distance > 12f + (cellMeters * 1.5f))
                    {
                        Assert.False(listed, $"cell {face},{u},{v} at {distance} m is listed");
                    }
                }
            }
        }

        // The sphere covers a handful of cells, so the check above is not vacuous.
        Assert.InRange(required, 3, 40);
        Assert.InRange(found.Count, required, 80);
    }

    [Fact]
    public void FaceSeams_NoGapNoDuplicate()
    {
        // Every cell of a small planet is found by the direction of its centre, so the 6 * 8 * 8 cells tile the sphere without a duplicate.
        const int cells = 8;
        var seen = new HashSet<(int, int, int)>();
        for (var face = 0; face < 6; face++)
        {
            for (var u = 0; u < cells; u++)
            {
                for (var v = 0; v < cells; v++)
                {
                    CubeSphereGrid.CellOfDirection(CubeSphereGrid.DirectionOfCell(face, u, v, cells), cells, out var f, out var cu, out var cv);
                    Assert.Equal((face, u, v), (f, cu, cv));
                    seen.Add((f, cu, cv));
                }
            }
        }

        Assert.Equal(6 * cells * cells, seen.Count);

        // Directions on and next to the seams and the corners land in a valid cell whose centre is close by (no gap).
        var maxAngle = 2f * MathF.PI / 2f / cells;
        var random = new Random(11);
        var seams = new List<Vector3> { new(1f, 1f, 1f), new(1f, 1f, -1f), new(-1f, 1f, 1f), new(1f, -1f, -1f), new(1f, 1f, 0f), new(0f, -1f, 1f), new(-1f, 0f, -1f) };
        for (var i = 0; i < 4000; i++)
        {
            var t = (random.NextSingle() * 2f) - 1f;
            var e = (random.NextSingle() - 0.5f) * 1e-4f;
            seams.Add(new Vector3(1f + e, 1f, t));
            seams.Add(new Vector3(t, 1f + e, -1f));
            seams.Add(new Vector3(-1f, t, 1f + e));
        }

        foreach (var direction in seams)
        {
            CubeSphereGrid.CellOfDirection(direction, cells, out var face, out var u, out var v);

            Assert.InRange(face, 0, 5);
            Assert.InRange(u, 0, cells - 1);
            Assert.InRange(v, 0, cells - 1);
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(direction), CubeSphereGrid.DirectionOfCell(face, u, v, cells)), -1f, 1f));
            Assert.True(angle <= maxAngle, $"{direction} is {angle} rad from the centre of its cell");
        }
    }
}
