namespace Horyzont.Core.Intents;

/// <summary>Place a block on a construct face or on the terrain (a new construct).</summary>
/// <param name="Target">Where to place.</param>
/// <param name="BlockTypeId">Block type.</param>
/// <param name="Orientation">Orientation below 24.</param>
public sealed record PlaceBlockIntent(BlockTarget Target, ushort BlockTypeId, byte Orientation) : Intent;
