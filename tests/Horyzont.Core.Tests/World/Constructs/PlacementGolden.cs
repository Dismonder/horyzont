using System.Globalization;
using System.Numerics;

namespace Horyzont.Core.Tests.World.Constructs;

/// <summary>
/// Reader of the golden files of K2-13 W1 (Golden/ConstructPlacement). They were recorded with the K1 code
/// (IntentValidator.OrientOnSurface, the origin formula of ValidatePlaceOnTerrain, IntentValidator.SphereHitsPlayer and
/// CellHitsPlayer) in commit a37dce8, before W1 moved it to ConstructPlacement and PlayerCapsule; the recorder and the K1 reference
/// are in that commit. The files never change: a different bit is a change of the validation semantics.
/// </summary>
internal static class PlacementGolden
{
    internal const int PlacementCases = 64;
    internal const int CapsuleCases = 256;

    internal static string F(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture);

    internal static string F(Vector3 v) => $"{F(v.X)} {F(v.Y)} {F(v.Z)}";

    internal static string F(Quaternion q) => $"{F(q.X)} {F(q.Y)} {F(q.Z)} {F(q.W)}";

    internal static float ParseFloat(string token) =>
        BitConverter.UInt32BitsToSingle(uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    internal static Vector3 ParseVector(string[] tokens, int index) =>
        new(ParseFloat(tokens[index]), ParseFloat(tokens[index + 1]), ParseFloat(tokens[index + 2]));

    internal static Quaternion ParseQuaternion(string[] tokens, int index) =>
        new(ParseFloat(tokens[index]), ParseFloat(tokens[index + 1]), ParseFloat(tokens[index + 2]), ParseFloat(tokens[index + 3]));

    internal static string[][] ReadTokens(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "ConstructPlacement", name))
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .ToArray();
}
