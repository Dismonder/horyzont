using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Validates intents against the state and the terrain. The order of the checks decides the returned reason
/// (docs/plans/K1-03-core-stan-i-operacje.md, "Walidacja i rozwiązanie"). Never throws for bad input.
/// </summary>
internal sealed class IntentValidator(WorldState state, ITerrainService terrain, GameRules rules)
{
    /// <summary>Distance from a cell centre to its corner, rounded up.</summary>
    private const float CellReachSlack = 0.87f;
    private const float SightEpsilon = 1e-3f;

    internal RejectReason Validate(PlayerId playerId, Intent intent, PlayerBuckets? buckets, out IntentFacts facts)
    {
        facts = default;
        if (!state.Players.TryGet(playerId, out var player))
        {
            return RejectReason.UnknownPlayer;
        }

        if (!player.IsOnline || buckets is null)
        {
            return RejectReason.PlayerOffline;
        }

        if (!HasValidParameters(intent, out var rayOrigin, out var bucket, buckets))
        {
            return RejectReason.InvalidParameters;
        }

        var hasPlanet = TryGetEye(player, out var playerPlanet, out var eye);
        if (rayOrigin is { } origin && hasPlanet && Vector3.Distance(origin, eye) > rules.MaxRayOriginErrorMeters)
        {
            return RejectReason.RayOriginMismatch;
        }

        if (!bucket.HasToken)
        {
            return RejectReason.RateLimited;
        }

        return intent switch
        {
            TerrainToolIntent tool => ValidateTerrainTool(tool, hasPlanet, playerPlanet, out facts),
            PlaceBlockIntent { Target: ConstructFaceTarget face } place => ValidatePlaceOnFace(place, face, hasPlanet, playerPlanet, eye, out facts),
            PlaceBlockIntent { Target: TerrainTarget target } place => ValidatePlaceOnTerrain(place, target, hasPlanet, playerPlanet, out facts),
            RemoveBlockIntent remove => ValidateRemove(remove, hasPlanet, playerPlanet, eye, out facts),
            _ => RejectReason.InvalidParameters,
        };
    }

    /// <summary>Rate class of an intent.</summary>
    internal static TokenBucket? BucketFor(Intent intent, PlayerBuckets buckets) => intent switch
    {
        TerrainToolIntent => buckets.Terrain,
        PlaceBlockIntent or RemoveBlockIntent => buckets.Blocks,
        _ => null,
    };

    private static bool HasValidParameters(Intent intent, out Vector3? rayOrigin, out TokenBucket bucket, PlayerBuckets buckets)
    {
        rayOrigin = null;
        bucket = buckets.Blocks;
        switch (intent)
        {
            case TerrainToolIntent tool:
                bucket = buckets.Terrain;
                rayOrigin = tool.RayOrigin;
                return Geometry.IsFinite(tool.RayOrigin) && Geometry.IsDirection(tool.RayDirection) && float.IsFinite(tool.Radius);
            case PlaceBlockIntent { Target: TerrainTarget target }:
                rayOrigin = target.RayOrigin;
                return Geometry.IsFinite(target.RayOrigin) && Geometry.IsDirection(target.RayDirection)
                    && Geometry.IsDirection(target.LookForward);
            case PlaceBlockIntent { Target: ConstructFaceTarget face }:
                return Enum.IsDefined(face.Face);
            case RemoveBlockIntent:
                return true;
            default:
                return false;
        }
    }

    private RejectReason ValidateTerrainTool(TerrainToolIntent tool, bool hasPlanet, PlanetId playerPlanet, out IntentFacts facts)
    {
        facts = default;
        if (!state.Planets.TryGet(tool.Planet, out var planet))
        {
            return RejectReason.UnknownPlanet;
        }

        if (!hasPlanet || tool.Planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        if (tool.Radius < rules.MinToolRadius || tool.Radius > Math.Min(rules.MaxToolRadius, TerrainRules.MaxEditRadiusMeters) || !Enum.IsDefined(tool.Mode)
            || (tool.Mode == TerrainEditMode.Remove && tool.MaterialId != 0))
        {
            return RejectReason.InvalidParameters;
        }

        if (tool.Mode == TerrainEditMode.Add && !state.Materials.IsKnown(tool.MaterialId))
        {
            return RejectReason.UnknownMaterial;
        }

        if (!terrain.Raycast(tool.Planet, tool.RayOrigin, tool.RayDirection, rules.ReachMeters, out var hit))
        {
            return RejectReason.NoHit;
        }

        if (ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(tool.Planet), tool.RayOrigin, tool.RayDirection, hit.Distance, out var blocker)
            && blocker.Distance < hit.Distance)
        {
            return RejectReason.NoLineOfSight;
        }

        var center = hit.Position;
        var distanceFromCore = center.Length();
        if (distanceFromCore < planet.RadiusMeters - planet.CoreDepthMeters + tool.Radius)
        {
            return RejectReason.BelowCore;
        }

        if (distanceFromCore > planet.RadiusMeters + planet.AtmosphereHeightMeters)
        {
            return RejectReason.OutsideAtmosphere;
        }

        var loadRadius = tool.Radius + (TerrainRules.EditMarginVoxels * planet.VoxelSizeMeters);
        if (!terrain.IsAreaLoaded(tool.Planet, center, loadRadius))
        {
            return RejectReason.AreaNotLoaded;
        }

        if (tool.Mode == TerrainEditMode.Add && SphereHitsPlayer(tool.Planet, center, tool.Radius))
        {
            return RejectReason.IntersectsPlayer;
        }

        facts = new IntentFacts(tool.Planet, center, Quaternion.Identity, default);
        return RejectReason.None;
    }

