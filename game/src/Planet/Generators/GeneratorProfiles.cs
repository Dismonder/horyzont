using Godot;

using Horyzont.Core.World;

namespace Horyzont.Game.Planet.Generators;

/// <summary>
/// Known generator profiles. The output of a profile never changes once merged: a new generator is a new profile next to the
/// old one, and old worlds keep using the profile stored in their <see cref="PlanetParams.GeneratorProfile"/>.
/// </summary>
public static class GeneratorProfiles
{
    public const string PlanetV1 = "core:planet/1";

    /// <summary>Profile of new worlds.</summary>
    public const string Current = PlanetV1;

    private static readonly string[] _known = [PlanetV1];

    /// <summary>Known profiles sorted ordinally and joined with ';'; a component of the build hash, so peers with different generators do not connect.</summary>
    public static string BuildHashComponent { get; } = string.Join(';', _known.Order(StringComparer.Ordinal));

    /// <summary>Whether the profile is known to this build.</summary>
    public static bool IsSupported(string profile) => Array.IndexOf(_known, profile) >= 0;

    /// <summary>Creates and compiles the generator of the profile of the planet. Main thread.</summary>
    /// <exception cref="NotSupportedException">Unknown profile or a voxel size other than 1.</exception>
    public static VoxelGeneratorGraph CreateGenerator(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        if (planet.VoxelSizeMeters != 1f)
        {
            throw new NotSupportedException($"Voxel size {planet.VoxelSizeMeters} is not supported; only 1 m voxels.");
        }

        return planet.GeneratorProfile switch
        {
            PlanetV1 => PlanetGeneratorV1.Create(planet),
            _ => throw new NotSupportedException($"Unknown generator profile '{planet.GeneratorProfile}'."),
        };
    }
}
