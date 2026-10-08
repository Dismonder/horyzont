using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>Version of a construct after an operation. A compound operation of subproject 3 can change several.</summary>
/// <param name="Construct">Construct.</param>
/// <param name="Version">Block data version after the operation.</param>
/// <param name="Removed">True when the operation removed the construct.</param>
public readonly record struct ConstructChange(EntityId Construct, uint Version, bool Removed);
