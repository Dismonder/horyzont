using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Server.Terrain;

/// <summary>What the terrain replicator needs from the running server; the game implements it over the session and the characters.</summary>
public interface ITerrainReplicationHost
{
    /// <summary>Gets a value indicating whether declared viewers may be far from the feet (a developer camera is allowed).</summary>
    bool FreeCameraAllowed { get; }

    /// <summary>Whether a player is the local player of a host, who shares the terrain of the server and gets no terrain traffic.</summary>
    /// <param name="player">The player.</param>
    /// <returns>True for the local player.</returns>
    bool IsLocalPlayer(PlayerId player);

    /// <summary>
    /// Reads the feet of the server character of a player expressed in the frame of <paramref name="planet"/> (K5-22 decision 8: through
    /// the frame graph, also for a passenger in a ship frame), so a client approaching another planet may declare its terrain view.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="planet">The planet whose terrain the client views.</param>
    /// <param name="feetInPlanet">Feet in the frame of <paramref name="planet"/>.</param>
    /// <param name="extraTolerance">
    /// Added to <see cref="TerrainReplicationPolicy.ViewerTolerance"/> for the collision viewer: the ship radius for a player in a ship frame
    /// (the collision viewer of a passenger stands in the middle of the ship), otherwise 0.
    /// </param>
    /// <returns>
    /// <see cref="TerrainFeet.Found"/> within <see cref="TerrainReplicationPolicy.OtherPlanetViewFactor"/> R of the centre,
    /// <see cref="TerrainFeet.TooFar"/> for a body farther away, <see cref="TerrainFeet.NoBody"/> without a body.
    /// </returns>
    TerrainFeet GetFeet(PlayerId player, PlanetId planet, out Vector3 feetInPlanet, out float extraTolerance);

    /// <summary>Sends a reliable game message to a player in the Loading or InGame state.</summary>
    /// <param name="player">The recipient.</param>
    /// <param name="message">The message.</param>
    void Send(PlayerId player, Message message);
}
