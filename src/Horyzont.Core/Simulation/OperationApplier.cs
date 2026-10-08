using System.Numerics;
using System.Text;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Checks preconditions and applies operations. The semantics are part of the journal format: old journals must replay
/// identically, so a change here needs a new operation format version (docs/ZASADY.md, point 3).
/// Reads only the state and the operation, never game content or the terrain.
/// </summary>
internal sealed class OperationApplier(WorldState state, ITerrainService terrain, GameRules rules)
{
    private static readonly IReadOnlyList<TerrainBlockVersion> _noTerrain = [];
    private static readonly IReadOnlyList<ConstructChange> _noConstructs = [];
    private readonly List<TerrainBlockKey> _affected = [];

    /// <summary>Checks the preconditions without changing anything.</summary>
    internal bool CheckPreconditions(Operation operation, out string failure)
    {
        failure = operation switch
        {
            TerrainEditOp op => CheckTerrainEdit(op),
            ConstructCreateOp op => CheckConstructCreate(op),
            BlockSetOp op => CheckBlockSet(op),
            BlockClearOp op => CheckBlockClear(op),
            PlayerJoinOp op => CheckPlayerJoin(op),
            PlayerLeaveOp op => CheckPlayerLeave(op),
            EntityReparentOp op => CheckReparent(op),
            _ => $"Unknown operation {operation.GetType().Name}.",
        } ?? string.Empty;
        return failure.Length == 0;
    }

    /// <summary>Applies an operation whose preconditions hold and sets <see cref="WorldState.LastOpId"/>.</summary>
    internal AppliedOperation Apply(OpId id, Tick tick, PlayerId? actor, Operation operation)
    {
        var terrainVersions = _noTerrain;
        var constructChanges = _noConstructs;
        switch (operation)
        {
            case TerrainEditOp op:
                terrainVersions = ApplyTerrainEdit(op);
                break;
            case ConstructCreateOp op:
                state.EntityIds.MarkUsed(op.Construct);
                var created = state.Constructs.Create(op.Construct, op.Parent, op.Origin, op.Rotation);
                created.SetBlock(op.FirstCell, op.Block);
                constructChanges = [new ConstructChange(created.Id, created.Version, false)];
                break;
            case BlockSetOp op:
                state.Constructs.TryGet(op.Construct, out var target);
                target!.SetBlock(op.Cell, op.Block);
                constructChanges = [new ConstructChange(target.Id, target.Version, false)];
                break;
            case BlockClearOp op:
                state.Constructs.TryGet(op.Construct, out var cleared);
                cleared!.SetBlock(op.Cell, BlockState.Empty);
                var removed = cleared.BlockCount == 0;
                if (removed)
                {
                    state.Constructs.Remove(cleared.Id);
                }

                constructChanges = [new ConstructChange(cleared.Id, cleared.Version, removed)];
                break;
            case PlayerJoinOp op:
                ApplyPlayerJoin(op);
                break;
            case PlayerLeaveOp op:
                state.Players.TryGet(op.Player, out var leaving);
                leaving!.Position = op.Position;
                leaving.BodyRotation = op.BodyRotation;
                leaving.Pitch = op.Pitch;
                leaving.IsOnline = false;
                state.Players.MarkChanged(op.Player);
                break;
            case EntityReparentOp op:
                ApplyReparent(op);
                break;
            default:
                throw new InvalidOperationException($"Unknown operation {operation.GetType().Name}.");
        }

        state.SetLastOpId(id);
        return new AppliedOperation(id, tick, actor, operation, terrainVersions, constructChanges);
    }

    private TerrainBlockVersion[] ApplyTerrainEdit(TerrainEditOp op)
    {
        state.Planets.TryGet(op.Planet, out var planet);
        TerrainMath.AffectedBlocks(op.Planet, op.Center, op.Radius, planet!.VoxelSizeMeters, rules.TerrainEditMarginVoxels, _affected);
        var versions = new TerrainBlockVersion[_affected.Count];
        for (var i = 0; i < _affected.Count; i++)
        {
            versions[i] = new TerrainBlockVersion(_affected[i], state.TerrainVersions.Bump(_affected[i]));
        }

        terrain.ApplyEdit(op);
        return versions;
    }

