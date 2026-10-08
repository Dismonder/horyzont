using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>A decoder that reads one value from a <see cref="ByteReader"/> (the shape of every <c>Read*</c> codec method).</summary>
public delegate T ReadFn<out T>(ref ByteReader reader);
