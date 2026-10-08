using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World.Constructs;

/// <summary>Result of <see cref="ConstructRaycast"/>.</summary>
/// <param name="Construct">Hit construct.</param>
/// <param name="Cell">Hit cell.</param>
/// <param name="Face">Face through which the ray entered the cell.</param>
/// <param name="Position">Hit point in the frame of the ray.</param>
/// <param name="Distance">Distance along the ray in metres.</param>
public readonly record struct ConstructHit(EntityId Construct, CellPos Cell, Face Face, Vector3 Position, float Distance);
