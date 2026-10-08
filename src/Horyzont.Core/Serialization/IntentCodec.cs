using Horyzont.Core.Intents;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for <see cref="Intent"/> frames: intent code, format version, payload.</summary>
public static class IntentCodec
{
    /// <summary>Appends the frame of <paramref name="intent"/>.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="intent">Intent.</param>
    public static void Write(ByteWriter w, Intent intent) => throw new NotImplementedException();

    /// <summary>Reads one intent frame.</summary>
    /// <param name="r">Reader positioned at the frame.</param>
    /// <returns>The intent.</returns>
    public static Intent Read(ref ByteReader r) => throw new NotImplementedException();
}
