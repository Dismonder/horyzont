using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Sit in a free cockpit or seat the player aims at, or move to another seat of the same construct (K5-25 decision 5).</summary>
/// <param name="Construct">Construct of the seat.</param>
/// <param name="SeatCell">Cell of the cockpit or seat block.</param>
/// <param name="RayOrigin">Ray origin: the eye of the player.</param>
/// <param name="RayDirection">Normalized ray direction.</param>
public sealed record SeatEnterIntent(EntityId Construct, CellPos SeatCell, Vector3 RayOrigin, Vector3 RayDirection) : Intent;
