using System.Buffers.Binary;
using System.IO.Hashing;

using Horyzont.Core.Ids;

namespace Horyzont.Core.World;

/// <summary>
/// Derivation of planet identity and parameters from the galaxy seed. Results are stored when a world is
/// created and never re-derived for an existing world, but the function is frozen (golden test values),
/// because subproject 2 derives further planets of the same galaxy from the seed.
/// </summary>
public static class PlanetGenesis
{
    /// <summary>Index of the home planet of a new world.</summary>
    public const uint HomePlanetIndex = 0;

    private const long IdHashSeed = 1;
    private const long SeedHashSeed = 2;

    /// <summary>Planet id: XxHash64 of (LE galaxy seed, LE planet index) with seed 1; 0 is replaced by 1.</summary>
    /// <param name="galaxySeed">Galaxy seed.</param>
    /// <param name="planetIndex">Index of the planet in the galaxy.</param>
    /// <returns>A non-zero planet id.</returns>
    public static PlanetId DerivePlanetId(ulong galaxySeed, uint planetIndex)
    {
        var value = Hash(galaxySeed, planetIndex, IdHashSeed);
        return new PlanetId(value == 0 ? 1UL : value);
    }

    /// <summary>Generator seed of the planet: XxHash64 of the same bytes with seed 2.</summary>
    /// <param name="galaxySeed">Galaxy seed.</param>
    /// <param name="planetIndex">Index of the planet in the galaxy.</param>
    /// <returns>The planet seed.</returns>
    public static ulong DerivePlanetSeed(ulong galaxySeed, uint planetIndex) => Hash(galaxySeed, planetIndex, SeedHashSeed);

    /// <summary>Creates a planet of system 1 with the default parameters of the specification.</summary>
    /// <param name="galaxySeed">Galaxy seed.</param>
    /// <param name="planetIndex">Index of the planet in the galaxy.</param>
    /// <param name="generatorProfile">Generator profile key with version.</param>
    /// <returns>Parameters accepted by <see cref="WorldState.CreateNew(Ids.WorldId, ulong, IEnumerable{PlanetParams}, IEnumerable{string}, IEnumerable{string})"/>.</returns>
    /// <exception cref="ArgumentException">The profile is empty or longer than allowed.</exception>
    public static PlanetParams CreatePlanet(ulong galaxySeed, uint planetIndex, string generatorProfile)
    {
        if (string.IsNullOrEmpty(generatorProfile))
        {
            throw new ArgumentException("Generator profile must not be empty.", nameof(generatorProfile));
        }

        var planet = new PlanetParams(
            DerivePlanetId(galaxySeed, planetIndex), new SystemId(1), DerivePlanetSeed(galaxySeed, planetIndex), generatorProfile);
        try
        {
            planet.Validate();
        }
        catch (InvalidDataException ex)
        {
            throw new ArgumentException(ex.Message, nameof(generatorProfile), ex);
        }

        return planet;
    }

    private static ulong Hash(ulong galaxySeed, uint planetIndex, long hashSeed)
    {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, galaxySeed);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], planetIndex);
        return XxHash64.HashToUInt64(bytes, hashSeed);
    }
}
