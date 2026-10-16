using Horyzont.Core.World.Blocks;

namespace Horyzont.Constructs.Content;

/// <summary>A block type from the game content.</summary>
/// <param name="Def">Core definition; <c>Def.MaterialId</c> is the <see cref="BlockMaterialDef.Id"/> (content-local, never persisted).</param>
/// <param name="NameKey">Translation key, e.g. BLOCK_STONE_BLOCK.</param>
/// <param name="Mesh">Prefab mesh for <see cref="BlockShape.Mesh"/>, null for cubes.</param>
/// <param name="Light">Light parameters of a <see cref="BlockFunctionKind.Light"/> block, null for other blocks (K6-30).</param>
public sealed record BlockTypeContent(BlockTypeDef Def, string NameKey, ShapeMesh? Mesh, BlockLightDef? Light = null);
