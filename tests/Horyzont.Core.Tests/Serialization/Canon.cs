using System.Numerics;

using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>Computes what a lossy field must look like after an encode/decode round trip.</summary>
public static class Canon
{
    /// <summary>A float after a trip through a 16-bit half.</summary>
    /// <param name="v">Original.</param>
    /// <returns>The value the decoder returns.</returns>
    public static float ViaHalf(float v) => (float)(Half)v;

    /// <summary>A vector after its components went through a 16-bit half.</summary>
    /// <param name="v">Original.</param>
    /// <returns>The value the decoder returns.</returns>
    public static Vector3 ViaHalf(Vector3 v) => new(ViaHalf(v.X), ViaHalf(v.Y), ViaHalf(v.Z));

    /// <summary>A movement axis after quantization.</summary>
    /// <param name="v">Original.</param>
    /// <returns>The value the decoder returns.</returns>
    public static float ViaAxis(float v) => Quantization.DequantizeAxis(Quantization.QuantizeAxis(v));

    /// <summary>The state with quantized velocity (rotation is compared with a tolerance instead).</summary>
    /// <param name="s">Original.</param>
    /// <returns>The expected decoded state.</returns>
    public static EntityState Expected(EntityState s) => s with { Velocity = ViaHalf(s.Velocity) };

    /// <summary>The message the decoder must return for <paramref name="message"/>, up to the rotation tolerance.</summary>
    /// <param name="message">Original.</param>
    /// <returns>The expected decoded message.</returns>
    public static Message Expected(Message message) => message switch
    {
        InputBatch b => b with { Inputs = b.Inputs.Select(i => i with { MoveX = ViaAxis(i.MoveX), MoveY = ViaAxis(i.MoveY) }).ToArray() },
        Snapshot s => s with { Entities = s.Entities.Select(Expected).ToArray() },
        EntitySpawn e => e with { Initial = Expected(e.Initial) },
        _ => message,
    };

    /// <summary>True for messages that contain a compressed rotation, so the byte-stability property does not apply.</summary>
    /// <param name="message">Message.</param>
    /// <returns>Whether the message carries an <see cref="EntityState"/>.</returns>
    public static bool HasCompressedRotation(Message message) => message is Snapshot or EntitySpawn;
}
