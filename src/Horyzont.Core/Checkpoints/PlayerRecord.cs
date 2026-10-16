using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Checkpoints;

/// <summary>A player in a checkpoint.</summary>
/// <param name="Player">Player id.</param>
/// <param name="Identity">Platform identity.</param>
/// <param name="DisplayName">Name from the last join.</param>
/// <param name="Entity">Body entity.</param>
/// <param name="Frame">Parent frame.</param>
/// <param name="ParentSeq">Reparent counter.</param>
/// <param name="Seat">Seat cell when the frame is a construct.</param>
/// <param name="Position">Feet position in the parent frame.</param>
/// <param name="BodyRotation">Body rotation in the parent frame.</param>
/// <param name="Pitch">Camera pitch.</param>
/// <param name="IsOnline">Whether the player was online.</param>
/// <param name="Vitals">Health and stamina (K6-33, blob 2 version 2), null for a player whose vitals were never set (version 1).</param>
public sealed record PlayerRecord(
    PlayerId Player, PlatformIdentity Identity, string DisplayName, EntityId Entity, FrameRef Frame, uint ParentSeq,
    CellPos? Seat, Vector3 Position, Quaternion BodyRotation, float Pitch, bool IsOnline, PlayerVitals? Vitals = null)
{
    /// <summary>Record of a live player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The record.</returns>
    public static PlayerRecord From(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new(
            player.Player, player.Identity, player.DisplayName, player.Entity, player.Frame, player.ParentSeq, player.Seat,
            player.Position, player.BodyRotation, player.Pitch, player.IsOnline, player.Vitals);
    }
}
