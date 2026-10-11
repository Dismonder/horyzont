using Horyzont.Core.Ids;

namespace Horyzont.Core.Intents;

/// <summary>
/// Leave the seat; in flight this is an ejection (K5-25 decision 6). The construct guards against a late intent after a seat change.
/// </summary>
/// <param name="Construct">Construct the player sits in.</param>
public sealed record SeatExitIntent(EntityId Construct) : Intent;
