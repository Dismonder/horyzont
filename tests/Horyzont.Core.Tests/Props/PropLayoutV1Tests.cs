using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;

namespace Horyzont.Core.Tests.Props;

/// <summary>K6-31 D5: the frozen prop layout V1 of the profile <c>core:planet/1</c>.</summary>
public sealed class PropLayoutV1Tests
{
    // K6-31 A6: recorded after the Opus 5.5 review of A1 approved the table of D5. Changing PropLayoutV1 changes this value: a new look is a
    // PropLayoutV2 for a new generator profile, never an edit of V1.
    private const ulong LayoutV1Digest = 3687299226734270200UL;

    private static readonly PropLayoutV1 _layout = new();

    // Density per cell and scale range (1/1000) per kind: the table of D5 and the scale ranges of PlanetFlora.
    private static readonly (PropKindV1 Kind, int Slot, double Density, int MinScale, int MaxScale)[] _kinds =
    [
        (PropKindV1.Pine, 0, 0.224, 700, 1300),
        (PropKindV1.PineTall, 0, 0.096, 800, 1200),
        (PropKindV1.Broadleaf, 0, 0.160, 700, 1250),
        (PropKindV1.BroadleafAutumn, 0, 0.077, 700, 1200),
        (PropKindV1.Bush, 0, 0.300, 600, 1400),
        (PropKindV1.Rock, 1, 0.384, 400, 1600),
        (PropKindV1.Boulder, 1, 0.077, 600, 2200),
        (PropKindV1.Crystals, 1, 0.051, 600, 1800),
    ];

    [Fact]
    public void SameSeedAndId_SameCandidate()
    {
        var other = new PropLayoutV1();
        var found = 0;
        for (var u = 0; u < 40; u++)
        {
            for (var v = 0; v < 40; v++)
            {
                var id = PropId.Create(2, 100 + u, 200 + v, 0);
                var first = _layout.TryGetCandidate(42, id, out var a);
                var second = other.TryGetCandidate(42, id, out var b);

                Assert.Equal(first, second);
                Assert.Equal(a, b);
                found += first ? 1 : 0;
            }
        }

        // A layout that never places anything would agree with itself.
        Assert.InRange(found, 100, 1500);
    }

    [Fact]
    public void DifferentSeed_DifferentLayout()
    {
        var different = 0;
        for (var u = 0; u < 60; u++)
        {
            for (var v = 0; v < 60; v++)
            {
                var id = PropId.Create(0, 300 + u, 300 + v, 0);
                var a = _layout.TryGetCandidate(42, id, out var first);
                var b = _layout.TryGetCandidate(1234, id, out var second);
                different += a != b || first != second ? 1 : 0;
            }
        }

        Assert.True(different > 400, $"{different} of 3600 cells differ");
    }

