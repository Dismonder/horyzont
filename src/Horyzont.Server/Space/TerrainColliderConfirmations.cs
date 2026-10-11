using System.Numerics;

namespace Horyzont.Server.Space;

/// <summary>The LOD 0 collider of one terrain mesh block as the terrain reports it on the main thread.</summary>
public enum TerrainBlockCollider : byte
{
    /// <summary>The block has no surface (all air or all solid): there is nothing to collide with, so nothing to wait for.</summary>
    Absent = 0,

    /// <summary>The block has a surface, but its collider is not in the physics space yet.</summary>
    Disabled = 1,

    /// <summary>The collider of the block is in the physics space.</summary>
    Active = 2,
}

/// <summary>Reads the collider state of a LOD 0 mesh block (the game implements it over Voxel Tools). Main thread.</summary>
public interface ITerrainColliderProbe
{
    /// <summary>The collider state of the LOD 0 mesh block at a block position.</summary>
    /// <param name="x">Block X.</param>
    /// <param name="y">Block Y.</param>
    /// <param name="z">Block Z.</param>
    /// <returns>The state.</returns>
    TerrainBlockCollider Probe(int x, int y, int z);
}

/// <summary>An inclusive box of mesh block positions; empty when a max is below its min.</summary>
/// <param name="MinX">Smallest block X.</param>
/// <param name="MinY">Smallest block Y.</param>
/// <param name="MinZ">Smallest block Z.</param>
/// <param name="MaxX">Largest block X.</param>
/// <param name="MaxY">Largest block Y.</param>
/// <param name="MaxZ">Largest block Z.</param>
public readonly record struct TerrainBlockBox(int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ)
{
    /// <summary>Gets a value indicating whether the box holds no block.</summary>
    public bool IsEmpty => MaxX < MinX || MaxY < MinY || MaxZ < MinZ;

    /// <summary>
    /// The mesh blocks of an area in voxels, exactly as <c>VoxelLodTerrain.is_area_meshed</c> takes them: <c>Box3i(position, size)</c>
    /// truncates toward zero, then <c>downscaled(blockSize)</c> floor-divides the min and <c>min + size − 1</c>.
    /// </summary>
    /// <param name="position">Min corner of the area in voxels.</param>
    /// <param name="size">Size of the area in voxels.</param>
    /// <param name="blockSize">Mesh block size in voxels (LOD 0).</param>
    /// <returns>The block box.</returns>
    public static TerrainBlockBox OfVoxelArea(Vector3 position, Vector3 size, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        var (minX, maxX) = Axis(position.X, size.X, blockSize);
        var (minY, maxY) = Axis(position.Y, size.Y, blockSize);
        var (minZ, maxZ) = Axis(position.Z, size.Z, blockSize);
        return new TerrainBlockBox(minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static (int Min, int Max) Axis(float position, float size, int blockSize)
    {
        var p = (int)position;
        var s = (int)size;
        return (FloorDiv(p, blockSize), FloorDiv(p + s - 1, blockSize));
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return (value % divisor != 0) && ((value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
    }
}

/// <summary>
/// Whether every LOD 0 collider in a box is in the physics space (README Y11 readiness, fix of the K5-22 descent race). Voxel Tools
/// marks a LOD 0 mesh block up to date (<c>is_area_meshed</c>) in the main-thread call that builds its collider, but builds it
/// <em>disabled</em> unless the clipbox streaming already activated it; it activates a LOD 0 collider only once all 8 blocks under its
/// LOD 1 parent loaded, one update task later. So "meshed" can hold for frames before the collider exists in the physics space.
/// <para>
/// Probing a block (<see cref="ITerrainColliderProbe"/>) is costly (Voxel Tools answers with a dictionary), so a block found with an
/// active collider (or without surface) is remembered while queries keep seeing it: a block confirmed or seen in process frame
/// <c>f − 1</c> or <c>f</c> counts as confirmed in <c>f</c>. A block not seen for a whole frame is probed again (it may have been
/// unloaded and loaded again meanwhile, with a new disabled collider). A Voxel Tools LOD 0 collider, once active, is disabled only by
/// unloading its block. The caller checks <c>is_area_meshed</c> first. Allocation-free once the sets have grown; one thread.
/// </para>
/// </summary>
public sealed class TerrainColliderConfirmations
{
    private HashSet<(int X, int Y, int Z)> _current = [];
    private HashSet<(int X, int Y, int Z)> _previous = [];
    private ulong _frame;
    private bool _started;

    /// <summary>Gets the number of blocks probed so far.</summary>
    public long Probes { get; private set; }

    /// <summary>Whether every block of a box has an active collider or no surface.</summary>
    /// <typeparam name="TProbe">The probe (a struct to stay allocation-free).</typeparam>
    /// <param name="box">The blocks.</param>
    /// <param name="frame">The current process frame (the terrain changes only between frames).</param>
    /// <param name="probe">Reads a block.</param>
    /// <returns>True when all colliders are active; false at the first block whose collider is not.</returns>
    public bool AllActive<TProbe>(in TerrainBlockBox box, ulong frame, ref TProbe probe)
        where TProbe : ITerrainColliderProbe
    {
        Advance(frame);
        for (var x = box.MinX; x <= box.MaxX; x++)
        {
            for (var y = box.MinY; y <= box.MaxY; y++)
            {
                for (var z = box.MinZ; z <= box.MaxZ; z++)
                {
                    var key = (x, y, z);
                    if (_current.Contains(key))
                    {
                        continue;
                    }

                    if (!_previous.Contains(key))
                    {
                        Probes++;
                        if (probe.Probe(x, y, z) == TerrainBlockCollider.Disabled)
                        {
                            return false;
                        }
                    }

                    _current.Add(key);
                }
            }
        }

        return true;
    }

    /// <summary>Forgets every confirmation.</summary>
    public void Clear()
    {
        _current.Clear();
        _previous.Clear();
    }

    private void Advance(ulong frame)
    {
        if (_started && frame == _frame)
        {
            return;
        }

        if (_started && frame == _frame + 1)
        {
            (_previous, _current) = (_current, _previous);
            _current.Clear();
        }
        else
        {
            Clear();
        }

        _frame = frame;
        _started = true;
    }
}
