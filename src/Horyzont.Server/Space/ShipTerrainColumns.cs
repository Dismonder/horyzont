using System.Numerics;

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Frames;

namespace Horyzont.Server.Space;

/// <summary>
/// The terrain column of a ship-mode construct in a planet frame (K5-22 decision 5, README Y11): bounding sphere of the blocks, speed of
/// its centre from <see cref="ServerFrameHistory"/>, altitude over the lowest possible ground <c>R − MaxRelief</c> (the server does not
/// sample the ground, so the column is conservative: it reaches the ground whenever the ship is within the look-ahead). The collision
/// viewer of the ship stands in this column. Pure.
/// </summary>
public static class ShipTerrainColumns
{
    /// <summary>Centre of the bounding sphere of the blocks in the construct's local space.</summary>
    /// <param name="ship">The construct.</param>
    /// <returns>The centre.</returns>
    public static Vector3 BoundingCenter(Construct ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        var min = ship.BoundsMin;
        var max = ship.BoundsMax;
        return new Vector3(min.X + max.X + 1, min.Y + max.Y + 1, min.Z + max.Z + 1) / 2f;
    }

    /// <summary>Radius of the bounding sphere of the blocks (half the diagonal of the cell bounds).</summary>
    /// <param name="ship">The construct.</param>
    /// <returns>The radius in metres.</returns>
    public static float BoundingRadius(Construct ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        var min = ship.BoundsMin;
        var max = ship.BoundsMax;
        return new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1).Length() / 2f;
    }

    /// <summary>Computes the column of a ship-mode construct whose parent is a planet.</summary>
    /// <param name="ship">The construct.</param>
    /// <param name="state">The world (planet radius).</param>
    /// <param name="history">The frame history (speed); a ship without history has speed 0.</param>
    /// <param name="tickSeconds">Seconds per tick.</param>
    /// <param name="maxReliefMeters">Highest relief of the generator over and under R.</param>
    /// <param name="planet">Receives the planet of the column.</param>
    /// <param name="column">Receives the column in the planet frame.</param>
    /// <returns>False for a construct that is not a ship or not in a planet frame.</returns>
    public static bool TryGet(
        Construct ship, WorldState state, ServerFrameHistory history, double tickSeconds, float maxReliefMeters, out PlanetId planet,
        out TerrainColumn column)
    {
        var found = TryGetQuery(ship, state, history, tickSeconds, maxReliefMeters, out planet, out var query);
        column = query.Column;
        return found;
    }

    /// <summary>Computes the terrain query (column and sweep box, README Y11) of a ship-mode construct whose parent is a planet.</summary>
    /// <param name="ship">The construct.</param>
    /// <param name="state">The world (planet radius).</param>
    /// <param name="history">The frame history (velocity); a ship without history has velocity 0.</param>
    /// <param name="tickSeconds">Seconds per tick.</param>
    /// <param name="maxReliefMeters">Highest relief of the generator over and under R.</param>
    /// <param name="planet">Receives the planet of the query.</param>
    /// <param name="query">Receives the query in the planet frame.</param>
    /// <returns>False for a construct that is not a ship or not in a planet frame.</returns>
    public static bool TryGetQuery(
        Construct ship, WorldState state, ServerFrameHistory history, double tickSeconds, float maxReliefMeters, out PlanetId planet,
        out ShipTerrainQuery query)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);
        planet = default;
        query = default;
        if (ship.Mode != ConstructMode.Ship || ship.Parent.Kind != FrameKind.Planet || !state.Planets.TryGet(new PlanetId(ship.Parent.Id), out var body))
        {
            return false;
        }

        var local = BoundingCenter(ship);
        var center = ship.LocalToParent(local);
        var v = history.PointVelocity(FrameRef.Of(ship.Id), Vector3D.FromFloat(local), tickSeconds);
        var velocity = new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
        {
            velocity = Vector3.Zero;
        }

        var lowest = body.RadiusMeters - maxReliefMeters;
        var altitude = MathF.Max(center.Length() - lowest, 0f);
        planet = body.Id;
        query = ShipTerrainQuery.Of(center, BoundingRadius(ship), velocity, altitude, body.RadiusMeters, maxReliefMeters);
        return true;
    }
}
