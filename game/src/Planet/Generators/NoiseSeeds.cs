using System.Buffers.Binary;
using System.IO.Hashing;

namespace Horyzont.Game.Planet.Generators;

/// <summary>Noise seeds of a planet generator, derived from the planet seed. Part of the generator output (frozen per profile).</summary>
public static class NoiseSeeds
{
    public const int ContinentSlot = 1;
    public const int DetailSlot = 2;
    public const int RidgesSlot = 3;
    public const int CavesSlot = 4;
    public const int OreSlot = 5;

    /// <summary>Lower 32 bits of XxHash64 of the little-endian planet seed and slot.</summary>
    public static int For(ulong planetSeed, int slot)
    {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, planetSeed);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], slot);
        return unchecked((int)(uint)(XxHash64.HashToUInt64(bytes) & 0xFFFFFFFFUL));
    }
}
