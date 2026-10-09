namespace Horyzont.Core.Simulation;

/// <summary>
/// Constants of the terrain edit operation semantics. They decide which block versions <c>Apply</c> and <c>Replay</c> bump and
/// which journal entries are accepted, so they are part of the journal format: they are constants, not runtime rules, and
/// changing one needs a new operation format version (docs/ZASADY.md, point 3).
/// </summary>
public static class TerrainRules
{
    /// <summary>Margin of terrain blocks touched by an edit, in voxels.</summary>
    public const int EditMarginVoxels = 4;

    /// <summary>
    /// Largest accepted edit radius in metres. The specification limits tools to 0.5-3 m; the bound leaves headroom for tuning
    /// the tool radius without a format change while keeping a hostile journal entry from allocating without limit.
    /// </summary>
    public const float MaxEditRadiusMeters = 16f;
}
