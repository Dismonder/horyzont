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

    /// <summary>Reads the feet of the server character of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="planet">Planet of the character.</param>
    /// <param name="feet">Feet in the planet frame.</param>
    /// <returns>Whether the player has a body.</returns>
    bool TryGetFeet(PlayerId player, out PlanetId planet, out Vector3 feet);

    /// <summary>Sends a reliable game message to a player in the Loading or InGame state.</summary>
    /// <param name="player">The recipient.</param>
    /// <param name="message">The message.</param>
    void Send(PlayerId player, Message message);
}
