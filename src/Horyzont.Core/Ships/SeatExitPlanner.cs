using System.Numerics;

using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Ships;

/// <summary>
/// Where a player stands after leaving a seat (K5-25 decision 7), in the frame of the construct. Candidates in seat axes:
/// the cell in front of the seat, behind, left, right, above; the first whose cell and the cell above it (along the region up
/// rounded to a construct axis) are empty and whose feet are not in the ground. An ejection without a free cell goes above the
/// bounding box of the construct, 1 m along the region up, over the seat.
/// </summary>
public static class SeatExitPlanner
{
    /// <summary>Plans the exit feet.</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="seat">Seat cell.</param>
    /// <param name="orientation">Seat block orientation.</param>
    /// <param name="regionUp">Up of the region (planet) at the seat, in construct space.</param>
    /// <param name="isGround">True when a construct-space point lies in the ground or the ground is unknown there.</param>
    /// <param name="eject">An ejection: falls back to the point above the construct.</param>
    /// <param name="rules">Seat rules.</param>
    /// <param name="feet">Exit feet in construct space.</param>
    /// <returns>False when a plain exit finds no free cell.</returns>
    public static bool TryPlan(
        Construct construct, CellPos seat, byte orientation, Vector3 regionUp, Func<Vector3, bool> isGround, bool eject, SeatRules rules,
        out Vector3 feet)
    {
        ArgumentNullException.ThrowIfNull(construct);
        ArgumentNullException.ThrowIfNull(isGround);
        ArgumentNullException.ThrowIfNull(rules);
        var up = RoundToAxis(regionUp);
        var upCell = ToCell(up);
        var forward = ToCell(SeatPose.ForwardOf(orientation));
        var right = ToCell(SeatPose.Normal(BlockOrientation.Right(orientation)));
        Span<CellPos> offsets = [forward, Negate(forward), Negate(right), right, upCell];
        foreach (var offset in offsets)
        {
            var cell = new CellPos(seat.X + offset.X, seat.Y + offset.Y, seat.Z + offset.Z);
            var above = new CellPos(cell.X + upCell.X, cell.Y + upCell.Y, cell.Z + upCell.Z);
            if (!IsFree(construct, cell) || !IsFree(construct, above))
            {
                continue;
            }

            var candidate = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) - (up * 0.5f) + (up * rules.FeetLift);
            if (!isGround(candidate))
            {
                feet = candidate;
                return true;
            }
        }

        if (!eject)
        {
            feet = default;
            return false;
        }

        var seatCentre = new Vector3(seat.X + 0.5f, seat.Y + 0.5f, seat.Z + 0.5f);
        var min = construct.BoundsMin;
        var max = construct.BoundsMax;
        var top = float.MinValue;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? min.X : max.X + 1, (i & 2) == 0 ? min.Y : max.Y + 1, (i & 4) == 0 ? min.Z : max.Z + 1);
            top = MathF.Max(top, Vector3.Dot(corner - seatCentre, up));
        }

        feet = seatCentre + (up * (top + 1f));
        return true;
    }

    private static bool IsFree(Construct construct, CellPos cell) => cell.IsInBounds && construct.GetBlock(cell).IsEmpty;

    private static Vector3 RoundToAxis(Vector3 v)
    {
        var ax = MathF.Abs(v.X);
        var ay = MathF.Abs(v.Y);
        var az = MathF.Abs(v.Z);
        if (ax >= ay && ax >= az)
        {
            return new Vector3(MathF.Sign(v.X) >= 0 ? 1f : -1f, 0f, 0f);
        }

        return ay >= az ? new Vector3(0f, MathF.Sign(v.Y) >= 0 ? 1f : -1f, 0f) : new Vector3(0f, 0f, MathF.Sign(v.Z) >= 0 ? 1f : -1f);
    }

    private static CellPos ToCell(Vector3 axis) => new((int)MathF.Round(axis.X), (int)MathF.Round(axis.Y), (int)MathF.Round(axis.Z));

    private static CellPos Negate(CellPos c) => new(-c.X, -c.Y, -c.Z);
}
