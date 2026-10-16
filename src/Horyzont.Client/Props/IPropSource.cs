using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;

namespace Horyzont.Client.Props;

/// <summary>Where the picker finds the props around the eye: the anchors the client draws (same layout and surface as the server).</summary>
public interface IPropSource
{
    /// <summary>Collects the props whose anchor lies within a sphere.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="center">Centre of the sphere in the frame of the planet.</param>
    /// <param name="radius">Radius of the sphere in metres.</param>
    /// <param name="result">Receives the props (appended; the caller clears it).</param>
    void CollectInSphere(PlanetId planet, Vector3 center, float radius, List<PropInstance> result);
}