    private void ApplyPlayerJoin(PlayerJoinOp op)
    {
        if (state.Players.TryGet(op.Player, out var known))
        {
            known.DisplayName = op.DisplayName;
            known.Position = op.Position;
            known.BodyRotation = op.BodyRotation;
            known.IsOnline = true;
            state.Players.MarkChanged(op.Player);
            return;
        }

        state.PlayerIds.MarkUsed(op.Player);
        state.EntityIds.MarkUsed(op.Entity);
        state.Players.Add(new PlayerState(
            op.Player, op.Identity, op.DisplayName, op.Entity, op.Frame, op.ParentSeq, null, op.Position, op.BodyRotation, 0f, true));
    }

    private void ApplyReparent(EntityReparentOp op)
    {
        if (state.Players.TryGetByEntity(op.Entity, out var player))
        {
            player.Frame = op.NewParent;
            player.ParentSeq = op.NewParentSeq;
            player.Position = op.LocalPosition;
            player.BodyRotation = op.LocalRotation;
            player.Seat = op.SeatCell;
            state.Players.MarkChanged(player.Player);
            return;
        }

        state.Constructs.TryGet(op.Entity, out var construct);
        construct!.Reparent(op.NewParent, op.NewParentSeq, op.LocalPosition, op.LocalRotation);
    }

    private string? CheckTerrainEdit(TerrainEditOp op)
    {
        if (!state.Planets.TryGet(op.Planet, out _))
        {
            return "unknown planet";
        }

        if (op.Shape != TerrainShape.Sphere || !Enum.IsDefined(op.Mode))
        {
            return "unknown shape or mode";
        }

        if (!Geometry.IsFinite(op.Center) || !float.IsFinite(op.Radius) || op.Radius <= 0)
        {
            return "invalid centre or radius";
        }

        if (op.Mode == TerrainEditMode.Remove ? op.MaterialId != 0 : !state.Materials.IsKnown(op.MaterialId))
        {
            return "invalid material";
        }

        return null;
    }

    private string? CheckConstructCreate(ConstructCreateOp op)
    {
        if (!state.EntityIds.IsUnused(op.Construct))
        {
            return "construct id already used";
        }

        if (op.Parent.Kind != FrameKind.Planet || !state.Planets.TryGet(new PlanetId(op.Parent.Id), out _))
        {
            return "parent is not a known planet";
        }

        if (!op.FirstCell.IsInBounds)
        {
            return "cell out of bounds";
        }

        if (!Geometry.IsFinite(op.Origin) || !Geometry.IsRotation(op.Rotation))
        {
            return "invalid pose";
        }

        return CheckBlock(op.Block);
    }

    private string? CheckBlockSet(BlockSetOp op)
    {
        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (!op.Cell.IsInBounds || !construct.GetBlock(op.Cell).IsEmpty)
        {
            return "cell out of bounds or occupied";
        }

        return CheckBlock(op.Block);
    }

    private string? CheckBlockClear(BlockClearOp op)
    {
        if (!state.Constructs.TryGet(op.Construct, out var construct))
        {
            return "unknown construct";
        }

        if (!op.Cell.IsInBounds || construct.GetBlock(op.Cell).IsEmpty)
        {
            return "cell out of bounds or empty";
        }

        if (state.TryGetSeated(op.Construct, op.Cell, out _))
        {
            return "a player sits in the cell";
        }

        if (construct.BlockCount == 1 && state.HasChildren(FrameRef.Of(op.Construct)))
        {
            return "the last block of a frame with children";
        }

        return null;
    }

