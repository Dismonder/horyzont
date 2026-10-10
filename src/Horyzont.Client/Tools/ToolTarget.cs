using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tools;

/// <summary>The first thing the aim ray hits within reach.</summary>
/// <param name="Kind">Terrain, a construct cell or nothing.</param>
/// <param name="Point">Hit point in the planet frame.</param>
/// <param name="Normal">Surface normal at the hit in the planet frame.</param>
/// <param name="Distance">Distance from the eye.</param>
/// <param name="Construct">Hit construct (construct cell only).</param>
/// <param name="Cell">Hit cell (construct cell only).</param>
/// <param name="Face">Hit face of the cell (construct cell only).</param>
public readonly record struct ToolTarget(ToolTargetKind Kind, Vector3 Point, Vector3 Normal, float Distance, EntityId Construct, CellPos Cell, Face Face);
