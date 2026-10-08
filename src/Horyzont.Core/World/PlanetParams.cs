using System.Text;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;

namespace Horyzont.Core.World;

/// <summary>Parameters of a planet. All distances in metres.</summary>
/// <param name="Id">Planet id, not 0.</param>
/// <param name="System">Star system (1 in subprojects 1–6).</param>
/// <param name="Seed">Generator seed.</param>
/// <param name="GeneratorProfile">Generator profile with version, at most 64 UTF-8 bytes.</param>
/// <param name="RadiusMeters">Surface radius.</param>
/// <param name="VoxelSizeMeters">Terrain voxel size.</param>
/// <param name="Gravity">Surface gravity in m/s².</param>
/// <param name="AtmosphereHeightMeters">Height of the atmosphere above the surface radius.</param>
/// <param name="CoreDepthMeters">Depth below the surface radius at which digging stops.</param>
/// <param name="DayLengthSeconds">Sun cycle of subproject 2; from K5-21 the star system computes the time of day.</param>
public sealed record PlanetParams(
    PlanetId Id,
    SystemId System,
    ulong Seed,
    string GeneratorProfile,
    float RadiusMeters = 4000f,
    float VoxelSizeMeters = 1f,
    float Gravity = 9.81f,
    float AtmosphereHeightMeters = 1000f,
    float CoreDepthMeters = 400f,
    float DayLengthSeconds = 1800f)
{
    /// <summary>Checks the parameters; used when a world is created or loaded.</summary>
    internal void Validate()
    {
        if (Id.Value == 0)
        {
            throw new InvalidDataException("Planet id 0 is reserved.");
        }

        if (string.IsNullOrEmpty(GeneratorProfile)
            || Encoding.UTF8.GetByteCount(GeneratorProfile) > ProtocolConstants.MaxRegistryKeyBytes)
        {
            throw new InvalidDataException($"Planet {Id.Value}: generator profile must have 1..64 UTF-8 bytes.");
        }

        if (!Positive(RadiusMeters) || !Positive(VoxelSizeMeters) || !float.IsFinite(Gravity) || !Positive(DayLengthSeconds)
            || !float.IsFinite(AtmosphereHeightMeters) || AtmosphereHeightMeters < 0
            || !float.IsFinite(CoreDepthMeters) || CoreDepthMeters < 0 || CoreDepthMeters >= RadiusMeters)
        {
            throw new InvalidDataException($"Planet {Id.Value}: parameters out of range.");
        }
    }

    private static bool Positive(float value) => float.IsFinite(value) && value > 0;
}
