using System.Text;

namespace Horyzont.Core.Serialization;

/// <summary>Strict UTF-8 length of a string: the same rule as <see cref="ByteWriter.WriteString"/> (a lone surrogate is invalid), so what the applier accepts the journal can write.</summary>
internal static class StrictUtf8
{
    private static readonly UTF8Encoding _encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Counts the UTF-8 bytes of a string.</summary>
    /// <param name="text">The string.</param>
    /// <param name="bytes">The byte count when valid.</param>
    /// <returns>False for null or a string that is not valid UTF-8 (a lone surrogate).</returns>
    internal static bool TryGetByteCount(string? text, out int bytes)
    {
        bytes = 0;
        if (text is null)
        {
            return false;
        }

        try
        {
            bytes = _encoding.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
