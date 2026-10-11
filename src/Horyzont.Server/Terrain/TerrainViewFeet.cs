using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;

namespace Horyzont.Server.Terrain;

/// <summary>
/// The feet of a body expressed in the frame of the planet a client views (K5-22 decision 8), for
/// <see cref="ITerrainReplicationHost.GetFeet"/>: through the frame graph, accepted within
/// <see cref="TerrainReplicationPolicy.OtherPlanetViewFactor"/> R of the planet centre; a player in a construct frame gets the radius of
/// the construct as extra collision viewer tolerance. Pure; allocation-free.
/// </summary>
public static class TerrainViewFeet
{
    /// <summary>Resolves the feet of a body in a viewed planet frame.</summary>
    /// <param name="graph">The frame graph of the world.</param>
    /// <param name="state">The world (planets, constructs).</param>
    /// <param name="playerFrame">The frame of the player in the simulation.</param>
    /// <param name="bodyPlanet">The planet of the body.</param>
    /// <param name="bodyFeet">The feet in the frame of <paramref name="bodyPlanet"/>.</param>
    /// <param name="viewed">The planet whose terrain the client views.</param>
    /// <param name="feetInPlanet">Receives the feet in the frame of <paramref name="viewed"/>.</param>
    /// <param name="extraTolerance">Receives the extra collision viewer tolerance (ship radius) or 0.</param>
    /// <returns><see cref="TerrainFeet.Found"/> or <see cref="TerrainFeet.TooFar"/>.</returns>
    public static TerrainFeet Resolve(
        FrameGraph graph, WorldState state, FrameRef playerFrame, PlanetId bodyPlanet, Vector3 bodyFeet, PlanetId viewed, out Vector3 feetInPlanet,
        out float extraTolerance)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(state);
        feetInPlanet = default;
        extraTolerance = 0f;
        if (!state.Planets.TryGet(viewed, out var planet))
        {
            return TerrainFeet.TooFar;
        }

        Vector3D point;
        if (bodyPlanet == viewed)
        {
            point = Vector3D.FromFloat(bodyFeet);
        }
        else if (!graph.TryExpress(FrameRef.Of(bodyPlanet), bodyFeet, FrameRef.Of(viewed), out point))
        {
            return TerrainFeet.TooFar;
        }

        if (!point.IsFinite || point.Length() > TerrainReplicationPolicy.OtherPlanetViewFactor * planet.RadiusMeters)
        {
            return TerrainFeet.TooFar;
        }

        feetInPlanet = new Vector3((float)point.X, (float)point.Y, (float)point.Z);
        if (playerFrame.Kind == FrameKind.Construct && state.Constructs.TryGet(new EntityId(playerFrame.Id), out var construct))
        {
            extraTolerance = Horyzont.Server.Space.ShipTerrainColumns.BoundingRadius(construct);
        }

        return TerrainFeet.Found;
    }
}
