namespace Horyzont.Core.Props;

/// <summary>What a layout puts into a cell slot (K6-31 D5): a kind and integer offsets, so decisions never depend on floating point.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="OffsetU">Offset along the cell columns, in 1/1024 of a cell (0-1023).</param>
/// <param name="OffsetV">Offset along the cell rows, in 1/1024 of a cell (0-1023).</param>
/// <param name="Rotation">Rotation around the up axis, in 1/65536 of a turn.</param>
/// <param name="Scale">Scale in 1/1000.</param>
public readonly record struct PropCandidate(PropKindV1 Kind, ushort OffsetU, ushort OffsetV, ushort Rotation, ushort Scale);
