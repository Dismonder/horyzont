using System.Numerics;

using Horyzont.Core.Survival;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-34, D11: the 26 directions of the shelter rays and the local basis they are turned into.</summary>
public sealed class ShelterDirectionsTests
{
    private static Vector3 Normalized(float x, float y, float z) => Vector3.Normalize(new Vector3(x, y, z));

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, was {actual}");

    [Fact]
    public void TwentySixUnitDirections_Distinct()
    {
        Assert.Equal(26, ShelterDirections.Count);

        for (var i = 0; i < ShelterDirections.Count; i++)
        {
            var direction = ShelterDirections.Local(i);
            Assert.Equal(1f, direction.Length(), 5);

            // Every component is -1, 0 or 1 before the normalisation, i.e. all non-zero components have the same absolute value.
            var absolute = new[] { MathF.Abs(direction.X), MathF.Abs(direction.Y), MathF.Abs(direction.Z) }.Where(v => v > 1e-6f).ToArray();
            Assert.All(absolute, v => Assert.Equal(absolute[0], v, 5));
            for (var j = i + 1; j < ShelterDirections.Count; j++)
            {
                Assert.True(Vector3.Dot(direction, ShelterDirections.Local(j)) < 0.9999f, $"{i} and {j} point the same way");
            }
        }

        // The order of the loops dy, dz, dx from -1, with the zero vector left out.
        AssertNear(Normalized(-1, -1, -1), ShelterDirections.Local(0));
        AssertNear(Normalized(0, -1, -1), ShelterDirections.Local(1));
        AssertNear(Normalized(1, -1, -1), ShelterDirections.Local(2));
        AssertNear(Normalized(0, -1, 0), ShelterDirections.Local(4));
        AssertNear(Normalized(1, -1, 1), ShelterDirections.Local(8));
        AssertNear(Normalized(-1, 0, -1), ShelterDirections.Local(9));
        AssertNear(Normalized(-1, 0, 0), ShelterDirections.Local(12));
        AssertNear(Normalized(1, 0, 0), ShelterDirections.Local(13));
        AssertNear(Normalized(1, 0, 1), ShelterDirections.Local(16));
        AssertNear(Normalized(-1, 1, -1), ShelterDirections.Local(17));
        AssertNear(Normalized(1, 1, 1), ShelterDirections.Local(25));

        Assert.Throws<ArgumentOutOfRangeException>(() => ShelterDirections.Local(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShelterDirections.Local(26));
    }

    [Fact]
    public void UpRay_IsLocalUp()
    {
        Assert.Equal(21, ShelterDirections.UpIndex);
        AssertNear(new Vector3(0, 1, 0), ShelterDirections.Local(ShelterDirections.UpIndex));

        // Whatever the up direction of the point, the ray of the index UpIndex is that direction.
        foreach (var up in new[]
        {
            new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(0, -1, 0),
            Normalized(1, 1, 1), Normalized(-3, 2, 5), Normalized(0.95f, 0.31f, 0.02f), Normalized(0.89f, 0.1f, 0.44f),
        })
        {
            AssertNear(up, ShelterDirections.Direction(ShelterDirections.UpIndex, up));
        }
    }

    [Fact]
    public void Basis_OrthonormalNearPolesAndEquator()
    {
        var ups = new List<Vector3>
        {
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),

            // On both sides of the switch of the reference axis (|up.X| = 0.9), and next to the poles of the sphere.
            Normalized(0.9f, 0.436f, 0f), Normalized(0.91f, 0.4f, 0.1f), Normalized(0.899f, 0.3f, 0.3f), Normalized(0.9001f, 0f, 0.4358f),
            Normalized(0.001f, 1f, 0f), Normalized(0f, 1f, 0.001f), Normalized(0.999f, 0.01f, 0.01f), Normalized(-0.95f, 0.1f, 0.2f),
        };
        for (var i = 0; i < 200; i++)
        {
            var t = (i + 0.5f) / 200f;
            var y = 1f - (2f * t);
            var r = MathF.Sqrt(1f - (y * y));
            var phi = i * 2.399963f;
            ups.Add(new Vector3(r * MathF.Cos(phi), y, r * MathF.Sin(phi)));
        }

        foreach (var up in ups)
        {
            ShelterDirections.Basis(up, out var x, out var y, out var z);

            AssertNear(up, y, 1e-5f);
            Assert.Equal(1f, x.Length(), 4);
            Assert.Equal(1f, z.Length(), 4);
            Assert.True(MathF.Abs(Vector3.Dot(x, y)) < 1e-4f, $"x.y at {up}");
            Assert.True(MathF.Abs(Vector3.Dot(y, z)) < 1e-4f, $"y.z at {up}");
            Assert.True(MathF.Abs(Vector3.Dot(x, z)) < 1e-4f, $"x.z at {up}");
            AssertNear(Vector3.Cross(x, y), z);

            // Every ray is a unit vector and the 26 stay distinct after the rotation.
            var seen = new List<Vector3>();
            for (var index = 0; index < ShelterDirections.Count; index++)
            {
                var direction = ShelterDirections.Direction(index, up);
                Assert.True(float.IsFinite(direction.X) && float.IsFinite(direction.Y) && float.IsFinite(direction.Z), $"ray {index} at {up}");
                Assert.Equal(1f, direction.Length(), 4);
                Assert.DoesNotContain(seen, other => Vector3.Dot(other, direction) > 0.9999f);
                seen.Add(direction);
            }
        }
    }
}
