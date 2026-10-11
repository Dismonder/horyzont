using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Switch a construct to the ship mode or back from a cockpit.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="CockpitCell">Cell of the cockpit block the player aims at.</param>
/// <param name="TargetMode">Requested mode; differs from the current one.</param>
/// <param name="RayOrigin">Ray origin: the player's eye.</param>
/// <param name="RayDirection">Normalized ray direction.</param>
public sealed record ShipModeIntent(EntityId Construct, CellPos CockpitCell, ConstructMode TargetMode, Vector3 RayOrigin, Vector3 RayDirection) : Intent;
