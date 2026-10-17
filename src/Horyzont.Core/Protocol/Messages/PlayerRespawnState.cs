using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Progress;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>What the body of the receiving player is waiting for (K6-35 B1 review; the value 2 is reserved for the teleport of K6-37).</summary>
public enum PlayerTransitionKind : byte
{
    /// <summary>Nothing: the body is in the world.</summary>
    None = 0,

    /// <summary>The player died and waits for the new body to be ready (the refusal <c>InTransition</c> applies meanwhile).</summary>
    Respawn = 1,
}

/// <summary>One tombstone of the receiving player in the list of message 48: no contents, those come only through a container subscription.</summary>
/// <param name="Entity">The persistent entity.</param>
/// <param name="Container">The container it hosts.</param>
/// <param name="Frame">The planet frame of the entity.</param>
/// <param name="ParentSeq">Reparent counter of the frame.</param>
/// <param name="Position">Position in the frame.</param>
public readonly record struct TombstoneEntry(EntityId Entity, ContainerId Container, FrameRef Frame, uint ParentSeq, Vector3 Position);

/// <summary>The death that created this state: the operation and the counter of the new body, so the client shows the summary once.</summary>
/// <param name="Op">The operation of the death.</param>
/// <param name="TargetParentSeq">The reparent counter of the body after the respawn.</param>
public readonly record struct DeathNotice(OpId Op, uint TargetParentSeq);

/// <summary>
/// The tail that K6-35 appends to message 48 (<see cref="PlayerProgressState"/>), after the food: an optional respawn point (<c>bool</c>, then the bed), the
/// transition (a byte, <see cref="PlayerTransitionKind"/>), the list of tombstones as a page (<c>u32</c> revision, <c>u16</c> page, <c>u16</c> page count of at least 1, a varuint
/// count of at most <see cref="ProtocolConstants.MaxTombstonesPerPage"/> and the entries: varuint entity, varuint container, frame, varuint counter, three <c>f32</c>) and an optional
/// death notice (<c>bool</c>, varuint operation, varuint counter). A page above the page count is an error; the client replaces its list only when it holds every page of
/// one revision.
/// </summary>
/// <param name="Bed">The respawn point of the player, the reference only.</param>
/// <param name="Transition">What the body waits for.</param>
/// <param name="Revision">Revision of the list; a page of an older revision is ignored.</param>
/// <param name="Page">Index of this page, from 0.</param>
/// <param name="PageCount">Number of pages of the revision, at least 1 (an empty list is one empty page).</param>
/// <param name="Tombstones">The entries of this page, at most <see cref="ProtocolConstants.MaxTombstonesPerPage"/>.</param>
/// <param name="Death">The death that happened, when this message announces one.</param>
public sealed record PlayerRespawnState(
    BedRef? Bed, PlayerTransitionKind Transition, uint Revision, ushort Page, ushort PageCount, IReadOnlyList<TombstoneEntry> Tombstones, DeathNotice? Death)
{
    /// <summary>Gets the state of a player with no bed, no tombstones and no transition: the tail a server without the package would not need.</summary>
    public static PlayerRespawnState None { get; } = new(null, PlayerTransitionKind.None, 0, 0, 1, [], null);
}
