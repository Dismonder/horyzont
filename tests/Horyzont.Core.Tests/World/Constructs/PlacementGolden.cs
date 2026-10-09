using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.World.Constructs;

/// <summary>
/// Golden data of K2-13 W1. The files in Golden/ConstructPlacement were recorded with the K1 code (IntentValidator.OrientOnSurface,
/// the origin formula of ValidatePlaceOnTerrain, IntentValidator.SphereHitsPlayer and CellHitsPlayer) before W1 moved it to
/// ConstructPlacement and PlayerCapsule. They never change: a different bit is a change of the validation semantics.
/// </summary>
internal static class PlacementGolden
{
    internal const string RecorderVariable = "HORYZONT_GOLDEN_PLACEMENT_OUT";
    internal const int PlacementCases = 64;
    internal const int CapsuleCases = 256;

    internal static string Directory => Path.Combine(AppContext.BaseDirectory, "Golden", "ConstructPlacement");

    internal static string F(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture);

    internal static string F(Vector3 v) => $"{F(v.X)} {F(v.Y)} {F(v.Z)}";

    internal static string F(Quaternion q) => $"{F(q.X)} {F(q.Y)} {F(q.Z)} {F(q.W)}";

    internal static float ParseFloat(string token) =>
        BitConverter.UInt32BitsToSingle(uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    internal static Vector3 ParseVector(string[] tokens, int index) =>
        new(ParseFloat(tokens[index]), ParseFloat(tokens[index + 1]), ParseFloat(tokens[index + 2]));

    internal static Quaternion ParseQuaternion(string[] tokens, int index) =>
        new(ParseFloat(tokens[index]), ParseFloat(tokens[index + 1]), ParseFloat(tokens[index + 2]), ParseFloat(tokens[index + 3]));

    /// <summary>The K1 computation of ValidatePlaceOnTerrain (OrientOnSurface and the origin formula).</summary>
    internal static void K1OnTerrain(Vector3 hit, Vector3 look, Vector3 ray, out Vector3 origin, out Quaternion rotation)
    {
        rotation = IntentValidator.OrientOnSurface(hit, look, ray);
        origin = hit - Vector3.Transform(new Vector3(0.5f, 0f, 0.5f), rotation);
    }

    internal static List<string> RecordPlacement()
    {
        var random = new Random(130013);
        var lines = new List<string>();
        var fixedHits = new[]
        {
            new Vector3(0, 4000, 0), new Vector3(0, -4000, 0), new Vector3(4000, 0, 0), new Vector3(-4000, 0, 0),
            new Vector3(0, 0, 4000), new Vector3(0, 0, -4000), Vector3.Zero, new Vector3(0, 0.0001f, 0),
        };
        for (var i = 0; i < PlacementCases; i++)
        {
            var hit = i < fixedHits.Length ? fixedHits[i] : 4000f * RandomUnit(random);
            var up = Vector3.Normalize(hit == Vector3.Zero ? Vector3.UnitY : hit);
            Vector3 look, ray;
            switch (i % 8)
            {
                case 0: // look along up: degenerate, falls back to the ray
                    look = up;
                    ray = RandomUnit(random);
                    break;
                case 1: // look and ray along up: falls back to the planet X axis
                    look = -up;
                    ray = up;
                    break;
                case 2: // zero look and zero ray
                    look = Vector3.Zero;
                    ray = Vector3.Zero;
                    break;
                case 3: // tangent look
                    look = Vector3.Normalize(Vector3.Cross(up, RandomUnit(random)));
                    ray = RandomUnit(random);
                    break;
                case 4: // up parallel to X: the X fallback degenerates too
                    look = Vector3.Zero;
                    ray = Vector3.Zero;
                    hit = new Vector3(4000f * (i % 16 == 4 ? 1 : -1), 0, 0);
                    break;
                default:
                    look = RandomUnit(random);
                    ray = RandomUnit(random);
                    break;
            }

            K1OnTerrain(hit, look, ray, out var origin, out var rotation);
            lines.Add($"{F(hit)} {F(look)} {F(ray)} {F(origin)} {F(rotation)}");
        }

        return lines;
    }

    internal static List<string> RecordCapsule()
    {
        var f = new SimFixture();
        var player = f.Join();
        var validator = new IntentValidator(f.State, f.Terrain, f.Rules);
        var sphere = typeof(IntentValidator).GetMethod("SphereHitsPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cell = typeof(IntentValidator).GetMethod("CellHitsPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var random = new Random(130014);
        var lines = new List<string>();
        for (var i = 0; i < CapsuleCases; i++)
        {
            var feet = i % 16 == 0 ? new Vector3(0, 4000.1f, 0) : 4000.1f * RandomUnit(random);
            f.MovePlayer(player, feet);
            var up = Vector3.Normalize(feet);
            var kind = i % 2 == 0 ? 'S' : 'C';
            if (kind == 'S')
            {
                var radius = 0.5f + (2.5f * (float)random.NextDouble());
                var center = feet + (up * (-0.5f + (3f * (float)random.NextDouble()))) + (2.2f * RandomUnit(random) * (float)random.NextDouble());
                var hit = (bool)sphere.Invoke(validator, [WorldFactory.HomePlanet, center, radius])!;
                lines.Add($"S {F(feet)} {F(center)} {F(radius)} {(hit ? 1 : 0)}");
            }
            else
            {
                var origin = feet + (2.4f * RandomUnit(random) * (float)random.NextDouble());
                var axis = RandomUnit(random);
                var angle = i % 5 == 0 ? 0f : (float)(random.NextDouble() * Math.PI * 2);
                var rotation = Quaternion.CreateFromAxisAngle(axis, angle);
                var position = new CellPos(random.Next(-1, 2), random.Next(-1, 2), random.Next(-1, 2));
                var hit = (bool)cell.Invoke(validator, [WorldFactory.HomePlanet, origin, rotation, null, position])!;
                lines.Add($"C {F(feet)} {F(origin)} {F(rotation)} {position.X} {position.Y} {position.Z} {(hit ? 1 : 0)}");
            }
        }

        return lines;
    }

    internal static string Join(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    internal static string[][] ReadTokens(string name) =>
        File.ReadAllText(Path.Combine(Directory, name))
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .ToArray();

    private static Vector3 RandomUnit(Random random)
    {
        while (true)
        {
            var v = new Vector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
            if (v.LengthSquared() is > 0.01f and <= 1f)
            {
                return Vector3.Normalize(v);
            }
        }
    }
}
