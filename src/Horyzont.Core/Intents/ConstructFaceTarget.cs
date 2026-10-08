using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Place next to an occupied cell, across one of its faces.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Occupied cell.</param>
/// <param name="Face">Face of the cell.</param>
public sealed record ConstructFaceTarget(EntityId Construct, CellPos Cell, Face Face) : BlockTarget;
