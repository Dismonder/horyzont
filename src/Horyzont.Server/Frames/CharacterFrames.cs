using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships;

namespace Horyzont.Server.Frames;

/// <summary>What the server body of a player does after its frame changed (K5-20, decision 15).</summary>
public enum CharacterFrameAction : byte
{
    /// <summary>The frame did not change.</summary>
    None = 0,

    /// <summary>The player is in a construct frame of the body planet: the body is attached (kinematic, no collision).</summary>
    Attach = 1,

    /// <summary>The player is back in the frame of the body planet: the body walks again from the pose of the operation.</summary>
    Walk = 2,

    /// <summary>The player is on another planet (directly or through a construct): the body and its viewer are spawned there anew.</summary>
    Respawn = 3,

    /// <summary>A frame the server bodies do not support (the system frame, P6, or an unknown frame): the body is left as it is.</summary>
    Unsupported = 4,

    /// <summary>
    /// Ejected from a construct into the frame of the body planet with a velocity (K5-25 decision 19, R4): the body glides from the
    /// pose of the operation with that velocity, without the frozen ground wait (the ship gate keeps the terrain under it ready).
    /// </summary>
    Glide = 5,
}

/// <summary>
/// The frame rules of the server bodies (K5-20, decision 15; review K5, B2): what a reparent of a player means for its body and
/// where an attached body stands. Pure, no allocation.
/// </summary>
public static class CharacterFrames
{
    /// <summary>Decides what the body does after the frame of its player changed.</summary>
    /// <param name="graph">The frame graph of the world.</param>
    /// <param name="bodyPlanet">The planet the body lives on.</param>
    /// <param name="bodySeq">The reparent counter the body follows.</param>
    /// <param name="frame">The frame of the player in the simulation.</param>
    /// <param name="seq">The reparent counter of the player in the simulation.</param>
    /// <param name="planet">Receives the planet the body must live on (the body planet when it does not change).</param>
    /// <returns>The action.</returns>
    public static CharacterFrameAction Decide(FrameGraph graph, PlanetId bodyPlanet, uint bodySeq, FrameRef frame, uint seq, out PlanetId planet)
    {
        ArgumentNullException.ThrowIfNull(graph);
        planet = bodyPlanet;
        if (seq == bodySeq)
        {
            return CharacterFrameAction.None;
        }

        switch (frame.Kind)
        {
            case FrameKind.Planet:
                planet = new PlanetId(frame.Id);
                return planet == bodyPlanet ? CharacterFrameAction.Walk : CharacterFrameAction.Respawn;
            case FrameKind.Construct when graph.TryGetRegion(frame, out var region):
                if (region.Kind != FrameKind.Planet)
                {
                    // A ship in the system frame: the body stays on its planet; an attached body is posed globally anyway.
                    return CharacterFrameAction.Attach;
                }

                planet = new PlanetId(region.Id);
                return planet == bodyPlanet ? CharacterFrameAction.Attach : CharacterFrameAction.Respawn;
            default:
                return CharacterFrameAction.Unsupported;
        }
    }

    /// <summary>
    /// <see cref="Decide(FrameGraph, PlanetId, uint, FrameRef, uint, out PlanetId)"/> for a body that was in <paramref name="previousFrame"/>
    /// and an ejection velocity of the reparent: a walk back to the body planet from a construct frame with a velocity above
    /// <see cref="EjectRules.GlideMinSpeed"/> is <see cref="CharacterFrameAction.Glide"/> (the rule of <see cref="EjectRules.StartsGlide"/>).
    /// </summary>
    /// <param name="graph">The frame graph of the world.</param>
    /// <param name="bodyPlanet">The planet the body lives on.</param>
    /// <param name="bodySeq">The reparent counter the body follows.</param>
    /// <param name="previousFrame">Kind of the frame the body followed so far.</param>
    /// <param name="frame">The frame of the player in the simulation.</param>
    /// <param name="seq">The reparent counter of the player in the simulation.</param>
    /// <param name="ejectVelocity">Velocity of the reparent in the new frame (zero when none was recorded).</param>
    /// <param name="planet">Receives the planet the body must live on.</param>
    /// <returns>The action.</returns>
    public static CharacterFrameAction Decide(
        FrameGraph graph, PlanetId bodyPlanet, uint bodySeq, FrameKind previousFrame, FrameRef frame, uint seq, Vector3 ejectVelocity, out PlanetId planet)
    {
        var action = Decide(graph, bodyPlanet, bodySeq, frame, seq, out planet);
        return action == CharacterFrameAction.Walk && previousFrame == FrameKind.Construct && ejectVelocity.Length() > EjectRules.GlideMinSpeed
            ? CharacterFrameAction.Glide
            : action;
    }

    /// <summary>
    /// Where a new body of a player in <paramref name="frame"/> lives, by the same rules as <see cref="Decide(FrameGraph, PlanetId, uint, FrameRef, uint, out PlanetId)"/>: a planet frame is that
    /// planet; a construct frame is attached on the planet of its region, or on <paramref name="fallback"/> when the region is the
    /// system frame (a ship between planets: the body stays on its last planet, posed globally anyway).
    /// </summary>
    /// <param name="graph">The frame graph of the world.</param>
    /// <param name="frame">The frame of the player in the simulation.</param>
    /// <param name="fallback">The last planet of the body, or the home planet; default when there is none.</param>
    /// <param name="planet">Receives the planet of the body.</param>
    /// <param name="attached">Receives whether the body is attached to a construct frame.</param>
    /// <returns>False for the system frame, an unknown frame or a ship in the system frame without a fallback.</returns>
    public static bool TrySpawnPlanet(FrameGraph graph, FrameRef frame, PlanetId fallback, out PlanetId planet, out bool attached)
    {
        ArgumentNullException.ThrowIfNull(graph);
        planet = default;
        attached = false;
        switch (frame.Kind)
        {
            case FrameKind.Planet:
                planet = new PlanetId(frame.Id);
                return true;
            case FrameKind.Construct when graph.TryGetRegion(frame, out var region):
                planet = region.Kind == FrameKind.Planet ? new PlanetId(region.Id) : fallback;
                attached = true;
                return planet != default;
            default:
                return false;
        }
    }

    /// <summary>The world pose of an attached body: the frame pose composed with the local pose of the player.</summary>
    /// <param name="graph">The frame graph.</param>
    /// <param name="frame">The frame of the player.</param>
    /// <param name="localPosition">Feet position in the frame.</param>
    /// <param name="localRotation">Body rotation in the frame.</param>
    /// <param name="world">Receives the world pose.</param>
    /// <returns>False when the frame has no world pose.</returns>
    public static bool TryAttachedWorldPose(FrameGraph graph, FrameRef frame, Vector3 localPosition, Quaternion localRotation, out PoseD world)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.TryGetWorldPose(frame, out var pose))
        {
            world = PoseD.Identity;
            return false;
        }

        world = FrameMath.Compose(pose, FrameMath.FromFloat(localPosition, localRotation));
        return true;
    }
}
