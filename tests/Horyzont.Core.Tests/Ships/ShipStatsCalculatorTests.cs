using System.Numerics;

using Horyzont.Core.Ships;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships;

public sealed class ShipStatsCalculatorTests
{
    private static (WorldState State, Construct Construct, BlockContentTable Content) Build(IReadOnlyList<ShipBlock> blocks)
    {
        var state = ShipFixtures.CreateWorld(blocks);
        Assert.True(state.Constructs.TryGet(ShipFixtures.ConstructId, out var construct));
        return (state, construct, ShipFixtures.ContentFor(state));
    }

    [Fact]
    public void ShipStats_ThirtyBlocks_MatchHandComputedFromContent()
    {
        var (_, construct, content) = Build(ShipFixtures.Thirty);

        var stats = ShipStatsCalculator.Compute(construct, content);

        // Masses from blocks.json: cockpit 400, 4 engines 4 x 600, 2 stabilizers 2 x 300, 2 tanks 2 x 250, 2 seats 2 x 80,
        // navigator 250, 4 landing gear 4 x 150, pulse drive 800, cargo 200, 9 hull 9 x 200, 2 windows 2 x 150, door 200.
        const float expectedMass = 400 + 2400 + 600 + 500 + 160 + 250 + 600 + 800 + 200 + 1800 + 300 + 200;
        Assert.Equal(8210f, expectedMass);
        Assert.Equal(expectedMass, stats.MassKg);
        Assert.Equal(30, stats.BlockCount);

        // Two engines with the nozzle down push up; the rear nozzle pushes toward −Z; the front nozzle toward +Z.
        Assert.Equal(new ShipThrust(0f, 0f, 120000f, 0f, 60000f, 60000f), stats.Thrust);
        Assert.Equal(80000f, stats.TorqueNewtonMeters);
        Assert.Equal(2000f, stats.FuelCapacity);
        Assert.Equal(1, stats.Cockpits);
        Assert.Equal(4, stats.Engines);
        Assert.Equal(2, stats.Stabilizers);
        Assert.Equal(2, stats.FuelTanks);
        Assert.Equal(1, stats.PulseDrives);
        Assert.Equal(4, stats.LandingGears);

        // Seats sorted by cell: x, then y, then z. The cockpit is the pilot seat.
        Assert.Equal(
            [
                new ShipSeat(new CellPos(1, 2, 1), SeatRole.Passenger),
                new ShipSeat(new CellPos(2, 2, 0), SeatRole.Pilot),
                new ShipSeat(new CellPos(2, 2, 1), SeatRole.Navigator),
                new ShipSeat(new CellPos(3, 2, 1), SeatRole.Passenger),
            ],
            stats.Seats);

        // Centre of mass and tensor by an independent brute-force sum over the layout.
        double mass = 0, cx = 0, cy = 0, cz = 0;
        foreach (var block in ShipFixtures.Thirty)
        {
            double m = ShipFixtures.Def(block.Key).MassKg;
            mass += m;
            cx += m * (block.Cell.X + 0.5);
            cy += m * (block.Cell.Y + 0.5);
            cz += m * (block.Cell.Z + 0.5);
        }

        cx /= mass;
        cy /= mass;
        cz /= mass;
        Assert.Equal((float)cx, stats.CenterOfMass.X, 1e-3f);
        Assert.Equal((float)cy, stats.CenterOfMass.Y, 1e-3f);
        Assert.Equal((float)cz, stats.CenterOfMass.Z, 1e-3f);

        double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
        foreach (var block in ShipFixtures.Thirty)
        {
            double m = ShipFixtures.Def(block.Key).MassKg;
            double dx = block.Cell.X + 0.5 - cx, dy = block.Cell.Y + 0.5 - cy, dz = block.Cell.Z + 0.5 - cz;
            xx += (m / 6) + (m * ((dy * dy) + (dz * dz)));
            yy += (m / 6) + (m * ((dx * dx) + (dz * dz)));
            zz += (m / 6) + (m * ((dx * dx) + (dy * dy)));
            xy -= m * dx * dy;
            xz -= m * dx * dz;
            yz -= m * dy * dz;
        }

        Assert.Equal((float)xx, stats.Inertia.Xx, (float)xx * 1e-4f);
        Assert.Equal((float)yy, stats.Inertia.Yy, (float)yy * 1e-4f);
        Assert.Equal((float)zz, stats.Inertia.Zz, (float)zz * 1e-4f);
        Assert.Equal((float)xy, stats.Inertia.Xy, 1f);
        Assert.Equal((float)xz, stats.Inertia.Xz, 1f);
        Assert.Equal((float)yz, stats.Inertia.Yz, 1f);
    }

