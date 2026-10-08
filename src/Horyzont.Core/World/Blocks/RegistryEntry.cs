namespace Horyzont.Core.World.Blocks;

/// <summary>Mapping between a text key and its numeric id in a world registry.</summary>
/// <param name="Id">Numeric id, from 1.</param>
/// <param name="Key">Text key, at most <see cref="Protocol.ProtocolConstants.MaxRegistryKeyBytes"/> UTF-8 bytes.</param>
public readonly record struct RegistryEntry(ushort Id, string Key);
