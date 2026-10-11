using System.Numerics;

namespace Horyzont.Server.Space;

/// <summary>
/// The sweep box of a ship near the terrain (README Y11, Opus review of K5-22): when <c>r − hull − d &lt; R + MaxRelief + 64 m</c>
/// (<c>d</c> = <see cref="TerrainColumn.Depth"/>) the LOD 0 collision must also be built in a box of half-size <c>hull + 8 m</c> centred at
/// <c>ship + v̂ · min(2 s · |v|, 40 m)</c> — where the hull will be, whatever the generated ground under it (valley walls, player
/// edits). The ship gets a second collision viewer at <see cref="Center"/> unless its ground viewer is within
/// <see cref="SingleViewerMeters"/> of it. The viewers are refreshed every 10 ticks, so the horizontal speed in the band is capped at
/// <see cref="MaxHorizontalSpeed"/> (flight, K5-24). Pure.
/// </summary>
/// <param name="Center">Centre of the box in the planet frame.</param>
/// <param name="HalfSize">Half-size of the box (hull radius + <see cref="HullMarginMeters"/>); the readiness query radius.</param>
public readonly record struct TerrainSweep(Vector3 Center, float HalfSize)
{
    /// <summary>Seconds of look-ahead along the velocity.</summary>
    public const float LookaheadSeconds = 2f;

    /// <summary>Largest offset of the box centre ahead of the ship.</summary>
    public const float MaxOffsetMeters = 40f;

    /// <summary>Margin of the box over the hull radius.</summary>
    public const float HullMarginMeters = 8f;

    /// <summary>Height of the band over <c>R + MaxRelief</c> (the clear margin of the terrain).</summary>
    public const float BandMarginMeters = 64f;

    /// <summary>Distance around a viewer in which Voxel Tools builds LOD 0, the collision LOD (<c>PlanetTerrainFactory.LodDistance</c>).</summary>
    public const float LodRingMeters = 96f;

    /// <summary>Seconds between refreshes of the ship viewers (<c>ServerShipViewers.UpdateIntervalTicks</c> = 10 at 60 Hz).</summary>
    public const float RefreshSeconds = 10f / 60f;

    /// <summary>A ground viewer this close to the sweep centre covers the box: no second viewer (height over the ground ≤ 50 m).</summary>
    public const float SingleViewerMeters = 50f;

    /// <summary>
    /// Largest query radius the readiness checks as a whole box (covers a sweep box of a hull up to 48 m); a larger sphere is a column
    /// and is checked at its ground (<see cref="TerrainGround"/>).
    /// </summary>
    public const float FullBoxMaxRadius = LodRingMeters - MaxOffsetMeters;

    /// <summary>
    /// Speed cap across the band: in one refresh the ship must not leave the LOD 0 ring of the viewer at its sweep point,
    /// <c>(96 − 40 − hull) / (10 / 60 s)</c>; 0 for a hull of 56 m or more.
    /// </summary>
    /// <param name="shipRadius">Radius of the bounding sphere of the ship.</param>
    /// <returns>The cap in m/s.</returns>
    public static float MaxHorizontalSpeed(float shipRadius)
        => MathF.Max(LodRingMeters - MaxOffsetMeters - MathF.Max(shipRadius, 0f), 0f) / RefreshSeconds;

    /// <summary>Whether a ship is in the band where the sweep box applies: <c>r − hull − d &lt; R + MaxRelief + 64 m</c>.</summary>
    /// <param name="shipCenter">Centre of the bounding sphere of the ship in the planet frame.</param>
    /// <param name="shipRadius">Radius of the bounding sphere.</param>
    /// <param name="depth">The look-ahead depth of the column (<see cref="TerrainColumn.Depth"/>).</param>
    /// <param name="planetRadius">Surface radius R.</param>
    /// <param name="maxReliefMeters">Highest relief over R.</param>
    /// <returns>True in the band.</returns>
    public static bool InBand(Vector3 shipCenter, float shipRadius, float depth, float planetRadius, float maxReliefMeters)
        => shipCenter.Length() - MathF.Max(shipRadius, 0f) - MathF.Max(depth, 0f) < planetRadius + maxReliefMeters + BandMarginMeters;

    /// <summary>The sweep box of a ship (whether or not it is in the band).</summary>
    /// <param name="shipCenter">Centre of the bounding sphere of the ship in the planet frame.</param>
    /// <param name="shipRadius">Radius of the bounding sphere.</param>
    /// <param name="velocity">Velocity of the ship in the planet frame (m/s).</param>
    /// <returns>The box.</returns>
    public static TerrainSweep Ahead(Vector3 shipCenter, float shipRadius, Vector3 velocity)
    {
        var speed = velocity.Length();
        var offset = float.IsFinite(speed) && speed > 1e-4f ? velocity / speed * MathF.Min(LookaheadSeconds * speed, MaxOffsetMeters) : Vector3.Zero;
        return new TerrainSweep(shipCenter + offset, MathF.Max(shipRadius, 0f) + HullMarginMeters);
    }
}
