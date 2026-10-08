using Horyzont.Core.Operations;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for <see cref="Operation"/> frames: kind, format version, payload.</summary>
public static class OperationCodec
{
    /// <summary>Appends the frame of <paramref name="op"/>.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="op">Operation.</param>
    public static void Write(ByteWriter w, Operation op) => throw new NotImplementedException();

    /// <summary>Reads one operation frame.</summary>
    /// <param name="r">Reader positioned at the frame.</param>
    /// <returns>The operation.</returns>
    public static Operation Read(ref ByteReader r) => throw new NotImplementedException();
}
