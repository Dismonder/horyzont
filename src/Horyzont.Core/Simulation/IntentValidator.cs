using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Ships;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Validates intents against the state and the terrain. The order of the checks decides the returned reason
/// (docs/plans/K1-03-core-stan-i-operacje.md, "Walidacja i rozwiązanie"). Never throws for bad input.
/// </summary>
internal sealed class IntentValidator(
    WorldState state, ITerrainService terrain, GameRules rules, BlockContentTable content, SurvivalRules? survival, FunctionalBlockRules functional)
{
    /// <summary>Distance from a cell centre to its corner, rounded up.</summary>
    private const float CellReachSlack = 0.87f;

    /// <summary>A tank with less than this much room is full for a refuel (K5-24 decision 14).</summary>
    private const float RefuelFullMargin = 0.5f;
    private const float SightEpsilon = 1e-3f;

    /// <summary>Corners of a block are probed this far inside the cell, so a block that only touches the ground is not embedded.</summary>
    private const float AnchorInset = 0.1f;

    /// <summary>Blocks farther than this above the highest terrain cannot be embedded in it.</summary>
    private const float AnchorSkipMargin = 2f;

    /// <summary>
    /// Ignores the final 0.25 m of terrain occlusion along the sight ray to allow SDF march uncertainty
    /// and predicted ray origins grazing the target surface. Terrain thinner than this slack at the target
    /// cannot hide it; a wall of at least 1 m still occludes it. Construct checks retain their exact tolerance.
    /// This is an intent validation tolerance, not terrain edit or replay semantics.
    /// </summary>
    private const float TerrainSightSlackMeters = 0.25f;

    private readonly List<CellPos> _doorColumn = [];

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

        // Chest settings, sleeping and setting the respawn point need the survival content; switching a door or a lamp does not (K6-30 D21).
        if (survival is null && intent is BlockConfigIntent or BlockInteractIntent { Action: not BlockInteractAction.Toggle })
        {
            return RejectReason.InvalidParameters;
        }

        if (intent is CraftIntent craft)
        {
            var craftReason = ValidateCraftParameters(craft);
            if (craftReason != RejectReason.None)
            {
                return craftReason;
            }
        }

        // Item intents (K6-29W): parameters and container access before the rate limit; without content they are invalid (D8).
        if (intent is ItemMoveIntent or ItemDropIntent or TakeAllIntent or QuickStackIntent)
        {
            var itemReason = survival is null ? RejectReason.InvalidParameters : survival.CheckItemParameters(playerId, intent);
            if (itemReason != RejectReason.None)
            {
                return itemReason;
            }
        }

        var hasPlanet = TryGetEye(player, out var playerPlanet, out var eye);
        if (rayOrigin is { } origin && hasPlanet && Vector3.Distance(origin, eye) > rules.MaxRayOriginErrorMeters)
        {
            return RejectReason.RayOriginMismatch;
        }

        if (!bucket.HasToken || (intent is TerrainToolIntent && !buckets.TerrainWindow.CanAccept(state.CurrentTick.Value)))
        {
            return RejectReason.RateLimited;
        }

        return intent switch
        {
            TerrainToolIntent tool => ValidateTerrainTool(tool, hasPlanet, playerPlanet, eye, out facts),
            PlaceBlockIntent { Target: ConstructFaceTarget face } place => ValidatePlaceOnFace(place, face, hasPlanet, playerPlanet, eye, out facts),
            PlaceBlockIntent { Target: TerrainTarget target } place => ValidatePlaceOnTerrain(place, target, hasPlanet, playerPlanet, eye, out facts),
            RemoveBlockIntent remove => ValidateRemove(remove, hasPlanet, playerPlanet, eye, out facts),
            ShipModeIntent ship => ValidateShipMode(ship, hasPlanet, playerPlanet, eye, out facts),
            MarkerSetIntent or MarkerRemoveIntent => MarkerIntentRules.Validate(state, playerId, intent),
            RefuelIntent refuel => ValidateRefuel(refuel, hasPlanet, playerPlanet, eye, out facts),

            BlockInteractIntent interact => ValidateBlockInteract(playerId, interact, hasPlanet, playerPlanet, eye, out facts),
            BlockConfigIntent config => ValidateBlockConfig(playerId, config, hasPlanet, playerPlanet, eye, out facts),

            // The content checks of item intents (source content, counts, target room) are part of their resolution.
            ItemMoveIntent or ItemDropIntent or TakeAllIntent or QuickStackIntent => RejectReason.None,

            // Crafting: the content checks (recipe, station, sources) are part of its resolution (K6-32).
            CraftIntent => RejectReason.None,
            _ => RejectReason.InvalidParameters,
        };
    }

    /// <summary>Rate class of an intent.</summary>
    internal static TokenBucket? BucketFor(Intent intent, PlayerBuckets buckets) => intent switch
    {
        TerrainToolIntent => buckets.Terrain,
        PlaceBlockIntent or RemoveBlockIntent or ShipModeIntent or RefuelIntent or BlockInteractIntent or BlockConfigIntent => buckets.Blocks,
        MarkerSetIntent or MarkerRemoveIntent => buckets.Markers,
        ItemMoveIntent or ItemDropIntent or TakeAllIntent or QuickStackIntent or CraftIntent => buckets.Items,
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
            case ShipModeIntent ship:
                rayOrigin = ship.RayOrigin;
                return Geometry.IsFinite(ship.RayOrigin) && Geometry.IsDirection(ship.RayDirection) && Enum.IsDefined(ship.TargetMode);
            case MarkerSetIntent or MarkerRemoveIntent:
                bucket = buckets.Markers;
                return MarkerIntentRules.HasValidParameters(intent);
            case RefuelIntent refuel:
                rayOrigin = refuel.RayOrigin;
                return Geometry.IsFinite(refuel.RayOrigin) && Geometry.IsDirection(refuel.RayDirection);
            case ItemMoveIntent or ItemDropIntent or TakeAllIntent or QuickStackIntent or CraftIntent:
                bucket = buckets.Items;
                return true;
            case BlockInteractIntent interact:
                return Enum.IsDefined(interact.Action);
            case BlockConfigIntent { Config: { } config }:
                return Enum.IsDefined(config.Kind);
            default:
                return false;
        }
    }

    /// <summary>The parameters of a craft intent (K6-32): the content has recipes, the key and the count are in range, the cell of the station is in bounds.</summary>
    private RejectReason ValidateCraftParameters(CraftIntent craft)
    {
        if (survival is not { HasRecipes: true } || craft.Recipe is null)
        {
            return RejectReason.InvalidParameters;
        }

        var keyBytes = System.Text.Encoding.UTF8.GetByteCount(craft.Recipe);
        if (keyBytes is 0 or > ProtocolConstants.MaxRecipeKeyBytes || craft.Count == 0 || craft.Count > rules.MaxCraftCount)
        {
            return RejectReason.InvalidParameters;
        }

        return craft.Station is { } station && !station.Cell.IsInBounds ? RejectReason.CellOutOfBounds : RejectReason.None;
    }

    private RejectReason ValidateTerrainTool(TerrainToolIntent tool, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
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

        if (Vector3.Distance(eye, hit.Position) > rules.ReachMeters)
        {
            return RejectReason.OutOfReach;
        }

        if (!HasLineOfSight(tool.Planet, eye, hit.Position, null, default))
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

        var functionalPlacement = CheckFunctionalPlacement(place, target.Construct);
        if (functionalPlacement != RejectReason.None)
        {
            return functionalPlacement;
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

        // The parent chain resolved to the planet above, so this fails only when it is nested deeper than the overlap test follows:
        // such a construct cannot be checked against its neighbours, hence no block may be added to it.
        if (!BlockOverlap.TryGetConstructPose(state.Constructs, construct, FrameRef.Of(planet), out _, out var planetRotation))
        {
            return RejectReason.WrongPlanet;
        }

        if (BlockOverlap.OverlapsConstruct(state.Constructs, FrameRef.Of(planet), cellCenter, planetRotation, construct.Id))
        {
            return RejectReason.CellOccupied;
        }

        if (CellHitsPlayer(planet, construct.Origin, construct.Rotation, construct, cell))
        {
            return RejectReason.IntersectsPlayer;
        }

        if (construct.Mode == ConstructMode.Ship && construct.Parent.Kind == FrameKind.Planet)
        {
            var embedded = CellEmbedded(planet, construct, cell);
            if (embedded != RejectReason.None)
            {
                return embedded;
            }
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, cell);
        return RejectReason.None;
    }

    private RejectReason ValidatePlaceOnTerrain(
        PlaceBlockIntent place, TerrainTarget target, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        var block = CheckBlockType(place);
        if (block != RejectReason.None)
        {
            return block;
        }

        var functionalPlacement = CheckFunctionalPlacement(place, null);
        if (functionalPlacement != RejectReason.None)
        {
            return functionalPlacement;
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

        if (Vector3.Distance(eye, hit.Position) > rules.ReachMeters)
        {
            return RejectReason.OutOfReach;
        }

        if (!HasLineOfSight(target.Planet, eye, hit.Position, null, default))
        {
            return RejectReason.NoLineOfSight;
        }

        ConstructPlacement.OnTerrain(hit.Position, target.LookForward, target.RayDirection, out var origin, out var rotation);
        var cellCenter = origin + Vector3.Transform(new Vector3(0.5f, 0.5f, 0.5f), rotation);
        if (!terrain.IsAreaLoaded(target.Planet, cellCenter, 1f))
        {
            return RejectReason.AreaNotLoaded;
        }

        if (BlockOverlap.OverlapsConstruct(state.Constructs, FrameRef.Of(target.Planet), cellCenter, rotation, null))
        {
            return RejectReason.CellOccupied;
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

        // A ship keeps its last cockpit and its last engine: the flight model of K5-24 assumes it has both.
        if (construct.Mode == ConstructMode.Ship)
        {
            var kind = content.Get(construct.GetBlock(remove.Cell).TypeId).Function.Kind;
            if (kind == BlockFunctionKind.Cockpit && CountFunction(construct, BlockFunctionKind.Cockpit, 2) < 2)
            {
                return RejectReason.NoCockpit;
            }

            if (kind == BlockFunctionKind.Engine && CountFunction(construct, BlockFunctionKind.Engine, 2) < 2)
            {
                return RejectReason.NoEngine;
            }
        }

        // The applier refuses to remove a seat or the last block of a frame that still has children.
        if (state.TryGetSeated(construct.Id, remove.Cell, out _)
            || (construct.BlockCount == 1 && state.HasChildren(FrameRef.Of(construct.Id))))
        {
            return RejectReason.IntersectsPlayer;
        }

        // Without survival content a block with a state or a container cannot be taken down (K6-30 D21).
        if (survival is null && functional.HasFunctionalState(remove.Construct, remove.Cell))
        {
            return RejectReason.InvalidParameters;
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, remove.Cell);
        return RejectReason.None;
    }

    /// <summary>
    /// The common checks of the intents aimed at one block (K6-30 D18), in the order of <see cref="ValidateRemove"/>: the construct, the
    /// planet, the cell, reach from the eye and line of sight.
    /// </summary>
    private RejectReason CheckBlockTarget(
        EntityId constructId, CellPos cell, bool hasPlanet, PlanetId playerPlanet, Vector3 eye,
        out Construct construct, out PlanetId planet, out Vector3 cellCenter)
    {
        planet = default;
        cellCenter = default;
        if (!state.Constructs.TryGet(constructId, out construct!))
        {
            return RejectReason.UnknownConstruct;
        }

        if (!hasPlanet || !state.TryResolvePlanet(construct.Parent, out planet) || planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        if (!cell.IsInBounds)
        {
            return RejectReason.CellOutOfBounds;
        }

        if (construct.GetBlock(cell).IsEmpty)
        {
            return RejectReason.CellEmpty;
        }

        cellCenter = ToPlanet(construct, CellCenter(cell));
        if (Vector3.Distance(eye, cellCenter) > rules.ReachMeters + CellReachSlack)
        {
            return RejectReason.OutOfReach;
        }

        return HasLineOfSight(planet, eye, cellCenter, construct.Id, cell) ? RejectReason.None : RejectReason.NoLineOfSight;
    }

    /// <summary>
    /// Toggle (a door or a lamp; closing a door column may not catch an online player), Sleep and SetRespawn (a bed that is free or the
    /// own bed of the player), K6-30 D6, D10, D18.
    /// </summary>
    private RejectReason ValidateBlockInteract(
        PlayerId playerId, BlockInteractIntent intent, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        var target = CheckBlockTarget(intent.Construct, intent.Cell, hasPlanet, playerPlanet, eye, out var construct, out var planet, out var cellCenter);
        if (target != RejectReason.None)
        {
            return target;
        }

        var block = construct.GetBlock(intent.Cell);
        var kind = content.Get(block.TypeId).Function.Kind;
        if (intent.Action == BlockInteractAction.Toggle)
        {
            if (kind == BlockFunctionKind.Door)
            {
                // Open doors do not collide, so only closing can catch a player; the whole column closes together.
                if (block.FunctionState != 0)
                {
                    FunctionalBlockRules.DoorColumn(construct, intent.Cell, rules.MaxDoorColumnCells, _doorColumn);
                    for (var i = 0; i < _doorColumn.Count; i++)
                    {
                        if (CellHitsPlayer(planet, construct.Origin, construct.Rotation, construct, _doorColumn[i]))
                        {
                            return RejectReason.IntersectsPlayer;
                        }
                    }
                }
            }
            else if (kind != BlockFunctionKind.Light)
            {
                return RejectReason.InvalidParameters;
            }
        }
        else
        {
            if (kind != BlockFunctionKind.Bed || !state.BlockEntities.TryGet(new BlockEntities.BlockRef(intent.Construct, intent.Cell), out var entity)
                || entity is not BlockEntities.BedBlockState bed)
            {
                return RejectReason.InvalidParameters;
            }

            if (!bed.Owner.IsNone && bed.Owner != playerId)
            {
                return RejectReason.NotOwner;
            }
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, intent.Cell);
        return RejectReason.None;
    }

    /// <summary>The access of a chest, set by its owner; the teleport name is reserved for K6-37 (K6-30 D18, D19).</summary>
    private RejectReason ValidateBlockConfig(
        PlayerId playerId, BlockConfigIntent intent, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        var target = CheckBlockTarget(intent.Construct, intent.Cell, hasPlanet, playerPlanet, eye, out _, out var planet, out var cellCenter);
        if (target != RejectReason.None)
        {
            return target;
        }

        if (intent.Config is not ContainerAccessConfig access
            || !state.BlockEntities.TryGet(new BlockEntities.BlockRef(intent.Construct, intent.Cell), out var entity)
            || entity is not BlockEntities.ChestBlockState chest || !state.Containers.TryGet(chest.Container, out var container))
        {
            return RejectReason.InvalidParameters;
        }

        if (container.Owner != playerId)
        {
            return RejectReason.NotOwner;
        }

        if (container.Access == access.Access)
        {
            return RejectReason.InvalidParameters;
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, intent.Cell);
        return RejectReason.None;
    }

    /// <summary>A chest or a bed needs the survival content and a free block state of the construct (K6-30 D21, D23).</summary>
    private RejectReason CheckFunctionalPlacement(PlaceBlockIntent place, EntityId? construct)
    {
        var function = content.Get(place.BlockTypeId).Function;
        if (function.Kind == BlockFunctionKind.Station && Crafting.StationKinds.IsTimed(function.Station))
        {
            // A station with time has a state and two containers: it needs recipes, a free block state and a free place among the stations (K6-32 D18, D26).
            return survival is { HasRecipes: true }
                && !(construct is { } built
                    && (state.BlockEntities.CountOf(built) >= ProtocolConstants.MaxBlockEntitiesPerConstruct
                        || state.BlockEntities.StationsOf(built) >= rules.MaxTimedStationsPerConstruct))
                ? RejectReason.None
                : RejectReason.InvalidParameters;
        }

        if (function.Kind is not (BlockFunctionKind.Chest or BlockFunctionKind.Bed))
        {
            return RejectReason.None;
        }

        if (survival is null)
        {
            return RejectReason.InvalidParameters;
        }

        return construct is { } id && state.BlockEntities.CountOf(id) >= ProtocolConstants.MaxBlockEntitiesPerConstruct
            ? RejectReason.InvalidParameters
            : RejectReason.None;
    }

    private RejectReason ValidateShipMode(ShipModeIntent intent, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        if (!state.Constructs.TryGet(intent.Construct, out var construct))
        {
            return RejectReason.UnknownConstruct;
        }

        if (!hasPlanet || !state.TryResolvePlanet(construct.Parent, out var planet) || planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        var cell = intent.CockpitCell;
        if (!cell.IsInBounds)
        {
            return RejectReason.CellOutOfBounds;
        }

        var block = construct.GetBlock(cell);
        if (block.IsEmpty)
        {
            return RejectReason.CellEmpty;
        }

        if (content.Get(block.TypeId).Function.Kind != BlockFunctionKind.Cockpit)
        {
            return RejectReason.NoCockpit;
        }

        var cellCenter = ToPlanet(construct, CellCenter(cell));
        if (Vector3.Distance(eye, cellCenter) > rules.ReachMeters + CellReachSlack)
        {
            return RejectReason.OutOfReach;
        }

        if (!HasLineOfSight(planet, eye, cellCenter, construct.Id, cell))
        {
            return RejectReason.NoLineOfSight;
        }

        // K5-23: a ship stands parked on its planet, so both directions need the planet frame (a flying ship is K5-24).
        if (intent.TargetMode == construct.Mode || construct.Parent.Kind != FrameKind.Planet)
        {
            return RejectReason.InvalidParameters;
        }

        if (intent.TargetMode == ConstructMode.Ship)
        {
            var anchor = CheckSwitchToShip(planet, construct);
            if (anchor != RejectReason.None)
            {
                return anchor;
            }
        }
        else if (!IsParked(construct.Id))
        {
            // K5-23 P7, K5-24 decision 15: only a parked ship becomes static.
            return RejectReason.ShipNotParked;
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, cell);
        return RejectReason.None;
    }

    /// <summary>
    /// K5-24 decision 14 (order of the checks): the station on the planet of the player, an occupied station cell, the refuel station
    /// function, reach and line of sight like a cockpit, a parked ship within <see cref="GameRules.RefuelRangeMeters"/> of the
    /// station on the same planet, a tank not already full. The facts carry the tank capacity (the resolution fills the tank).
    /// </summary>
    private RejectReason ValidateRefuel(RefuelIntent intent, bool hasPlanet, PlanetId playerPlanet, Vector3 eye, out IntentFacts facts)
    {
        facts = default;
        if (!state.Constructs.TryGet(intent.Station, out var station))
        {
            return RejectReason.UnknownConstruct;
        }

        if (!hasPlanet || !state.TryResolvePlanet(station.Parent, out var planet) || planet != playerPlanet)
        {
            return RejectReason.WrongPlanet;
        }

        var cell = intent.StationCell;
        if (!cell.IsInBounds)
        {
            return RejectReason.CellOutOfBounds;
        }

        var block = station.GetBlock(cell);
        if (block.IsEmpty)
        {
            return RejectReason.CellEmpty;
        }

        if (content.Get(block.TypeId).Function.Kind != BlockFunctionKind.RefuelStation)
        {
            return RejectReason.InvalidParameters;
        }

        var cellCenter = ToPlanet(station, CellCenter(cell));
        if (Vector3.Distance(eye, cellCenter) > rules.ReachMeters + CellReachSlack)
        {
            return RejectReason.OutOfReach;
        }

        if (!HasLineOfSight(planet, eye, cellCenter, station.Id, cell))
        {
            return RejectReason.NoLineOfSight;
        }

        if (!state.Constructs.TryGet(intent.Ship, out var ship))
        {
            return RejectReason.UnknownConstruct;
        }

        if (ship.Mode != ConstructMode.Ship || ship.Id == station.Id)
        {
            return RejectReason.InvalidParameters;
        }

        if (!IsParked(ship.Id))
        {
            return RejectReason.ShipNotParked;
        }

        var stats = ShipStatsCalculator.Compute(ship, content);
        if (!state.TryResolvePlanet(ship.Parent, out var shipPlanet) || shipPlanet != planet
            || Vector3.Distance(ToPlanet(ship, stats.CenterOfMass), cellCenter) - stats.BoundingRadius > rules.RefuelRangeMeters)
        {
            return RejectReason.OutOfReach;
        }

        var fuel = state.ShipStates.TryGet(ship.Id, out var record) ? record.Fuel : 0f;
        if (!(fuel < stats.FuelCapacity - RefuelFullMargin))
        {
            return RejectReason.InvalidParameters;
        }

        facts = new IntentFacts(planet, cellCenter, Quaternion.Identity, cell, stats.FuelCapacity);
        return RejectReason.None;
    }

    /// <summary>A ship without a flight state has never moved since the switch, so it stands parked.</summary>
    private bool IsParked(EntityId ship) => !state.ShipStates.TryGet(ship, out var record) || (record.Flags & ShipStateFlags.Parked) != 0;

    /// <summary>Engines present, the terrain around the construct loaded and no block embedded in it.</summary>
    private RejectReason CheckSwitchToShip(PlanetId planet, Construct construct)
    {
        if (CountFunction(construct, BlockFunctionKind.Engine, 1) == 0)
        {
            return RejectReason.NoEngine;
        }

        var centre = ToPlanet(construct, (CellCenter(construct.BoundsMin) + CellCenter(construct.BoundsMax)) * 0.5f);
        var radius = (Vector3.Distance(CellCenter(construct.BoundsMin), CellCenter(construct.BoundsMax)) * 0.5f) + 1f;
        if (!terrain.IsAreaLoaded(planet, centre, radius))
        {
            return RejectReason.AreaNotLoaded;
        }

        foreach (var (chunkPos, chunk) in construct.Chunks)
        {
            var raw = chunk.Blocks;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] == 0)
                {
                    continue;
                }

                var reason = CellEmbedded(planet, construct, CellPos.FromChunk(chunkPos, i));
                if (reason != RejectReason.None)
                {
                    return reason;
                }
            }
        }

        return RejectReason.None;
    }

    /// <summary>
    /// A block is embedded when its centre or one of its corners, moved 0.1 m into the cell, lies in the terrain. Only blocks
    /// that can reach the terrain are probed. An unknown answer (area not loaded) is <see cref="RejectReason.AreaNotLoaded"/>.
    /// </summary>
    private RejectReason CellEmbedded(PlanetId planet, Construct construct, CellPos cell)
    {
        state.Planets.TryGet(planet, out var planetParams);
        var centre = construct.CellCenterToParent(cell);
        var skipBeyond = planetParams!.RadiusMeters + rules.TerrainMaxReliefMeters + AnchorSkipMargin;
        if (centre.Length() > skipBeyond + CellReachSlack)
        {
            return RejectReason.None;
        }

        const float far = 0.5f - AnchorInset;
        var local = CellCenter(cell);
        for (var i = -1; i < 8; i++)
        {
            var offset = i < 0
                ? Vector3.Zero
                : new Vector3((i & 1) == 0 ? -far : far, (i & 2) == 0 ? -far : far, (i & 4) == 0 ? -far : far);
            if (!terrain.TrySampleSolid(planet, construct.LocalToParent(local + offset), out var solid))
            {
                return RejectReason.AreaNotLoaded;
            }

            if (solid)
            {
                return RejectReason.ShipAnchored;
            }
        }

        return RejectReason.None;
    }

    /// <summary>Counts blocks with a function, stopping at <paramref name="stopAt"/>.</summary>
    private int CountFunction(Construct construct, BlockFunctionKind kind, int stopAt)
    {
        var count = 0;
        foreach (var (_, chunk) in construct.Chunks)
        {
            var raw = chunk.Blocks;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != 0 && content.Get(new BlockState(raw[i]).TypeId).Function.Kind == kind && ++count >= stopAt)
                {
                    return count;
                }
            }
        }

        return count;
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
    private bool HasLineOfSight(PlanetId planet, Vector3 eye, Vector3 target, EntityId? construct, CellPos cell)
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
        return !terrain.Raycast(planet, eye, direction, reach, out var terrainHit)
            || terrainHit.Distance >= reach - TerrainSightSlackMeters;
    }

    private bool TryGetEye(PlayerState player, out PlanetId planet, out Vector3 eye) => ContainerReach.TryGetEye(state, rules, player, out planet, out eye);

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
