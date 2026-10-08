using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>Voxel data of an edited terrain block.</summary>
/// <param name="Key">Block key.</param>
/// <param name="Version">Block version.</param>
/// <param name="Data">Voxel data, opaque to Core.</param>
#pragma warning disable CA1819 // Message records own their arrays (decoders copy them).
public sealed record TerrainBlockData(TerrainBlockKey Key, uint Version, byte[] Data) : Message
#pragma warning restore CA1819
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.TerrainBlockData;
}
