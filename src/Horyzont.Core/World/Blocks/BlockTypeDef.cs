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
/// <param name="Comfort">Comfort the block adds to the rest of a player within 10 m, 0 to 10 (K6-33 D9); the comfort of the different types is summed.</param>
/// <param name="Heat">Whether the block is a source of heat for the rest (K6-33 D9).</param>
public sealed record BlockTypeDef(
    string Key, BlockShape Shape, byte MaterialId, bool HasCollision, float MassKg, BlockFunction Function, byte Comfort = 0, bool Heat = false);
