using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>Pose of each planet in the system frame (K5-20 decision 5); K5-21 supplies the star system.</summary>
public interface IPlanetPlacement
{
    /// <summary>Pose of a planet frame in the system frame; the rotation of the planet is part of it.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="pose">Receives the pose.</param>
    /// <returns>False when the planet is not placed.</returns>
    bool TryGetPose(PlanetId planet, out PoseD pose);
}
