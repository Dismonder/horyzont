using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Progress;

/// <summary>Reference to the bed that is the respawn point of a player.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Cell">Cell of the bed.</param>
/// <param name="ExpectedType">Block type the cell must hold.</param>
public readonly record struct BedRef(EntityId Construct, CellPos Cell, ushort ExpectedType);
