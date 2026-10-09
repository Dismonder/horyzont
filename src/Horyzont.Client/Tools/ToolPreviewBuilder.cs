using System.Numerics;

using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Tools;

/// <summary>
/// Computes the preview of a tool and the reject reason the server would most likely answer, with the same shared Core geometry
/// (<see cref="ConstructPlacement"/>, <see cref="PlayerCapsule"/>, <see cref="BlockOverlap"/>) in the order of the validator. A hint
/// is advice for the colour of the preview, never a decision: the server validates every intent again. Does not allocate.
/// </summary>
internal sealed class ToolPreviewBuilder(GameRules rules, IToolWorldView world)
{
    private const int MaxOtherPlayers = 16;

    /// <summary>Distance from a cell centre to its corner, rounded up (as in the validator).</summary>
    private const float CellReachSlack = 0.87f;

    private readonly Vector3[] _others = new Vector3[MaxOtherPlayers];

    internal ToolPreview ForTerrainTool(in AimRay aim, in ToolSettings settings, bool fill)
    {
        if (!ToolTargeting.TryTargetTerrain(aim, world, rules, out var target, out var blocked))
        {
            return Empty(RejectReason.NoHit);
        }

        var hint = RejectReason.None;
        if (!world.TryGetPlanet(aim.Planet, out var planet))
        {
            hint = RejectReason.UnknownPlanet;
        }
        else if (fill && settings.FillMaterial == 0)
        {
            hint = RejectReason.UnknownMaterial;
        }
        else if (blocked)
        {
            hint = RejectReason.NoLineOfSight;
        }
        else
        {
            var distanceFromCore = target.Point.Length();
            if (distanceFromCore < planet.RadiusMeters - planet.CoreDepthMeters + settings.Radius)
            {
                hint = RejectReason.BelowCore;
            }
            else if (distanceFromCore > planet.RadiusMeters + planet.AtmosphereHeightMeters)
            {
                hint = RejectReason.OutsideAtmosphere;
            }
            else if (fill && SphereHitsPlayer(aim, target.Point, settings.Radius))
            {
                hint = RejectReason.IntersectsPlayer;
            }
        }

        return new ToolPreview(ToolPreviewKind.Sphere, target.Point, settings.Radius, default, Quaternion.Identity, default, default, 0, hint);
    }

    internal ToolPreview ForBuilder(in AimRay aim, in ToolSettings settings, in ToolTarget target)
    {
        switch (target.Kind)
        {
            case ToolTargetKind.Terrain:
                return OnTerrain(aim, settings, target);
            case ToolTargetKind.ConstructCell:
                return OnFace(aim, settings, target);
            default:
                return Empty(RejectReason.NoHit);
        }
    }

    private static ToolPreview Empty(RejectReason hint) =>
        new(ToolPreviewKind.None, default, 0f, default, Quaternion.Identity, default, default, 0, hint);

    private ToolPreview OnTerrain(in AimRay aim, in ToolSettings settings, in ToolTarget target)
    {
        ConstructPlacement.OnTerrain(target.Point, aim.LookForward, aim.Direction, out var origin, out var rotation);
        var cell = new CellPos(0, 0, 0);
        var center = origin + Vector3.Transform(new Vector3(0.5f, 0.5f, 0.5f), rotation);
        var hint = RejectReason.None;
        if (settings.BlockType == 0)
        {
            hint = RejectReason.UnknownBlockType;
        }
        else if (world.OverlapsConstruct(aim.Planet, center, rotation, null))
        {
            hint = RejectReason.CellOccupied;
        }
        else if (CellHitsPlayer(aim, origin, rotation, cell))
        {
            hint = RejectReason.IntersectsPlayer;
        }

        return new ToolPreview(ToolPreviewKind.NewConstructCell, center, 0f, origin, rotation, default, cell, settings.Orientation, hint);
    }

    private ToolPreview OnFace(in AimRay aim, in ToolSettings settings, in ToolTarget target)
    {
        if (!world.TryGetConstruct(target.Construct, out var construct) || !world.TryGetConstructPose(target.Construct, out var origin, out var rotation))
        {
            return Empty(RejectReason.UnknownConstruct);
        }

        var cell = target.Cell.Offset(target.Face);
        var center = origin + Vector3.Transform(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), rotation);
        var hint = RejectReason.None;
        if (settings.BlockType == 0)
        {
            hint = RejectReason.UnknownBlockType;
        }
        else if (!cell.IsInBounds)
        {
            hint = RejectReason.CellOutOfBounds;
        }
        else if (!construct.GetBlock(cell).IsEmpty)
        {
            hint = RejectReason.CellOccupied;
        }
        else if (Vector3.Distance(aim.Eye, center) > rules.ReachMeters + CellReachSlack)
        {
            hint = RejectReason.OutOfReach;
        }
        else if (world.OverlapsConstruct(aim.Planet, center, rotation, construct.Id))
        {
            hint = RejectReason.CellOccupied;
        }
        else if (CellHitsPlayer(aim, origin, rotation, cell))
        {
            hint = RejectReason.IntersectsPlayer;
        }

        return new ToolPreview(ToolPreviewKind.ConstructCell, center, 0f, origin, rotation, construct.Id, cell, settings.Orientation, hint);
    }

    private bool SphereHitsPlayer(in AimRay aim, Vector3 center, float radius)
    {
        if (PlayerCapsule.IntersectsSphere(aim.Feet, center, radius, rules))
        {
            return true;
        }

        var count = world.CopyOtherPlayerFeet(aim.Planet, _others);
        for (var i = 0; i < count; i++)
        {
            if (PlayerCapsule.IntersectsSphere(_others[i], center, radius, rules))
            {
                return true;
            }
        }

        return false;
    }

    private bool CellHitsPlayer(in AimRay aim, Vector3 origin, Quaternion rotation, CellPos cell)
    {
        if (PlayerCapsule.IntersectsCell(aim.Feet, origin, rotation, cell, rules))
        {
            return true;
        }

        var count = world.CopyOtherPlayerFeet(aim.Planet, _others);
        for (var i = 0; i < count; i++)
        {
            if (PlayerCapsule.IntersectsCell(_others[i], origin, rotation, cell, rules))
            {
                return true;
            }
        }

        return false;
    }
}
