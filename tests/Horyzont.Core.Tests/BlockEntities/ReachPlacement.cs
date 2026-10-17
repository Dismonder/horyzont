using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>
/// Puts a player at an exact eye distance from the centre of a cell of the fixture construct (unrotated, origin 0, 4000, 0), measured
/// with the eye of the simulation, so the range tests can sit 1 cm either side of a boundary without going through the code under test.
/// </summary>
internal static class ReachPlacement
{
    private static readonly Vector3 _constructOrigin = new(0f, 4000f, 0f);

    /// <summary>Moves <paramref name="player"/> so that the eye is <paramref name="meters"/> (+-5 mm) from the centre of <paramref name="cell"/>.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="player">The player to move.</param>
    /// <param name="cell">Cell of the fixture construct.</param>
    /// <param name="meters">Wanted eye distance to the cell centre.</param>
    /// <returns>The distance reached.</returns>
    public static float MoveEyeToDistance(FunctionalBlockFixture fixture, PlayerId player, CellPos cell, float meters)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var centre = _constructOrigin + new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        var z = centre.Z + meters;
        var distance = 0f;
        for (var i = 0; i < 16; i++)
        {
            fixture.Move(player, new Vector3(centre.X, 4001f, z));
            distance = Vector3.Distance(fixture.Sim.Eye(player), centre);
            if (MathF.Abs(distance - meters) < 0.0005f)
            {
                break;
            }

            z += meters - distance;
        }

        Assert.InRange(distance, meters - 0.005f, meters + 0.005f);
        return distance;
    }
}
