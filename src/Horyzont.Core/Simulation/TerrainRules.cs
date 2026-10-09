using Horyzont.Core.Operations;
using Horyzont.Core.World;

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

    /// <summary>
    /// Checks the part of a terrain edit that depends only on the operation and its planet: shape and mode, finite centre and
    /// radius, radius in (0, <see cref="MaxEditRadiusMeters"/>], centre inside the atmosphere bound. Shared by the operation
    /// applier and the terrain journal replay, so a hostile journal entry is rejected the same way before any terrain loads.
    /// </summary>
    /// <param name="op">The edit.</param>
    /// <param name="planet">Parameters of the edit's planet.</param>
    /// <param name="failure">Null when valid; otherwise the same message the operation applier reports.</param>
    /// <returns>True when valid.</returns>
    public static bool CheckEdit(TerrainEditOp op, PlanetParams planet, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(planet);
        failure = Check(op, planet);
        return failure is null;
    }

    private static string? Check(TerrainEditOp op, PlanetParams planet)
    {
        if (op.Shape != TerrainShape.Sphere || !Enum.IsDefined(op.Mode))
        {
            return "unknown shape or mode";
        }

        if (!Geometry.IsFinite(op.Center) || !float.IsFinite(op.Radius) || op.Radius <= 0)
        {
            return "invalid centre or radius";
        }

        // Journal entries are untrusted input: bound the work Apply and Replay do (AffectedBlocks grows with the radius).
        // Every operation the validator produces is inside these bounds (tool radius <= MaxEditRadiusMeters, centre inside the atmosphere).
        if (op.Radius > MaxEditRadiusMeters)
        {
            return "radius too large";
        }

        if (op.Center.Length() > planet.RadiusMeters + planet.AtmosphereHeightMeters + MaxEditRadiusMeters)
        {
            return "centre out of range";
        }

        return null;
    }
}
