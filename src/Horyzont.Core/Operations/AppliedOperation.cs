using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Operations;

/// <summary>An operation after it was applied.</summary>
/// <param name="Id">Operation number.</param>
/// <param name="Tick">Tick.</param>
/// <param name="Actor">Player whose intent produced it; null for server operations.</param>
/// <param name="Operation">The operation.</param>
/// <param name="TerrainVersions">New versions of terrain blocks; non-empty for terrain edits.</param>
/// <param name="ConstructChanges">One entry per changed construct, by entity id.</param>
public sealed record AppliedOperation(
    OpId Id,
    Tick Tick,
    PlayerId? Actor,
    Operation Operation,
    IReadOnlyList<TerrainBlockVersion> TerrainVersions,
    IReadOnlyList<ConstructChange> ConstructChanges);
