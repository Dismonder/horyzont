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
    /// <summary>
    /// Metres the client keeps inside <see cref="GameRules.ReachMeters"/> (about maximum speed × (RTT / 2 + send interval)): the server
    /// measures reach from its own, older eye, so a target at the very edge on the client could be refused there.
    /// </summary>
    public const float ClientReachMarginMeters = 0.4f;

    /// <summary>The reach the client offers targets within: the server limit minus <see cref="ClientReachMarginMeters"/>.</summary>
    /// <param name="rules">Game rules.</param>
    /// <returns>Client reach in metres.</returns>
    public static float ClientReach(GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.ReachMeters - ClientReachMarginMeters;
    }

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
        var reach = ClientReach(rules);
        var terrainHit = world.RaycastTerrain(aim.Planet, aim.Eye, aim.Direction, reach, out var terrain)
            && terrain.Distance <= reach;
        var limit = terrainHit ? terrain.Distance : reach;
        if (world.RaycastConstructs(aim.Planet, aim.Eye, aim.Direction, limit, out var construct)
            && construct.Distance <= reach && (!terrainHit || construct.Distance < terrain.Distance))
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
        var reach = ClientReach(rules);
        if (!world.RaycastTerrain(aim.Planet, aim.Eye, aim.Direction, reach, out var terrain) || terrain.Distance > reach)
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
