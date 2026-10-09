using System.Numerics;

using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tools;

/// <summary>
/// Finds what the aim ray points at, in the order the server uses: a construct wins over the terrain only when it is strictly closer
/// (<c>blocker.Distance &lt; hit.Distance</c> in the validator); a tie goes to the terrain. Nothing beyond <see cref="GameRules.ReachMeters"/>
/// from the eye is a target.
/// </summary>
public static class ToolTargeting
{
    /// <summary>The target of the builder: a construct cell (with the face the ray entered) or the terrain.</summary>
    /// <param name="aim">The aim.</param>
    /// <param name="world">World view.</param>
    /// <param name="rules">Game rules.</param>
    /// <param name="target">The target; <see cref="ToolTargetKind.None"/> when nothing is within reach.</param>
    /// <returns>True when there is a target.</returns>
    public static bool TryTarget(in AimRay aim, IToolWorldView world, GameRules rules, out ToolTarget target)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        target = default;
        var terrainHit = world.RaycastTerrain(aim.Planet, aim.Eye, aim.Direction, rules.ReachMeters, out var terrain)
            && terrain.Distance <= rules.ReachMeters;
        var limit = terrainHit ? terrain.Distance : rules.ReachMeters;
        if (world.RaycastConstructs(aim.Planet, aim.Eye, aim.Direction, limit, out var construct)
            && construct.Distance <= rules.ReachMeters && (!terrainHit || construct.Distance < terrain.Distance))
        {
            var normal = FaceNormal(construct.Face);
            if (world.TryGetConstructPose(construct.Construct, out _, out var rotation))
            {
                normal = Vector3.Transform(normal, rotation);
            }

            target = new ToolTarget(ToolTargetKind.ConstructCell, construct.Position, normal, construct.Distance, construct.Construct, construct.Cell, construct.Face);
            return true;
        }

        if (terrainHit)
        {
            target = new ToolTarget(ToolTargetKind.Terrain, terrain.Position, terrain.Normal, terrain.Distance, default, default, default);
            return true;
        }

        return false;
    }

    /// <summary>The terrain under the aim for the terrain manipulator, and whether a construct stands in front of it.</summary>
    /// <param name="aim">The aim.</param>
    /// <param name="world">World view.</param>
    /// <param name="rules">Game rules.</param>
    /// <param name="target">The terrain hit; <see cref="ToolTargetKind.None"/> when there is none within reach.</param>
    /// <param name="blockedByConstruct">A construct is strictly closer than the terrain hit (the server answers <c>NoLineOfSight</c>).</param>
    /// <returns>True when the terrain is within reach.</returns>
    public static bool TryTargetTerrain(in AimRay aim, IToolWorldView world, GameRules rules, out ToolTarget target, out bool blockedByConstruct)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        target = default;
        blockedByConstruct = false;
        if (!world.RaycastTerrain(aim.Planet, aim.Eye, aim.Direction, rules.ReachMeters, out var terrain) || terrain.Distance > rules.ReachMeters)
        {
            return false;
        }

        blockedByConstruct = world.RaycastConstructs(aim.Planet, aim.Eye, aim.Direction, terrain.Distance, out var blocker)
            && blocker.Distance < terrain.Distance;
        target = new ToolTarget(ToolTargetKind.Terrain, terrain.Position, terrain.Normal, terrain.Distance, default, default, default);
        return true;
    }

    internal static Vector3 FaceNormal(Face face) => face switch
    {
        Face.PosX => Vector3.UnitX,
        Face.NegX => -Vector3.UnitX,
        Face.PosY => Vector3.UnitY,
        Face.NegY => -Vector3.UnitY,
        Face.PosZ => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };
}
