using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>
/// Free a seat held by an offline player: the offline player moves to the respawn point (K5-25 decision 8).
/// </summary>
/// <param name="Construct">Construct of the seat.</param>
/// <param name="SeatCell">Cell of the occupied seat.</param>
/// <param name="RayOrigin">Ray origin: the eye of the player.</param>
/// <param name="RayDirection">Normalized ray direction.</param>
public sealed record SeatEvictIntent(EntityId Construct, CellPos SeatCell, Vector3 RayOrigin, Vector3 RayDirection) : Intent;