    private RejectReason ValidatePlaceOnFace(
        PlaceBlockIntent place, ConstructFaceTarget target, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        var block = CheckBlockType(place);
        if (block != RejectReason.None)
        {
            return block;
        }

        if (!state.Constructs.TryGet(target.Construct, out var construct))
        {
            return RejectReason.UnknownConstruct;
        }

        if (!hasPlanet || !state.TryResolvePlanet(construct.Parent, out var planet) || planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        if (construct.GetBlock(target.Cell).IsEmpty)
        {
            return RejectReason.CellEmpty;
        }

        var cell = target.Cell.Offset(target.Face);
        if (!cell.IsInBounds)
        {
            return RejectReason.CellOutOfBounds;
        }

        if (!construct.GetBlock(cell).IsEmpty)
        {
            return RejectReason.CellOccupied;
        }

        var cellCenter = ToPlanet(construct, CellCenter(cell));
        if (Vector3.Distance(eye, cellCenter) > rules.ReachMeters + CellReachSlack)
        {
            return RejectReason.OutOfReach;
        }

        var faceCenter = ToPlanet(construct, CellCenter(target.Cell) + (FaceNormal(target.Face) * 0.5f));
        if (!HasLineOfSight(planet, eye, faceCenter, construct.Id, target.Cell))
        {
            return RejectReason.NoLineOfSight;
        }

        if (!terrain.IsAreaLoaded(planet, cellCenter, 1f))
        {
            return RejectReason.AreaNotLoaded;
        }

        if (CellHitsPlayer(planet, construct.Origin, construct.Rotation, construct, cell))
        {
            return RejectReason.IntersectsPlayer;
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, cell);
        return RejectReason.None;
    }

    private RejectReason ValidatePlaceOnTerrain(
        PlaceBlockIntent place, TerrainTarget target, bool hasPlanet, PlanetId playerPlanet, out IntentFacts facts)
    {
        facts = default;
        var block = CheckBlockType(place);
        if (block != RejectReason.None)
        {
            return block;
        }

        if (!state.Planets.TryGet(target.Planet, out _))
        {
            return RejectReason.UnknownPlanet;
        }

        if (!hasPlanet || target.Planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        if (!terrain.Raycast(target.Planet, target.RayOrigin, target.RayDirection, rules.ReachMeters, out var hit))
        {
            return RejectReason.NoHit;
        }

        if (ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(target.Planet), target.RayOrigin, target.RayDirection, hit.Distance, out var blocker)
            && blocker.Distance < hit.Distance)
        {
            return RejectReason.NoLineOfSight;
        }

        ConstructPlacement.OnTerrain(hit.Position, target.LookForward, target.RayDirection, out var origin, out var rotation);
        var cellCenter = origin + Vector3.Transform(new Vector3(0.5f, 0.5f, 0.5f), rotation);
        if (!terrain.IsAreaLoaded(target.Planet, cellCenter, 1f))
        {
            return RejectReason.AreaNotLoaded;
        }

        if (CellHitsPlayer(target.Planet, origin, rotation, null, default))
        {
            return RejectReason.IntersectsPlayer;
        }

        facts = new IntentFacts(target.Planet, origin, rotation, default);
        return RejectReason.None;
    }

    private RejectReason ValidateRemove(RemoveBlockIntent remove, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        if (!state.Constructs.TryGet(remove.Construct, out var construct))
        {
            return RejectReason.UnknownConstruct;
        }

        if (!hasPlanet || !state.TryResolvePlanet(construct.Parent, out var planet) || planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        if (!remove.Cell.IsInBounds)
        {
            return RejectReason.CellOutOfBounds;
        }

        if (construct.GetBlock(remove.Cell).IsEmpty)
        {
            return RejectReason.CellEmpty;
        }

        var cellCenter = ToPlanet(construct, CellCenter(remove.Cell));
        if (Vector3.Distance(eye, cellCenter) > rules.ReachMeters + CellReachSlack)
        {
            return RejectReason.OutOfReach;
        }

        if (!HasLineOfSight(planet, eye, cellCenter, construct.Id, remove.Cell))
        {
            return RejectReason.NoLineOfSight;
        }

        // The applier refuses to remove a seat or the last block of a frame that still has children.
        if (state.TryGetSeated(construct.Id, remove.Cell, out _)
            || (construct.BlockCount == 1 && state.HasChildren(FrameRef.Of(construct.Id))))
        {
            return RejectReason.IntersectsPlayer;
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, remove.Cell);
        return RejectReason.None;
    }

    private RejectReason CheckBlockType(PlaceBlockIntent place)
    {
        if (place.BlockTypeId == 0 || !state.BlockTypes.IsKnown(place.BlockTypeId))
        {
            return RejectReason.UnknownBlockType;
        }

        return place.Orientation >= BlockState.OrientationCount ? RejectReason.InvalidParameters : RejectReason.None;
    }

    /// <summary>
    /// The ray from the eye to a target point inside or on the target cell hits no terrain and no other cell first.
    /// </summary>
    private bool HasLineOfSight(PlanetId planet, Vector3 eye, Vector3 target, EntityId construct, CellPos cell)
    {
        var toTarget = target - eye;
        var distance = toTarget.Length();
        if (distance <= SightEpsilon)
        {
            return true;
        }

        var direction = toTarget / distance;
        if (ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(planet), eye, direction, distance + SightEpsilon, out var hit)
            && (hit.Construct != construct || hit.Cell != cell)
            && hit.Distance < distance - SightEpsilon)
        {
            return false;
        }

        var reach = hit.Construct == construct && hit.Cell == cell ? MathF.Min(hit.Distance, distance) : distance;
        return !terrain.Raycast(planet, eye, direction, MathF.Max(0f, reach - SightEpsilon), out _);
    }

    private bool TryGetEye(PlayerState player, out PlanetId planet, out Vector3 eye)
    {
        if (!state.TryToPlanetFrame(player.Frame, player.Position, out planet, out var feet))
        {
            eye = default;
            return false;
        }

        eye = feet + (TerrainMath.UpAt(feet) * rules.EyeHeight);
        return true;
    }

    private bool SphereHitsPlayer(PlanetId planet, Vector3 center, float radius)
    {
        var players = state.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var other = players[i];
            if (!other.IsOnline || !state.TryToPlanetFrame(other.Frame, other.Position, out var otherPlanet, out var feet) || otherPlanet != planet)
            {
                continue;
            }

            if (PlayerCapsule.IntersectsSphere(feet, center, radius, rules))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a cell (box in the space of a construct pose) intersects the capsule of an online player.</summary>
    private bool CellHitsPlayer(PlanetId planet, Vector3 origin, Quaternion rotation, Construct? construct, CellPos cell)
    {
        var players = state.Players.All;
        for (var i = 0; i < players.Count; i++)
        {
            var other = players[i];
            if (!other.IsOnline || !state.TryToPlanetFrame(other.Frame, other.Position, out var otherPlanet, out var feet) || otherPlanet != planet)
            {
                continue;
            }

            var hit = construct is null
                ? PlayerCapsule.IntersectsCell(feet, origin, rotation, cell, rules)
                : IntersectsNestedCell(construct, feet, cell);
            if (hit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Capsule against a cell of a construct whose pose is composed along its parent chain.</summary>
    private bool IntersectsNestedCell(Construct construct, Vector3 feet, CellPos cell)
    {
        PlayerCapsule.Axis(feet, rules, out var bottom, out var top);
        return PlayerCapsule.IntersectsLocalCell(PlanetToConstruct(construct, bottom), PlanetToConstruct(construct, top), cell, rules);
    }

    private Vector3 ToPlanet(Construct construct, Vector3 local)
    {
        state.TryToPlanetFrame(FrameRef.Of(construct.Id), local, out _, out var planetPoint);
        return planetPoint;
    }

    private Vector3 PlanetToConstruct(Construct construct, Vector3 planetPoint)
    {
        var parentPoint = construct.Parent.Kind == FrameKind.Construct
            && state.Constructs.TryGet(new EntityId(construct.Parent.Id), out var parent)
            ? PlanetToConstruct(parent, planetPoint)
            : planetPoint;
        return construct.ParentToLocal(parentPoint);
    }

    private static Vector3 CellCenter(CellPos cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    private static Vector3 FaceNormal(Face face) => face switch
    {
        Face.PosX => Vector3.UnitX,
        Face.NegX => -Vector3.UnitX,
        Face.PosY => Vector3.UnitY,
        Face.NegY => -Vector3.UnitY,
        Face.PosZ => Vector3.UnitZ,
        _ => -Vector3.UnitZ,
    };
}
