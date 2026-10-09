using System.Globalization;

using Horyzont.Core.Ids;

namespace Horyzont.Persistence.Terrain;

/// <summary>
/// Whether the journal tail may be replayed by the current terrain edit semantics (K2-09, gate decision D1). A tail with a terrain
/// edit written under other semantics is not replayed: the world opens in safe mode from its checkpoint.
/// </summary>
/// <param name="ReplayAllowed">False when the tail has a terrain edit and the saved semantics differ from the current ones.</param>
/// <param name="SavedSemantics">Semantics that wrote the tail (meta <c>terrain_semantics</c>).</param>
/// <param name="CurrentSemantics">Semantics of this build.</param>
/// <param name="TailCount">Number of journal entries in the tail.</param>
/// <param name="FirstTailOpId">First operation of the tail; <see cref="OpId.None"/> when the tail is empty.</param>
/// <param name="TerrainEditCount">Number of terrain edits in the tail.</param>
/// <param name="FirstTerrainEditOpId">First terrain edit of the tail; <see cref="OpId.None"/> when there is none.</param>
public sealed record TerrainTailCheck(
    bool ReplayAllowed,
    string SavedSemantics,
    string CurrentSemantics,
    int TailCount,
    OpId FirstTailOpId,
    int TerrainEditCount,
    OpId FirstTerrainEditOpId)
{
    /// <summary>True when the tail must not be replayed.</summary>
    public bool IsMismatch => !ReplayAllowed;

    /// <summary>A message for the owner: which semantics wrote the tail, which are current and what is not replayed.</summary>
    /// <returns>The message.</returns>
    public string Describe() =>
        ReplayAllowed
            ? string.Create(CultureInfo.InvariantCulture, $"Journal tail of {TailCount} operations replays with terrain semantics '{CurrentSemantics}'.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Journal tail was written with terrain semantics '{SavedSemantics}', this build has '{CurrentSemantics}': " +
                $"{TailCount} operations from operation {FirstTailOpId.Value} ({TerrainEditCount} terrain edits, the first is " +
                $"operation {FirstTerrainEditOpId.Value}) are not replayed.");
}
