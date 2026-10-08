using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Remove a block of a construct.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell.</param>
public sealed record RemoveBlockIntent(EntityId Construct, CellPos Cell) : Intent;
