using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Refuel a parked ship from a refuel station block the player aims at (K5-24 decision 14).</summary>
/// <param name="Ship">The ship to refuel.</param>
/// <param name="Station">Construct of the refuel station block.</param>
/// <param name="StationCell">Cell of the refuel station block.</param>
/// <param name="RayOrigin">Ray origin: the eye of the player.</param>
/// <param name="RayDirection">Normalized ray direction.</param>
public sealed record RefuelIntent(EntityId Ship, EntityId Station, CellPos StationCell, Vector3 RayOrigin, Vector3 RayDirection) : Intent;
