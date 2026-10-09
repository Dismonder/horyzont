using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Terrain;

namespace Horyzont.Game.Client;

/// <summary>
/// The terrain the client sees. A host and solo player share the terrain of the server (one instance, no second copy and no second
/// set of collisions); a remote client builds its own from the generator. K2-11 (viewers, character collisions) and K2-13
/// (<c>GodotToolWorldView</c>) take the terrain only from here.
/// </summary>
public interface IClientTerrainProvider
{
    /// <summary>Prepares the terrain for the planets of the server hello; main thread. May be called again after a reconnect.</summary>
    /// <param name="hello">The hello of the server.</param>
    void Prepare(ServerHello hello);

    /// <summary>Gets the terrain node of a planet; the same object as the server's in host and solo mode.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="terrain">The node when known.</param>
    /// <returns>Whether the planet has a terrain.</returns>
    bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? terrain);
}
