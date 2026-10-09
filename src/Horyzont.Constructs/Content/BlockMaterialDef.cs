namespace Horyzont.Constructs.Content;

/// <summary>Visual material of a block type. Content-local: never saved and never sent (not a terrain material).</summary>
/// <param name="Id">Material number from 1; the layer in the block texture array is <c>Id - 1</c>.</param>
/// <param name="Key">Material key in the content file.</param>
/// <param name="ColorRgba8">Colour as 0xRRGGBBAA.</param>
public readonly record struct BlockMaterialDef(byte Id, string Key, uint ColorRgba8);
