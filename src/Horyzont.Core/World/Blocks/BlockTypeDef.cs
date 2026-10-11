namespace Horyzont.Core.World.Blocks;

/// <summary>
/// Definition of a block type from game content. Content, not a save or wire format: clients take definitions from their
/// own content by key.
/// </summary>
/// <param name="Key">Registry key, at most <see cref="Protocol.ProtocolConstants.MaxRegistryKeyBytes"/> UTF-8 bytes.</param>
/// <param name="Shape">Block shape.</param>
/// <param name="MaterialId">Visual material.</param>
/// <param name="HasCollision">Whether the block collides with characters.</param>
/// <param name="MassKg">Mass of one block, kg (positive, finite).</param>
/// <param name="Function">What the block does on a ship.</param>
public sealed record BlockTypeDef(string Key, BlockShape Shape, byte MaterialId, bool HasCollision, float MassKg, BlockFunction Function);
