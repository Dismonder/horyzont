using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

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

    /// <summary>Gets the read-only view of the terrain of a planet; in host and solo mode it looks at the terrain of the server.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="view">The view when known.</param>
    /// <returns>Whether the planet has a terrain.</returns>
    bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out IClientTerrainView? view);

    /// <summary>
    /// The client wants the terrain of a planet (K5-22 decision 10, <c>ClientPlanetTerrains</c>): a remote client creates its node, a host
    /// holds the server terrain with a lease. Idempotent; main thread.
    /// </summary>
    /// <param name="planet">The planet.</param>
    /// <returns>False for a planet unknown to the hello.</returns>
    bool Activate(PlanetId planet);

    /// <summary>The client no longer wants the terrain of a planet (a host only drops its lease; the server may keep it). Main thread.</summary>
    /// <param name="planet">The planet.</param>
    void Release(PlanetId planet);
}
