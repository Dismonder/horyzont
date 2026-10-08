using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.World;

/// <summary>State of a player known to the world. Mutated only by the operation applier and the simulation.</summary>
public sealed class PlayerState
{
    internal PlayerState(
        PlayerId player, PlatformIdentity identity, string displayName, EntityId entity, FrameRef frame, uint parentSeq,
        CellPos? seat, Vector3 position, Quaternion bodyRotation, float pitch, bool isOnline)
    {
        Player = player;
        Identity = identity;
        DisplayName = displayName;
        Entity = entity;
        Frame = frame;
        ParentSeq = parentSeq;
        Seat = seat;
        Position = position;
        BodyRotation = bodyRotation;
        Pitch = pitch;
        IsOnline = isOnline;
    }

    /// <summary>Player id in the world.</summary>
    public PlayerId Player { get; }

    /// <summary>Platform identity linked to the player.</summary>
    public PlatformIdentity Identity { get; }

    /// <summary>Name from the last join, at most 32 UTF-8 bytes.</summary>
    public string DisplayName { get; internal set; }

    /// <summary>Entity of the player's body.</summary>
    public EntityId Entity { get; }

    /// <summary>Parent frame; in subproject 1 always the planet frame.</summary>
    public FrameRef Frame { get; internal set; }

    /// <summary>Incremented by every reparent operation.</summary>
    public uint ParentSeq { get; internal set; }

    /// <summary>Seat cell when the frame is a construct.</summary>
    public CellPos? Seat { get; internal set; }

    /// <summary>Feet position in the parent frame.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>Body rotation in the parent frame.</summary>
    public Quaternion BodyRotation { get; internal set; }

    /// <summary>Camera pitch in radians.</summary>
    public float Pitch { get; internal set; }

    /// <summary>True while the player is connected.</summary>
    public bool IsOnline { get; internal set; }

    /// <summary>Eye position in the planet frame (subproject 1). From K5-20 "up" in every frame comes from FrameMath.</summary>
    /// <param name="rules">Game rules with the eye height.</param>
    /// <returns>The eye position.</returns>
    public Vector3 EyePosition(GameRules rules) => Position + (TerrainMath.UpAt(Position) * rules.EyeHeight);
}
