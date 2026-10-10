using Horyzont.Core.Ids;

namespace Horyzont.Core.Frames;

/// <summary>The placement before the star system exists: the home planet sits at the origin of the system frame, no other planet is placed.</summary>
/// <param name="home">The home planet.</param>
public sealed class HomeOnlyPlacement(PlanetId home) : IPlanetPlacement
{
    /// <summary>Gets the home planet.</summary>
    public PlanetId Home { get; } = home;

    /// <inheritdoc />
    public bool TryGetPose(PlanetId planet, out PoseD pose)
    {
        pose = PoseD.Identity;
        return planet == Home;
    }
}
