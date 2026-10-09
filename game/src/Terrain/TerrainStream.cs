using System.Diagnostics.CodeAnalysis;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Terrain;

/// <summary>
/// The voxel stream of one planet's <see cref="VoxelLodTerrain"/>. Called by Voxel Tools on its worker threads: LOD 0 blocks come
/// from <see cref="ITerrainBlockSource"/> (edited blocks; others go to the generator), LOD &gt; 0 blocks from the session's
/// <see cref="DerivedLodCache"/>. LOD 0 saves are ignored (the source already holds the same or newer data); LOD &gt; 0 saves go to
/// the cache. Never touches the scene tree or world state. After <see cref="Close"/> every call returns an error without reading.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "A Voxel Tools stream; the name is the K2-09 contract.")]
public sealed partial class TerrainStream : VoxelStreamScript
{
    private readonly PlanetId _planet;
    private readonly ITerrainBlockSource? _source;
    private readonly TerrainBlockCodec? _codec;
    private readonly DerivedLodCache? _lodCache;
    private readonly ILogger? _logger;
#if DEBUG
    private readonly ThreadLocal<VoxelBuffer> _verifyBuffer = new(static () => new VoxelBuffer());
#endif
    private readonly Lock _errorLock = new();
    private TerrainBlockKey? _firstError;
    private volatile bool _closed;
    private int _errorCount;
    private int _sourceReads;
    private int _saveMismatches;

    /// <summary>Creates the stream of a planet.</summary>
    /// <param name="planet">Planet of the terrain.</param>
    /// <param name="source">Saved and pending LOD 0 blocks.</param>
    /// <param name="codec">Block codec.</param>
    /// <param name="lodCache">Derived LOD &gt; 0 blocks of this session.</param>
    /// <param name="logger">Logger (thread-safe).</param>
    public TerrainStream(PlanetId planet, ITerrainBlockSource source, TerrainBlockCodec codec, DerivedLodCache lodCache, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(lodCache);
        ArgumentNullException.ThrowIfNull(logger);
        _planet = planet;
        _source = source;
        _codec = codec;
        _lodCache = lodCache;
        _logger = logger;
    }

    /// <summary>Godot needs a parameterless constructor; such a stream is closed and answers every call with an error.</summary>
    public TerrainStream() => _closed = true;

    /// <summary>Planet of the terrain.</summary>
    public PlanetId Planet => _planet;

    /// <summary>True after <see cref="Close"/>.</summary>
    public bool IsClosed => _closed;

    /// <summary>Number of load errors reported to Voxel Tools (unavailable or undecodable blocks; not calls after closing).</summary>
    public int ErrorCount => Volatile.Read(ref _errorCount);

    /// <summary>Number of LOD 0 reads of the block source (tests).</summary>
    internal int SourceReads => Volatile.Read(ref _sourceReads);

    /// <summary>LOD 0 saves whose data differed from the block source (checked only in debug builds; tests).</summary>
    internal int SaveMismatches => Volatile.Read(ref _saveMismatches);

    /// <summary>First block whose load failed; null while none did. Any thread.</summary>
    public TerrainBlockKey? FirstError
    {
        get
        {
            lock (_errorLock)
            {
                return _firstError;
            }
        }
    }

    /// <summary>Stops reading: from now on every call returns an error without touching the source.</summary>
    public void Close() => _closed = true;

    /// <inheritdoc />
    public override int _GetUsedChannelsMask() => TerrainChannels.UsedChannelsMask;

    /// <inheritdoc />
    public override int _LoadVoxelBlock(VoxelBuffer outBuffer, Vector3I positionInBlocks, int lod)
    {
        if (_closed || _source is null || _codec is null || _lodCache is null || _logger is null || lod < 0 || lod > byte.MaxValue)
        {
            return (int)VoxelStream.ResultCode.Error;
        }

        var key = new TerrainBlockKey(_planet, (byte)lod, positionInBlocks.X, positionInBlocks.Y, positionInBlocks.Z);
        try
        {
            if (lod > 0)
            {
                if (!_lodCache.TryGet(key, out var voxels))
                {
                    return (int)VoxelStream.ResultCode.BlockNotFound;
                }

                VoxelBlockSerializer.DeserializeFromByteArray(voxels, outBuffer, true);
                return (int)VoxelStream.ResultCode.BlockFound;
            }

            Interlocked.Increment(ref _sourceReads);
            switch (_source.TryLoad(key, out _, out var blob))
            {
                case TerrainBlockLookup.NotEdited:
                    return (int)VoxelStream.ResultCode.BlockNotFound;
                case TerrainBlockLookup.Found:
                    _codec.Decode(blob, outBuffer);
                    return (int)VoxelStream.ResultCode.BlockFound;
                default:
                    if (!_closed)
                    {
                        TerrainGameLog.BlockUnavailable(_logger, key);
                        ReportError(key);
                    }

                    return (int)VoxelStream.ResultCode.Error;
            }
        }
        catch (ProtocolViolationException exception)
        {
            TerrainGameLog.BlockCorrupt(_logger, exception, key);
            ReportError(key);
            return (int)VoxelStream.ResultCode.Error;
        }
#pragma warning disable CA1031 // An exception must not cross into Voxel Tools' worker thread; it is logged and becomes a load error.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TerrainGameLog.StreamCallFailed(_logger, exception, key);
            ReportError(key);
            return (int)VoxelStream.ResultCode.Error;
        }
    }

    /// <inheritdoc />
    public override void _SaveVoxelBlock(VoxelBuffer buffer, Vector3I positionInBlocks, int lod)
    {
        if (_closed || _source is null || _codec is null || _lodCache is null || _logger is null || lod < 0 || lod > byte.MaxValue)
        {
            return;
        }

        var key = new TerrainBlockKey(_planet, (byte)lod, positionInBlocks.X, positionInBlocks.Y, positionInBlocks.Z);
        try
        {
            if (lod > 0)
            {
                _lodCache.Put(key, VoxelBlockSerializer.SerializeToByteArray(buffer, TerrainEditSemantics.BlobCompression));
                return;
            }

#if DEBUG
            VerifyLod0Save(buffer, key);
#endif
        }
#pragma warning disable CA1031 // An exception must not cross into Voxel Tools' worker thread; it is logged and the save is dropped.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            TerrainGameLog.StreamCallFailed(_logger, exception, key);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
#if DEBUG
        if (disposing)
        {
            _verifyBuffer.Dispose();
        }
#endif
        base.Dispose(disposing);
    }

#if DEBUG
    private void VerifyLod0Save(VoxelBuffer saved, TerrainBlockKey key)
    {
        // Debug builds only: the data Voxel Tools saves at unload must equal the pair the buffer serves (plan K2-09, "zapis LOD 0").
        if (_source!.TryLoad(key, out _, out var blob) != TerrainBlockLookup.Found)
        {
            return;
        }

        var expected = _verifyBuffer.Value!;
        _codec!.Decode(blob, expected);
        if (_codec.Digest(expected) != _codec.Digest(saved))
        {
            Interlocked.Increment(ref _saveMismatches);
            TerrainGameLog.SavedBlockDiffers(_logger!, key);
        }
    }
#endif

    private void ReportError(TerrainBlockKey key)
    {
        Interlocked.Increment(ref _errorCount);
        lock (_errorLock)
        {
            _firstError ??= key;
        }
    }
}
