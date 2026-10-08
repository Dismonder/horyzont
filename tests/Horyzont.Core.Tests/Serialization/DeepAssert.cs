using System.Collections;
using System.Numerics;
using System.Reflection;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>
/// Structural equality for decoded values. Core records hold <c>IReadOnlyList</c> and array members, which compare by reference with the
/// compiler-generated record equality, so the tests compare public properties and sequence elements instead. Floats compare by bit pattern.
/// </summary>
public static class DeepAssert
{
    /// <summary>Largest rotation error accepted for the 32-bit compressed rotation, in degrees.</summary>
    public const double CompressedRotationToleranceDegrees = 0.3;

    /// <summary>Fails the test when the values differ.</summary>
    /// <param name="expected">Expected value.</param>
    /// <param name="actual">Decoded value.</param>
    /// <param name="compressedRotation">True when quaternions may differ by up to 0.3 degrees (messages with compressed rotation).</param>
    public static void Equal(object? expected, object? actual, bool compressedRotation = false)
    {
        var mismatch = Compare(expected, actual, compressedRotation, "$");
        if (mismatch is not null)
        {
            Assert.Fail(mismatch);
        }
    }

    private const int MaxDepth = 64;

    private static string? Compare(object? expected, object? actual, bool tolerant, string path, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            return $"{path}: comparison exceeded depth {MaxDepth} (cyclic or self-producing member)";
        }

        if (expected is null || actual is null)
        {
            return expected is null && actual is null ? null : $"{path}: expected {Show(expected)} but was {Show(actual)}";
        }

        var type = expected.GetType();
        var sequences = expected is IEnumerable && expected is not string && actual is IEnumerable && actual is not string;
        if (!sequences && type != actual.GetType())
        {
            return $"{path}: expected type {type.Name} but was {actual.GetType().Name}";
        }

        switch (expected)
        {
            case float f:
                return BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits((float)actual) ? null : $"{path}: expected {f:R} but was {(float)actual:R}";
            case Vector3 v:
                var av = (Vector3)actual;
                return v.X.Equals(av.X) && v.Y.Equals(av.Y) && v.Z.Equals(av.Z) ? null : $"{path}: expected {v} but was {av}";
            case Quaternion q:
                var aq = (Quaternion)actual;
                if (tolerant)
                {
                    var angle = Kit.AngleDegrees(q, aq);
                    return angle < CompressedRotationToleranceDegrees ? null : $"{path}: rotation differs by {angle:F4} degrees ({q} vs {aq})";
                }

                return q.X.Equals(aq.X) && q.Y.Equals(aq.Y) && q.Z.Equals(aq.Z) && q.W.Equals(aq.W) ? null : $"{path}: expected {q} but was {aq}";
            case string or Guid or Half:
                return expected.Equals(actual) ? null : $"{path}: expected {Show(expected)} but was {Show(actual)}";
        }

        if (type.IsPrimitive || type.IsEnum)
        {
            return expected.Equals(actual) ? null : $"{path}: expected {Show(expected)} but was {Show(actual)}";
        }

        if (expected is byte[] eb && actual is byte[])
        {
            return eb.AsSpan().SequenceEqual((byte[])actual) ? null : $"{path}: byte arrays differ (lengths {eb.Length} and {((byte[])actual).Length})";
        }

        if (expected is IEnumerable ee)
        {
            var el = ee.Cast<object?>().ToList();
            var al = ((IEnumerable)actual).Cast<object?>().ToList();
            if (el.Count != al.Count)
            {
                return $"{path}: expected {el.Count} elements but was {al.Count}";
            }

            for (var i = 0; i < el.Count; i++)
            {
                var m = Compare(el[i], al[i], tolerant, $"{path}[{i}]", depth + 1);
                if (m is not null)
                {
                    return m;
                }
            }

            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var members = new List<(string Name, FieldInfo Field)>();
        foreach (var field in type.GetFields(flags))
        {
            // Public fields and compiler backing fields of auto properties; computed getters have neither.
            var isBacking = field.Name.StartsWith('<') && field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal);
            if (field.IsPublic || isBacking)
            {
                members.Add((isBacking ? field.Name[1..field.Name.IndexOf('>')] : field.Name, field));
            }
        }

        if (members.Count == 0)
        {
            return expected.Equals(actual) ? null : $"{path}: expected {Show(expected)} but was {Show(actual)}";
        }

        foreach (var (name, field) in members)
        {
            var m = Compare(field.GetValue(expected), field.GetValue(actual), tolerant, $"{path}.{name}", depth + 1);
            if (m is not null)
            {
                return m;
            }
        }

        return null;
    }

    private static string Show(object? value) => value is null ? "null" : $"{value} ({value.GetType().Name})";
}
