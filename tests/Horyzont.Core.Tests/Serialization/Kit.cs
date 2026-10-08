using System.Globalization;
using System.Numerics;

using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Small helpers shared by the K1-04 codec tests.</summary>
public static class Kit
{
    /// <summary>Number of cases per property in normal CI runs.</summary>
    public const int Cases = 2000;

    /// <summary>Number of cases per property in the nightly (Soak) runs.</summary>
    public const int SoakCases = 200_000;

    /// <summary>Parses "01 0A ff" (whitespace separated hex bytes).</summary>
    /// <param name="text">Hex text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Hex(string text) =>
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(t => byte.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>Formats bytes as upper-case hex separated by spaces.</summary>
    /// <param name="data">Bytes.</param>
    /// <returns>The text.</returns>
    public static string ToHex(ReadOnlySpan<byte> data) => string.Join(' ', data.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>Concatenates byte arrays.</summary>
    /// <param name="parts">Parts.</param>
    /// <returns>One array.</returns>
    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>Runs <paramref name="write"/> on a fresh writer and returns what was written.</summary>
    /// <param name="write">Writing action.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Write(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Reads one value and requires that no bytes remain.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="data">Bytes.</param>
    /// <param name="read">Reader function.</param>
    /// <returns>The value.</returns>
    public static T ReadAll<T>(ReadOnlySpan<byte> data, ReadFn<T> read)
    {
        var r = new ByteReader(data);
        var value = read(ref r);
        r.EnsureEnd();
        return value;
    }

    /// <summary>Encodes an operation frame.</summary>
    /// <param name="op">Operation.</param>
    /// <returns>The frame.</returns>
    public static byte[] Encode(Operation op) => Write(w => OperationCodec.Write(w, op));

    /// <summary>Encodes an intent frame.</summary>
    /// <param name="intent">Intent.</param>
    /// <returns>The frame.</returns>
    public static byte[] Encode(Intent intent) => Write(w => IntentCodec.Write(w, intent));

    /// <summary>Encodes a message frame.</summary>
    /// <param name="message">Message.</param>
    /// <returns>The frame.</returns>
    public static byte[] Encode(Message message) => Write(w => MessageCodec.Write(w, message));

    /// <summary>Decodes an operation frame and requires that no bytes remain.</summary>
    /// <param name="data">Bytes.</param>
    /// <returns>The operation.</returns>
    public static Operation DecodeOperation(ReadOnlySpan<byte> data) => ReadAll(data, (ref ByteReader r) => OperationCodec.Read(ref r));

    /// <summary>Decodes an intent frame and requires that no bytes remain.</summary>
    /// <param name="data">Bytes.</param>
    /// <returns>The intent.</returns>
    public static Intent DecodeIntent(ReadOnlySpan<byte> data) => ReadAll(data, (ref ByteReader r) => IntentCodec.Read(ref r));

    /// <summary>Asserts that decoding throws <see cref="ProtocolViolationException"/> (and nothing else).</summary>
    /// <param name="decode">Decoder.</param>
    /// <param name="data">Bytes.</param>
    public static void AssertViolation(DecodeFn decode, byte[] data)
    {
        try
        {
            var value = decode(data);
            Assert.Fail($"Expected ProtocolViolationException but decoded {value} from [{ToHex(data.AsSpan(0, Math.Min(data.Length, 64)))}]");
        }
        catch (ProtocolViolationException)
        {
            // Expected.
        }
    }

    /// <summary>Asserts that decoding a message frame throws <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="hex">Frame as hex text.</param>
    public static void AssertMessageViolation(string hex) => AssertViolation(d => MessageCodec.Read(d), Hex(hex));

    /// <summary>Asserts that decoding an operation frame throws <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="hex">Frame as hex text.</param>
    public static void AssertOperationViolation(string hex) => AssertViolation(d => DecodeOperation(d), Hex(hex));

    /// <summary>Asserts that decoding an intent frame throws <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="hex">Frame as hex text.</param>
    public static void AssertIntentViolation(string hex) => AssertViolation(d => DecodeIntent(d), Hex(hex));

    /// <summary>Runs a property with the given number of cases and throws on failure.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="gen">Generator.</param>
    /// <param name="property">Property body; failing assertions fail the property.</param>
    /// <param name="cases">Number of cases.</param>
    public static void ForAll<T>(Gen<T> gen, Action<T> property, int cases = Cases) =>
        Check.One(Config.QuickThrowOnFailure.WithMaxTest(cases), Prop.ForAll(Arb.From(gen), property));

    /// <summary>The lengths of the proper prefixes to try: all of them for short input, a deterministic sample for long input.</summary>
    /// <param name="length">Length of the full encoding.</param>
    /// <returns>Prefix lengths in 0..length-1.</returns>
    public static IEnumerable<int> PrefixLengths(int length)
    {
        if (length <= 48)
        {
            return Enumerable.Range(0, length);
        }

        var rng = new Random(length);
        var picks = new SortedSet<int>();
        for (var i = 0; i < 16; i++)
        {
            picks.Add(i);
            picks.Add(length - 1 - i);
            picks.Add(rng.Next(16, length - 16));
        }

        return picks;
    }

    /// <summary>Applies 1 to 8 random edits (overwrite, bit flip, insert, delete, truncate, append) to a valid encoding.</summary>
    /// <param name="data">Valid encoding.</param>
    /// <param name="seed">Seed that makes the mutation reproducible.</param>
    /// <returns>The damaged bytes.</returns>
    public static byte[] Mutate(byte[] data, int seed)
    {
        var rng = new Random(seed);
        var bytes = new List<byte>(data);
        for (var edits = rng.Next(1, 9); edits > 0; edits--)
        {
            var kind = bytes.Count == 0 ? 2 : rng.Next(6);
            switch (kind)
            {
                case 0:
                    bytes[rng.Next(bytes.Count)] = (byte)rng.Next(256);
                    break;
                case 1:
                    bytes[rng.Next(bytes.Count)] ^= (byte)(1 << rng.Next(8));
                    break;
                case 2:
                    bytes.Insert(rng.Next(bytes.Count + 1), (byte)rng.Next(256));
                    break;
                case 3:
                    bytes.RemoveAt(rng.Next(bytes.Count));
                    break;
                case 4:
                    var tail = Math.Min(bytes.Count, rng.Next(1, 5));
                    bytes.RemoveRange(bytes.Count - tail, tail);
                    break;
                default:
                    for (var i = rng.Next(1, 5); i > 0; i--)
                    {
                        bytes.Add((byte)rng.Next(256));
                    }

                    break;
            }
        }

        return bytes.ToArray();
    }

    /// <summary>A unit quaternion whose first three components are the given values (the fourth is derived).</summary>
    /// <param name="x">X.</param>
    /// <param name="y">Y.</param>
    /// <param name="z">Z.</param>
    /// <returns>A normalized quaternion with distinctive component bit patterns.</returns>
    public static Quaternion UnitQuat(float x, float y, float z) => new(x, y, z, MathF.Sqrt(1f - (x * x) - (y * y) - (z * z)));

    /// <summary>The angle in degrees between two rotations, ignoring the sign ambiguity of quaternions.</summary>
    /// <param name="a">First rotation.</param>
    /// <param name="b">Second rotation.</param>
    /// <returns>The angle in degrees.</returns>
    public static double AngleDegrees(Quaternion a, Quaternion b)
    {
        var dot = Math.Abs(((double)a.X * b.X) + ((double)a.Y * b.Y) + ((double)a.Z * b.Z) + ((double)a.W * b.W));
        var norm = Math.Sqrt(((double)a.X * a.X) + ((double)a.Y * a.Y) + ((double)a.Z * a.Z) + ((double)a.W * a.W))
            * Math.Sqrt(((double)b.X * b.X) + ((double)b.Y * b.Y) + ((double)b.Z * b.Z) + ((double)b.W * b.W));
        return 2.0 * Math.Acos(Math.Min(1.0, dot / norm)) * 180.0 / Math.PI;
    }
}
