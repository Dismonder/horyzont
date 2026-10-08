namespace Horyzont.Core.Tests.Serialization;

/// <summary>A decoder that turns a whole byte buffer into a value, throwing on bad input.</summary>
public delegate object DecodeFn(ReadOnlySpan<byte> data);
