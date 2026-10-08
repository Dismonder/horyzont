namespace Horyzont.Core.World.Blocks;

/// <summary>Definition of a block type from game content.</summary>
/// <param name="Key">Registry key, at most <see cref="Protocol.ProtocolConstants.MaxRegistryKeyBytes"/> UTF-8 bytes.</param>
/// <param name="Shape">Block shape.</param>
/// <param name="MaterialId">Visual material.</param>
/// <param name="HasCollision">Whether the block collides with characters.</param>
public sealed record BlockTypeDef(string Key, BlockShape Shape, byte MaterialId, bool HasCollision);
