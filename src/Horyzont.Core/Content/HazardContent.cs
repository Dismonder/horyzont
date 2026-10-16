using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Content;

/// <summary>
/// The hazard content (<c>content/hazards/hazards.json</c>, K6-34 D8): the profiles, the profiles of the bodies, the default profile, the profile of space and the oxygen generator. Absent
/// for a world without the file (then the server behaves as in K6-33).
/// </summary>
/// <param name="Profiles">The profiles, 1 to 64, unique keys.</param>
/// <param name="Bodies">The profile of a body by its index; a body without an entry has <see cref="DefaultProfile"/>.</param>
/// <param name="DefaultProfile">Key of the profile of a body without an entry.</param>
/// <param name="SpaceProfile">Key of the profile of a player outside the atmosphere of a planet.</param>
/// <param name="Generator">The oxygen generator.</param>
public sealed record HazardContent(
    IReadOnlyList<HazardProfileDef> Profiles, IReadOnlyList<HazardBodyDef> Bodies, string DefaultProfile, string SpaceProfile, OxygenGeneratorDef Generator)
{
    /// <summary>Looks up a profile by key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="profile">The profile when found.</param>
    /// <returns>True when the content has the profile.</returns>
    public bool TryGetProfile(string key, [NotNullWhen(true)] out HazardProfileDef? profile)
    {
        for (var i = 0; i < Profiles.Count; i++)
        {
            if (Profiles[i].Key == key)
            {
                profile = Profiles[i];
                return true;
            }
        }

        profile = null;
        return false;
    }
}