    private string? CheckPlayerJoin(PlayerJoinOp op)
    {
        if (!IsDisplayName(op.DisplayName) || !Geometry.IsFinite(op.Position) || !Geometry.IsRotation(op.BodyRotation))
        {
            return "invalid name or pose";
        }

        if (state.Players.TryGet(op.Player, out var known))
        {
            if (known.IsOnline)
            {
                return "player already online";
            }

            if (known.Identity != op.Identity || known.Entity != op.Entity || known.Frame != op.Frame || known.ParentSeq != op.ParentSeq)
            {
                return "identity, entity, frame or ParentSeq differ from the known player";
            }

            return null;
        }

        if (!state.PlayerIds.IsUnused(op.Player))
        {
            return "player id already used";
        }

        if (state.Players.TryGetByIdentity(op.Identity, out _))
        {
            return "identity belongs to another player";
        }

        if (!state.EntityIds.IsUnused(op.Entity))
        {
            return "entity id already used";
        }

        if (!state.FrameExists(op.Frame) || op.ParentSeq != 0)
        {
            return "frame missing or ParentSeq not 0";
        }

        return null;
    }

    private string? CheckPlayerLeave(PlayerLeaveOp op)
    {
        if (!state.Players.TryGet(op.Player, out var player) || !player.IsOnline)
        {
            return "player unknown or offline";
        }

        if (player.Frame != op.Frame || player.ParentSeq != op.ParentSeq)
        {
            return "frame or ParentSeq differ";
        }

        if (!Geometry.IsFinite(op.Position) || !Geometry.IsRotation(op.BodyRotation) || !float.IsFinite(op.Pitch))
        {
            return "invalid pose";
        }

        return null;
    }

    private string? CheckReparent(EntityReparentOp op)
    {
        uint currentSeq;
        var isPlayer = state.Players.TryGetByEntity(op.Entity, out var player);
        if (isPlayer)
        {
            currentSeq = player!.ParentSeq;
        }
        else if (state.Constructs.TryGet(op.Entity, out var construct))
        {
            currentSeq = construct.ParentSeq;
        }
        else
        {
            return "entity has no frame";
        }

        if (currentSeq == uint.MaxValue || op.NewParentSeq != currentSeq + 1)
        {
            return "NewParentSeq is not current + 1";
        }

        if (!state.FrameExists(op.NewParent))
        {
            return "new frame does not exist";
        }

        if (!isPlayer && IsSelfOrDescendant(op.NewParent, op.Entity))
        {
            return "new frame is the construct or its descendant";
        }

        if (!Geometry.IsFinite(op.LocalPosition) || !Geometry.IsFinite(op.LocalVelocity) || !Geometry.IsRotation(op.LocalRotation))
        {
            return "invalid pose";
        }

        if (op.SeatCell is { } seat)
        {
            if (!isPlayer || op.NewParent.Kind != FrameKind.Construct)
            {
                return "seat only for a player entering a construct";
            }

            var seatConstruct = new EntityId(op.NewParent.Id);
            state.Constructs.TryGet(seatConstruct, out var target);
            if (!seat.IsInBounds || target!.GetBlock(seat).IsEmpty)
            {
                return "seat cell empty";
            }

            if (state.TryGetSeated(seatConstruct, seat, out var seated) && seated.Player != player!.Player)
            {
                return "seat taken";
            }
        }

        return null;
    }

    private bool IsSelfOrDescendant(FrameRef frame, EntityId construct)
    {
        for (var depth = 0; depth <= state.Constructs.Count; depth++)
        {
            if (frame.Kind != FrameKind.Construct)
            {
                return false;
            }

            if (frame.Id == construct.Value)
            {
                return true;
            }

            if (!state.Constructs.TryGet(new EntityId(frame.Id), out var parent))
            {
                return false;
            }

            frame = parent.Parent;
        }

        return true;
    }

    private string? CheckBlock(BlockState block)
    {
        if (block.IsEmpty || !block.IsWellFormed || !state.BlockTypes.IsKnown(block.TypeId))
        {
            return "block malformed or of unknown type";
        }

        return null;
    }

    internal static bool IsDisplayName(string? name) =>
        !string.IsNullOrEmpty(name) && Encoding.UTF8.GetByteCount(name) <= ProtocolConstants.MaxDisplayNameBytes;

    internal static bool IsPose(Vector3 position, Quaternion rotation) => Geometry.IsFinite(position) && Geometry.IsRotation(rotation);
}
