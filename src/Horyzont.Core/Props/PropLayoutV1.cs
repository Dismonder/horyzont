using System.Buffers.Binary;
using System.IO.Hashing;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>
/// Layout V1 of the profile <c>core:planet/1</c> (K6-31 D5), frozen by <c>PropLayoutV1_GoldenDigest</c>: nothing in it may change after the merge,
/// a new look is a <c>PropLayoutV2</c> for a new profile. The decision is a function of the planet seed and the <see cref="PropId"/> alone, in
/// integers (no floating point, no trigonometry): one 64-bit roll per slot (<see cref="XxHash64"/> of seed, identifier and a salt) falls on a fixed
/// interval of the cumulative densities of the kinds of the slot; a kind whose patch noise (integer value noise on a lattice of cells with a
/// smoothstep interpolation) does not pass its threshold leaves a gap, so the intervals never move. The offset, rotation and scale come from
/// a second roll with another salt.
/// </summary>
public sealed class PropLayoutV1 : IPropLayout
{
    private const ulong RollSalt = 0x5052_4F50_524F_4C4CUL;
    private const ulong DetailSalt = 0x5052_4F50_4445_5441UL;
    private const ulong NoiseSalt = 0x5052_4F50_4E4F_4953UL;
    private const int NoiseOne = 32768;

    // Order of the table of D5 inside a slot: the cumulative density of a kind is the end of its interval.
    // Density in millionths, noise lattice spacing in cells (8 x the period in regions, rounded), noise threshold in 1/32768 (the range -1..1),
    // scale range in 1/1000 (the ranges of PlanetFlora), height band in metres over the radius of the planet.
    private static readonly KindRule[] _rules =
    [
        new(PropKindV1.Pine, 0, 224_000, 28, 4_915, true, 700, 1300, -34.5f, 125f),
        new(PropKindV1.PineTall, 0, 96_000, 28, 1_638, true, 800, 1200, -13.5f, 110f),
        new(PropKindV1.Broadleaf, 0, 160_000, 38, -1_638, true, 700, 1250, -35.5f, 70f),
        new(PropKindV1.BroadleafAutumn, 0, 77_000, 22, -8_192, true, 700, 1200, -28.5f, 90f),
        new(PropKindV1.Bush, 0, 300_000, 11, 3_277, true, 600, 1400, -37.5f, 120f),
        new(PropKindV1.Rock, 1, 384_000, 0, 0, false, 400, 1600, -300f, 300f),
        new(PropKindV1.Boulder, 1, 77_000, 20, 0, true, 600, 2200, -300f, 300f),
        new(PropKindV1.Crystals, 1, 51_000, 15, -14_746, true, 600, 1800, 1.5f, 300f),
    ];

    /// <inheritdoc />
    public bool TryGetCandidate(ulong seed, PropId id, out PropCandidate candidate)
    {
        candidate = default;
        var slot = id.Slot;
        if (!id.IsValid || slot > 1)
        {
            return false;
        }

        var roll = (uint)(Hash(seed, id.Value, RollSalt) >> 32);
        ulong end = 0;
        for (var i = 0; i < _rules.Length; i++)
        {
            ref readonly var rule = ref _rules[i];
            if (rule.Slot != slot)
            {
                continue;
            }

            end += (ulong)rule.DensityMillionths * 0x1_0000_0000UL / 1_000_000UL;
            if (roll >= end)
            {
                continue;
            }

            if (rule.HasPatches && !PassesNoise(seed, id, rule))
            {
                return false;
            }

            var detail = Hash(seed, id.Value, DetailSalt);
            var range = (ulong)(rule.MaxScale - rule.MinScale + 1);
            candidate = new PropCandidate(
                rule.Kind, (ushort)(detail & 1023UL), (ushort)((detail >> 10) & 1023UL), (ushort)((detail >> 20) & 0xFFFFUL),
                (ushort)(rule.MinScale + (int)((detail >> 36) % range)));
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool Accepts(PropKindV1 kind, float heightOverRadiusMeters)
    {
        for (var i = 0; i < _rules.Length; i++)
        {
            if (_rules[i].Kind == kind)
            {
                return heightOverRadiusMeters >= _rules[i].LowMeters && heightOverRadiusMeters <= _rules[i].HighMeters;
            }
        }

        return false;
    }

    private static ulong Hash(ulong seed, ulong value, ulong salt)
    {
        Span<byte> buffer = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, seed);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..], value);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[16..], salt);
        return XxHash64.HashToUInt64(buffer);
    }

    private static bool PassesNoise(ulong seed, PropId id, in KindRule rule)
    {
        var spacing = rule.NoiseSpacing;
        var cellU = id.CellU;
        var cellV = id.CellV;
        var latticeU = cellU / spacing;
        var latticeV = cellV / spacing;
        var smoothU = Smooth((cellU % spacing) * 65536L / spacing);
        var smoothV = Smooth((cellV % spacing) * 65536L / spacing);
        var kindSalt = NoiseSalt + (byte)rule.Kind;
        var face = (ulong)id.Face;
        var v00 = Lattice(seed, face, latticeU, latticeV, kindSalt);
        var v10 = Lattice(seed, face, latticeU + 1, latticeV, kindSalt);
        var v01 = Lattice(seed, face, latticeU, latticeV + 1, kindSalt);
        var v11 = Lattice(seed, face, latticeU + 1, latticeV + 1, kindSalt);
        var low = v00 + (((v10 - v00) * smoothU) >> 16);
        var high = v01 + (((v11 - v01) * smoothU) >> 16);
        var noise = low + (((high - low) * smoothV) >> 16);
        return noise > rule.NoiseThreshold;
    }

    // Smoothstep 3t^2 - 2t^3 of t in 0..65536, result in 0..65536.
    private static long Smooth(long t) => ((t * t) >> 16) * ((3L * 65536) - (2 * t)) >> 16;

    // A value in -32768 .. 32767 at a lattice point.
    private static long Lattice(ulong seed, ulong face, int latticeU, int latticeV, ulong salt)
    {
        var position = (face << 40) | ((ulong)(uint)latticeU << 20) | (ulong)(uint)latticeV;
        return (long)((Hash(seed, position, salt) >> 48) & 0xFFFFUL) - NoiseOne;
    }

    private readonly record struct KindRule(
        PropKindV1 Kind, int Slot, int DensityMillionths, int NoiseSpacing, int NoiseThreshold, bool HasPatches, int MinScale, int MaxScale, float LowMeters,
        float HighMeters);
}
