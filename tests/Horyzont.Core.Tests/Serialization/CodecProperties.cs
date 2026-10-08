using System.Collections.Concurrent;

using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Property bodies shared by the CI tests (2 000 cases) and the nightly Soak tests (200 000 cases).</summary>
public static class CodecProperties
{
    private const int MaxFuzzInput = 2048;

    private static readonly ConcurrentDictionary<string, byte> _warmed = new();

    /// <summary>decode(encode(x)) equals x after quantization of the lossy fields.</summary>
    /// <param name="codec">Codec under test.</param>
    /// <param name="cases">Number of cases.</param>
    public static void RoundTrip(CodecCase codec, int cases) =>
        Kit.ForAll(
            codec.Values,
            value =>
            {
                var bytes = codec.Encode(value);
                var decoded = codec.Decode(bytes);
                DeepAssert.Equal(codec.Expected(value), decoded, codec.CompressedRotation(value));
            },
            cases);

    /// <summary>encode(decode(encode(x))) equals encode(x) byte for byte.</summary>
    /// <param name="codec">Codec under test.</param>
    /// <param name="cases">Number of cases.</param>
    public static void ByteStability(CodecCase codec, int cases) =>
        Kit.ForAll(
            codec.StableValues ?? codec.Values,
            value =>
            {
                var first = codec.Encode(value);
                var second = codec.Encode(codec.Decode(first));
                Assert.Equal(first, second);
            },
            cases);

    /// <summary>Every proper prefix of a valid encoding throws <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="codec">Codec under test.</param>
    /// <param name="cases">Number of cases.</param>
    public static void Truncation(CodecCase codec, int cases) =>
        Kit.ForAll(
            codec.Values,
            value =>
            {
                var bytes = codec.Encode(value);
                foreach (var length in Kit.PrefixLengths(bytes.Length))
                {
                    Kit.AssertViolation(codec.Decode, bytes[..length]);
                }
            },
            cases);

    /// <summary>A valid encoding followed by extra bytes is rejected by entry points that own the whole buffer.</summary>
    /// <param name="codec">Codec under test.</param>
    /// <param name="cases">Number of cases.</param>
    public static void TrailingBytes(CodecCase codec, int cases) =>
        Kit.ForAll(
            codec.Values.SelectMany(v => Gen.Choose(1, 8).Select(extra => (Value: v, Extra: extra))),
            pair =>
            {
                var bytes = codec.Encode(pair.Value);
                var padded = new byte[bytes.Length + pair.Extra];
                bytes.CopyTo(padded, 0);
                Kit.AssertViolation(codec.Decode, padded);
            },
            cases);

    /// <summary>Arbitrary bytes (0 to 2048) decode to an object or throw only <see cref="ProtocolViolationException"/>, within the allocation budget.</summary>
    /// <param name="codec">Codec under test.</param>
    /// <param name="cases">Number of cases.</param>
    public static void FuzzRandomBytes(CodecCase codec, int cases) =>
        Kit.ForAll(RandomInput(codec), data => Fuzz(codec, data), cases);

    /// <summary>Valid encodings with random damage decode to an object or throw only <see cref="ProtocolViolationException"/>.</summary>
    /// <param name="codec">Codec under test.</param>
    /// <param name="cases">Number of cases.</param>
    public static void FuzzMutatedEncodings(CodecCase codec, int cases)
    {
        var mutated = codec.Values.SelectMany(v => Gen.Choose(0, int.MaxValue).Select(seed => Kit.Mutate(codec.Encode(v), seed)));
        Kit.ForAll(
            mutated,
            data =>
            {
                var decoded = Fuzz(codec, data);
                if (decoded is not null)
                {
                    // Whatever the decoder accepts, the encoder must accept too: the two sides validate the same rules.
                    var again = codec.Encode(decoded);
                    Assert.NotEmpty(again);
                }
            },
            cases);
    }

    private static Gen<byte[]> RandomInput(CodecCase codec)
    {
        var element = Gen.Frequency((3, Gen.Choose(0, 255)), (2, Gen.Choose(0, 5)));
        var body = Gen.Frequency((1, Gen.Choose(0, 16)), (2, Gen.Choose(0, MaxFuzzInput)))
            .SelectMany(length => element.ArrayOf(length).Select(a => Array.ConvertAll(a, x => (byte)x)));
        if (codec.Headers.Count == 0)
        {
            return body;
        }

        var withHeader = Gen.Elements(codec.Headers.ToArray())
            .SelectMany(header => body.Select(tail => header.Concat(tail).Take(MaxFuzzInput).ToArray()));
        return Gen.Frequency((1, body), (2, withHeader));
    }

    private static object? Fuzz(CodecCase codec, byte[] data)
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        _warmed.GetOrAdd(codec.Name, _ => Warm(codec));

        object? result = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            result = codec.Decode(data);
        }
        catch (ProtocolViolationException)
        {
            // Allowed: malformed input must surface as exactly this exception type.
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var budget = (4L * data.Length) + (64 * 1024) + (16L * 1024 * codec.MaxChunks);
        Assert.True(allocated <= budget, $"{codec.Name}: decoding {data.Length} bytes allocated {allocated} bytes (budget {budget})");
        return result;
    }

    private static byte Warm(CodecCase codec)
    {
        // JIT and type loading allocate on the calling thread; do it once before the allocation budget is measured.
        var sample = codec.Values.Sample(3, 20);
        foreach (var value in sample)
        {
            for (var i = 0; i < 3; i++)
            {
                codec.Decode(codec.Encode(value));
            }
        }

        return 0;
    }
}
