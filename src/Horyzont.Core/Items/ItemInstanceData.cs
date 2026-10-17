namespace Horyzont.Core.Items;

/// <summary>
/// Per-instance item data (durability, upgrades). Reserved: always absent in format 1, where the codecs write the instance byte 0 and
/// reject any other value.
/// </summary>
public sealed record ItemInstanceData;
