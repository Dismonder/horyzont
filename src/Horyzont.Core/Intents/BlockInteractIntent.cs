using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Use a functional block: switch a door or a lamp, sleep in a bed (intent 28, spec 03, K6-30 D18).</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
/// <param name="Action">The action.</param>
public sealed record BlockInteractIntent(EntityId Construct, CellPos Cell, BlockInteractAction Action) : Intent;
