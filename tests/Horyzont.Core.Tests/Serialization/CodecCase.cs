using FsCheck;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>One codec under test, in a form that lets the same property run over every codec.</summary>
/// <param name="Name">Stable name used as the theory argument.</param>
/// <param name="Values">Generator of valid values.</param>
/// <param name="Encode">Encodes a generated value.</param>
/// <param name="Decode">Decodes a whole buffer (and requires that no bytes remain).</param>
public sealed record CodecCase(string Name, Gen<object> Values, Func<object, byte[]> Encode, DecodeFn Decode)
{
    /// <summary>Values whose re-encoding must be byte-identical; null means every value in <see cref="Values"/>.</summary>
    public Gen<object>? StableValues { get; init; }

    /// <summary>The value the decoder must return for an original (lossy fields already quantized).</summary>
    public Func<object, object> Expected { get; init; } = v => v;

    /// <summary>Whether quaternions are compared with the 0.3 degree tolerance for this value.</summary>
    public Func<object, bool> CompressedRotation { get; init; } = _ => false;

    /// <summary>True when the codec entry point itself rejects trailing bytes (message frames and blobs).</summary>
    public bool RejectsTrailingBytes { get; init; }

    /// <summary>The largest number of 16 KiB chunk arrays one decode may legitimately allocate.</summary>
    public int MaxChunks { get; init; }

    /// <summary>Valid leading bytes (type and version) used to steer random input deeper into the decoder.</summary>
    public IReadOnlyList<byte[]> Headers { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => Name;
}
