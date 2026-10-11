using System.Numerics;

using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships;

/// <summary>
/// Computes <see cref="ShipStats"/> of a construct. Pure: reads the blocks and the content table only. Allocates the result and
/// is called on events (mode change, block change of a ship), not every tick.
/// </summary>
public static class ShipStatsCalculator
{
    private static readonly Comparison<ShipSeat> _seatOrder = static (a, b) =>
    {
        var x = a.Cell.X.CompareTo(b.Cell.X);
        if (x != 0)
        {
            return x;
        }

        var y = a.Cell.Y.CompareTo(b.Cell.Y);
        return y != 0 ? y : a.Cell.Z.CompareTo(b.Cell.Z);
    };

    /// <summary>
    /// Thrust convention (README P1): an engine has its nozzle on its <c>Forward</c> face (the image of block-local −Z) and the
    /// force acts opposite, so an engine with Forward = −Y pushes the ship toward +Y.
    /// </summary>
    /// <param name="engineOrientation">Orientation of the engine block.</param>
    /// <returns>The construct face the engine pushes toward.</returns>
    public static Face ThrustFace(byte engineOrientation) => (Face)((byte)BlockOrientation.Forward(engineOrientation) ^ 1);

    /// <summary>Computes the stats.</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="content">Block definitions by type id.</param>
    /// <returns>The stats; zero mass and centre for an empty construct.</returns>
    public static ShipStats Compute(Construct construct, BlockContentTable content)
    {
        ArgumentNullException.ThrowIfNull(construct);
        ArgumentNullException.ThrowIfNull(content);

        double mass = 0, mx = 0, my = 0, mz = 0;
        double sxx = 0, syy = 0, szz = 0, sxy = 0, sxz = 0, syz = 0;
        double ownInertia = 0;
        Span<double> thrust = stackalloc double[6];
        double torque = 0, fuel = 0;
        int blocks = 0, cockpits = 0, engines = 0, stabilizers = 0, tanks = 0, pulses = 0, gears = 0;
        var seats = new List<ShipSeat>();
        var hasCockpit = false;
        var cockpitCell = default(CellPos);
        byte cockpitOrientation = 0;
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

        foreach (var (chunkPos, chunk) in construct.Chunks)
        {
            var raw = chunk.Blocks;
            for (var i = 0; i < raw.Length; i++)
            {
                var block = new BlockState(raw[i]);
                if (block.IsEmpty)
                {
                    continue;
                }

                var cell = CellPos.FromChunk(chunkPos, i);
                var def = content.Get(block.TypeId);
                double m = def.MassKg;
                double cx = cell.X + 0.5, cy = cell.Y + 0.5, cz = cell.Z + 0.5;
                blocks++;
                minX = Math.Min(minX, cell.X);
                minY = Math.Min(minY, cell.Y);
                minZ = Math.Min(minZ, cell.Z);
                maxX = Math.Max(maxX, cell.X + 1);
                maxY = Math.Max(maxY, cell.Y + 1);
                maxZ = Math.Max(maxZ, cell.Z + 1);
                mass += m;
                mx += m * cx;
                my += m * cy;
                mz += m * cz;
                sxx += m * cx * cx;
                syy += m * cy * cy;
                szz += m * cz * cz;
                sxy += m * cx * cy;
                sxz += m * cx * cz;
                syz += m * cy * cz;
                ownInertia += m / 6.0;

                var function = def.Function;
                switch (function.Kind)
                {
                    case BlockFunctionKind.Cockpit:
                        cockpits++;
                        seats.Add(new ShipSeat(cell, SeatRole.Pilot));
                        if (!hasCockpit || CompareCells(cell, cockpitCell) < 0)
                        {
                            hasCockpit = true;
                            cockpitCell = cell;
                            cockpitOrientation = block.Orientation;
                        }

                        break;
                    case BlockFunctionKind.Seat:
                        seats.Add(new ShipSeat(cell, function.Seat));
                        break;
                    case BlockFunctionKind.Engine:
                        engines++;
                        thrust[(byte)ThrustFace(block.Orientation)] += function.ThrustNewtons;
                        break;
                    case BlockFunctionKind.Stabilizer:
                        stabilizers++;
                        torque += function.TorqueNewtonMeters;
                        break;
                    case BlockFunctionKind.FuelTank:
                        tanks++;
                        fuel += function.FuelCapacity;
                        break;
                    case BlockFunctionKind.PulseDrive:
                        pulses++;
                        break;
                    case BlockFunctionKind.LandingGear:
                        gears++;
                        break;
                    default:
                        break;
                }
            }
        }

        seats.Sort(_seatOrder);
        var controlFrame = hasCockpit ? BlockOrientation.ToQuaternion(cockpitOrientation) : Quaternion.Identity;
        var thrustResult = new ShipThrust(
            (float)thrust[(byte)Face.PosX], (float)thrust[(byte)Face.NegX], (float)thrust[(byte)Face.PosY],
            (float)thrust[(byte)Face.NegY], (float)thrust[(byte)Face.PosZ], (float)thrust[(byte)Face.NegZ]);
        if (mass <= 0)
        {
            return new ShipStats(
                0f, Vector3.Zero, default, thrustResult, (float)torque, (float)fuel, blocks, cockpits, engines, stabilizers, tanks, pulses, gears, seats)
            {
                ControlFrame = controlFrame,
                ControlCell = hasCockpit ? cockpitCell : null,
            };
        }

        // Tensor about the origin of construct space, then shifted to the centre of mass (parallel axis theorem). Every block
        // is a 1 m cube: I = m/6 about its own axes.
        double comX = mx / mass, comY = my / mass, comZ = mz / mass;
        double ixx = ownInertia + (syy + szz) - (mass * ((comY * comY) + (comZ * comZ)));
        double iyy = ownInertia + (sxx + szz) - (mass * ((comX * comX) + (comZ * comZ)));
        double izz = ownInertia + (sxx + syy) - (mass * ((comX * comX) + (comY * comY)));
        double ixy = -(sxy - (mass * comX * comY));
        double ixz = -(sxz - (mass * comX * comZ));
        double iyz = -(syz - (mass * comY * comZ));

        return new ShipStats(
            (float)mass, new Vector3((float)comX, (float)comY, (float)comZ),
            new ShipInertia((float)ixx, (float)iyy, (float)izz, (float)ixy, (float)ixz, (float)iyz),
            thrustResult, (float)torque, (float)fuel, blocks, cockpits, engines, stabilizers, tanks, pulses, gears, seats)
        {
            ControlFrame = controlFrame,
            ControlCell = hasCockpit ? cockpitCell : null,
            BoundingRadius = BoundingRadius(comX, comY, comZ, minX, minY, minZ, maxX, maxY, maxZ),
        };
    }

    private static int CompareCells(CellPos a, CellPos b)
    {
        var x = a.X.CompareTo(b.X);
        if (x != 0)
        {
            return x;
        }

        var y = a.Y.CompareTo(b.Y);
        return y != 0 ? y : a.Z.CompareTo(b.Z);
    }

    private static float BoundingRadius(double cx, double cy, double cz, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        var dx = Math.Max(cx - minX, maxX - cx);
        var dy = Math.Max(cy - minY, maxY - cy);
        var dz = Math.Max(cz - minZ, maxZ - cz);
        return (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
