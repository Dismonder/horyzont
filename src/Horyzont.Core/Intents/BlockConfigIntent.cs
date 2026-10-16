using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Intents;

/// <summary>Change the setting of a functional block: the access of a chest, the name of a teleport (intent 29, spec 03, K6-30 D18).</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the block.</param>
/// <param name="Config">The new setting.</param>
public sealed record BlockConfigIntent(EntityId Construct, CellPos Cell, BlockConfig Config) : Intent;