    [Fact]
    public void ShipStats_UnknownBlockType_CountsAsMissing()
    {
        var (_, construct, _) = Build([new ShipBlock(ShipFixtures.Engine, new CellPos(0, 0, 0))]);

        var stats = ShipStatsCalculator.Compute(construct, BlockContentTable.Empty);

        Assert.Equal(BlockContentTable.MissingMassKg, stats.MassKg);
        Assert.Equal(0, stats.Engines);
        Assert.Equal(default, stats.Thrust);
    }

    [Theory]
    [MemberData(nameof(AllOrientations))]
    public void ShipStats_EngineOrientations_ThrustOppositeToForward(byte orientation)
    {
        var (_, construct, content) = Build([new ShipBlock(ShipFixtures.Engine, new CellPos(3, -2, 5), orientation)]);

        var stats = ShipStatsCalculator.Compute(construct, content);

        // The nozzle is on the Forward face (the image of block-local −Z); the force acts the other way.
        var forward = BlockOrientation.Forward(orientation);
        var expected = forward switch
        {
            Face.PosX => Face.NegX,
            Face.NegX => Face.PosX,
            Face.PosY => Face.NegY,
            Face.NegY => Face.PosY,
            Face.PosZ => Face.NegZ,
            _ => Face.PosZ,
        };
        Assert.Equal(expected, ShipStatsCalculator.ThrustFace(orientation));
        Assert.Equal(expected, BlockOrientation.ToConstruct(orientation, Face.PosZ));
        foreach (var face in Enum.GetValues<Face>())
        {
            Assert.Equal(face == expected ? 60000f : 0f, stats.Thrust[face]);
        }
    }

    public static TheoryData<byte> AllOrientations()
    {
        var data = new TheoryData<byte>();
        for (byte i = 0; i < BlockOrientation.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void ShipStats_Inertia_SingleBlockAndParallelAxis()
    {
        var (_, one, content) = Build([new ShipBlock(ShipFixtures.Hull, new CellPos(7, -3, 2))]);
        var single = ShipStatsCalculator.Compute(one, content);

        // A 1 m cube of 200 kg: I = m / 6 about every axis, no products; the centre is the cell centre.
        Assert.Equal(new Vector3(7.5f, -2.5f, 2.5f), single.CenterOfMass);
        Assert.Equal(new ShipInertia(200f / 6f, 200f / 6f, 200f / 6f, 0f, 0f, 0f), single.Inertia);

        // Two cubes side by side along X: the centre between them (d = ±0.5 on X).
        var (_, two, _) = Build([new ShipBlock(ShipFixtures.Hull, new CellPos(0, 0, 0)), new ShipBlock(ShipFixtures.Hull, new CellPos(1, 0, 0))]);
        var pair = ShipStatsCalculator.Compute(two, content);

        Assert.Equal(new Vector3(1f, 0.5f, 0.5f), pair.CenterOfMass);
        Assert.Equal(2 * 200f / 6f, pair.Inertia.Xx, 1e-3f);
        Assert.Equal(2 * ((200f / 6f) + (200f * 0.25f)), pair.Inertia.Yy, 1e-3f);
        Assert.Equal(pair.Inertia.Yy, pair.Inertia.Zz, 1e-3f);
        Assert.Equal(0f, pair.Inertia.Xy, 1e-3f);

        // Two cubes on the diagonal of the XY plane: a negative product of inertia entry (Ixy = −Σ m dx dy).
        var (_, diagonal, _) = Build([new ShipBlock(ShipFixtures.Hull, new CellPos(0, 0, 0)), new ShipBlock(ShipFixtures.Hull, new CellPos(1, 1, 0))]);
        var skew = ShipStatsCalculator.Compute(diagonal, content);

        Assert.Equal(-(2 * 200f * 0.25f), skew.Inertia.Xy, 1e-3f);
    }

    [Fact]
    public void ShipFixtures_Thirty_DownThrustExceedsWeightOnPlanetC()
    {
        var (_, construct, content) = Build(ShipFixtures.Thirty);

        var stats = ShipStatsCalculator.Compute(construct, content);

        // Planet C has g = 12 m/s². The engines with the nozzle down must be able to lift the 30-block ship (tuned in data).
        Assert.True(stats.Thrust.PosY > stats.MassKg * 12f, $"lift {stats.Thrust.PosY} N vs weight {stats.MassKg * 12f} N");
    }
}