    [Fact]
    public void KindsOnlyInOwnSlot()
    {
        var kinds = new Dictionary<int, HashSet<PropKindV1>> { [0] = [], [1] = [], [2] = [], [3] = [] };
        for (var u = 0; u < 200; u++)
        {
            for (var v = 0; v < 200; v++)
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    if (_layout.TryGetCandidate(42, PropId.Create(4, 50 + u, 70 + v, slot), out var candidate))
                    {
                        kinds[slot].Add(candidate.Kind);
                    }
                }
            }
        }

        Assert.Equal(
            [PropKindV1.Pine, PropKindV1.PineTall, PropKindV1.Broadleaf, PropKindV1.BroadleafAutumn, PropKindV1.Bush], kinds[0].Order());
        Assert.Equal([PropKindV1.Rock, PropKindV1.Boulder, PropKindV1.Crystals], kinds[1].Order());
        Assert.Empty(kinds[2]);
        Assert.Empty(kinds[3]);
    }

    [Fact]
    public void CandidateFields_WithinRanges()
    {
        var checkedCandidates = 0;
        for (var u = 0; u < 100; u++)
        {
            for (var v = 0; v < 100; v++)
            {
                for (var slot = 0; slot < 2; slot++)
                {
                    if (!_layout.TryGetCandidate(7, PropId.Create(1, u, v, slot), out var candidate))
                    {
                        continue;
                    }

                    var range = _kinds.Single(k => k.Kind == candidate.Kind);
                    Assert.InRange((int)candidate.OffsetU, 0, 1023);
                    Assert.InRange((int)candidate.OffsetV, 0, 1023);
                    Assert.InRange((int)candidate.Scale, range.MinScale, range.MaxScale);
                    checkedCandidates++;
                }
            }
        }

        Assert.True(checkedCandidates > 1000, $"{checkedCandidates} candidates");
    }

    [Fact]
    public void HeightBand_RejectsOutside()
    {
        // Bands of D5, metres over the planet radius.
        (PropKindV1 Kind, float Low, float High)[] bands =
        [
            (PropKindV1.Pine, -34.5f, 125f), (PropKindV1.PineTall, -13.5f, 110f), (PropKindV1.Broadleaf, -35.5f, 70f),
            (PropKindV1.BroadleafAutumn, -28.5f, 90f), (PropKindV1.Bush, -37.5f, 120f), (PropKindV1.Rock, -300f, 300f),
            (PropKindV1.Boulder, -300f, 300f), (PropKindV1.Crystals, 1.5f, 300f),
        ];

        foreach (var (kind, low, high) in bands)
        {
            var middle = (low + high) / 2f;
            Assert.True(_layout.Accepts(kind, middle), $"{kind} at {middle}");
            Assert.True(_layout.Accepts(kind, low + 0.01f), $"{kind} just above {low}");
            Assert.True(_layout.Accepts(kind, high - 0.01f), $"{kind} just below {high}");
            Assert.False(_layout.Accepts(kind, low - 0.01f), $"{kind} just below the band {low}");
            Assert.False(_layout.Accepts(kind, high + 0.01f), $"{kind} just above the band {high}");
        }
    }

    [Fact]
    public void Density_WithinTolerancePerKind()
    {
        // 317 x 317 = 100489 cells of one face. All kinds of a slot share one roll on fixed cumulative intervals, so a kind holds at most its
        // density; rocks have no patch noise and hit the density within 3 sigma, the others are cut by their noise but keep 5 % at least.
        const int edge = 317;
        var counts = new Dictionary<PropKindV1, int>();
        for (var u = 0; u < edge; u++)
        {
            for (var v = 0; v < edge; v++)
            {
                for (var slot = 0; slot < 2; slot++)
                {
                    if (_layout.TryGetCandidate(42, PropId.Create(0, u, v, slot), out var candidate))
                    {
                        counts[candidate.Kind] = counts.GetValueOrDefault(candidate.Kind) + 1;
                    }
                }
            }
        }

        const double cells = edge * edge;
        foreach (var (kind, _, density, _, _) in _kinds)
        {
            var sigma = Math.Sqrt(cells * density * (1 - density));
            var count = counts.GetValueOrDefault(kind);
            if (kind == PropKindV1.Rock)
            {
                Assert.InRange(count, (cells * density) - (3 * sigma), (cells * density) + (3 * sigma));
            }
            else
            {
                // Patch noise cuts the density but never removes a kind: between 5 % of the full count and the full density plus 3 sigma.
                Assert.InRange(count, 0.05 * cells * density, (cells * density) + (3 * sigma));
            }
        }
    }

    [Fact]
    public void Trees_ClusterInPatches()
    {
        // 40 x 40 regions of 8 x 8 cells. Patch noise makes whole regions rich or empty in pines (variance well above the mean); rocks have no
        // patches and scatter like a binomial draw (variance below the mean).
        const int regionsPerEdge = 40;
        var pines = new List<double>();
        var rocks = new List<double>();
        for (var regionU = 0; regionU < regionsPerEdge; regionU++)
        {
            for (var regionV = 0; regionV < regionsPerEdge; regionV++)
            {
                var pine = 0;
                var rock = 0;
                for (var u = 0; u < 8; u++)
                {
                    for (var v = 0; v < 8; v++)
                    {
                        var cellU = 100 + (regionU * 8) + u;
                        var cellV = 200 + (regionV * 8) + v;
                        pine += _layout.TryGetCandidate(42, PropId.Create(2, cellU, cellV, 0), out var tree) && tree.Kind == PropKindV1.Pine ? 1 : 0;
                        rock += _layout.TryGetCandidate(42, PropId.Create(2, cellU, cellV, 1), out var stone) && stone.Kind == PropKindV1.Rock ? 1 : 0;
                    }
                }

                pines.Add(pine);
                rocks.Add(rock);
            }
        }

        Assert.True(Dispersion(pines) >= 1.5, $"pine variance/mean {Dispersion(pines)}");
        Assert.True(Dispersion(rocks) < 1.3, $"rock variance/mean {Dispersion(rocks)}");
    }

    private static double Dispersion(List<double> counts)
    {
        var mean = counts.Average();
        var variance = counts.Sum(c => (c - mean) * (c - mean)) / counts.Count;
        return variance / mean;
    }

    [Fact]
    public void NoTranscendentalInDecision()
    {
        // The decision is a function of the identifier alone. A direction nudged inside its cell finds the same cell and so the same candidate;
        // the layout never sees a direction, so floating-point differences between machines cannot change what exists.
        const int cells = 784;
        var random = new Random(5);
        var compared = 0;
        for (var i = 0; i < 3000; i++)
        {
            var face = random.Next(6);
            var u = 1 + random.Next(cells - 2);
            var v = 1 + random.Next(cells - 2);
            var centre = CubeSphereGrid.DirectionOfCell(face, u, v, cells);
            var nudged = Vector3.Normalize(centre + new Vector3(1e-5f, -1e-5f, 1e-5f));

            CubeSphereGrid.CellOfDirection(nudged, cells, out var f, out var cu, out var cv);

            Assert.Equal((face, u, v), (f, cu, cv));
            var exact = _layout.TryGetCandidate(42, PropId.Create(face, u, v, 0), out var a);
            var viaNudge = _layout.TryGetCandidate(42, PropId.Create(f, cu, cv, 0), out var b);
            Assert.Equal(exact, viaNudge);
            Assert.Equal(a, b);
            compared += exact ? 1 : 0;
        }

        Assert.True(compared > 300, $"{compared} candidates compared");
    }

    [Fact]
    public void Layouts_CoreProfile_ReturnsV1_UnknownProfileNone()
    {
        Assert.True(PropLayouts.TryGet("core:planet/1", out var layout));
        Assert.IsType<PropLayoutV1>(layout);
        Assert.False(PropLayouts.TryGet("core:planet/2", out var unknown));
        Assert.Null(unknown);
        Assert.False(PropLayouts.TryGet("core:test/1", out _));
    }

    [Fact]
    public void PropLayoutV1_GoldenDigest()
    {
        // Seeds 0, 42, 1234 and ulong.MaxValue (overflow of the seed and of the noise lattice), four regions on each face (0,0), (3,5), (97,97)
        // at the far corner of a 784-cell face and (50,12), all slots of the 8 x 8 cells.
        Assert.Equal(LayoutV1Digest, Digest());
    }

    private static ulong Digest()
    {
        var hash = new XxHash64();
        foreach (var seed in new ulong[] { 0, 42, 1234, ulong.MaxValue })
        {
            for (var face = 0; face < 6; face++)
            {
                foreach (var (regionU, regionV) in new[] { (0, 0), (3, 5), (97, 97), (50, 12) })
                {
                    for (var u = 0; u < 8; u++)
                    {
                        for (var v = 0; v < 8; v++)
                        {
                            for (var slot = 0; slot < 4; slot++)
                            {
                                AppendCandidate(hash, seed, PropId.Create(face, (regionU * 8) + u, (regionV * 8) + v, slot));
                            }
                        }
                    }
                }
            }
        }

        return hash.GetCurrentHashAsUInt64();
    }

    private static void AppendCandidate(XxHash64 hash, ulong seed, PropId id)
    {
        Span<byte> buffer = stackalloc byte[17];
        var found = _layout.TryGetCandidate(seed, id, out var candidate);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, id.Value);
        buffer[8] = found ? (byte)candidate.Kind : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[9..], found ? candidate.OffsetU : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[11..], found ? candidate.OffsetV : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[13..], found ? candidate.Rotation : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[15..], found ? candidate.Scale : (ushort)0);
        hash.Append(buffer);
    }
}
