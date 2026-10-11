using System.Buffers.Binary;

using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>
/// <see cref="EntitySpawn.KindData"/> of a player (K5-26 decision 7): empty when the player does not sit, otherwise
/// <c>[1][x i32][y i32][z i32]</c> (little endian, 13 bytes), the seat cell in the construct frame of the spawn.
/// </summary>
public static class EntitySpawnSeat
{
    /// <summary>Length of an encoded seat.</summary>
    public const int Length = 13;

    /// <summary>Version byte of the encoding.</summary>
    public const byte Version = 1;

    /// <summary>Encodes a seat.</summary>
    /// <param name="seat">The seat cell; null when the player does not sit.</param>
    /// <returns>The kind data.</returns>
    public static byte[] Encode(CellPos? seat)
    {
        if (seat is not { } cell)
        {
            return [];
        }

        var data = new byte[Length];
        data[0] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1), cell.X);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(5), cell.Y);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(9), cell.Z);
        return data;
    }

    /// <summary>Decodes the kind data of a player spawn received from the network.</summary>
    /// <param name="data">The kind data.</param>
    /// <param name="frame">Frame of the spawn: a seat is valid only in a construct frame.</param>
    /// <returns>The seat; null for empty data.</returns>
    /// <exception cref="ProtocolViolationException">Another length or version, a seat outside a construct frame or out of bounds.</exception>
    public static CellPos? Decode(ReadOnlySpan<byte> data, FrameRef frame)
    {
        if (data.IsEmpty)
        {
            return null;
        }

        if (data.Length != Length || data[0] != Version)
        {
            throw new ProtocolViolationException("Player kind data is not a seat.");
        }

        var cell = new CellPos(
            BinaryPrimitives.ReadInt32LittleEndian(data[1..]), BinaryPrimitives.ReadInt32LittleEndian(data[5..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[9..]));
        if (frame.Kind != FrameKind.Construct || !cell.IsInBounds)
        {
            throw new ProtocolViolationException("Player seat outside a construct frame or out of bounds.");
        }

        return cell;
    }
}
