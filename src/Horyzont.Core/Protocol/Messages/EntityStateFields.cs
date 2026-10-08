namespace Horyzont.Core.Protocol.Messages;

/// <summary>Fields present in an <see cref="EntityState"/>.</summary>
[Flags]
#pragma warning disable CA1711 // "Fields" is not a suffix with a special meaning here; the name comes from the plan.
public enum EntityStateFields : byte
#pragma warning restore CA1711
{
    /// <summary>No fields.</summary>
    None = 0,

    /// <summary>Position.</summary>
    Position = 1,

    /// <summary>Rotation.</summary>
    Rotation = 2,

    /// <summary>Velocity.</summary>
    Velocity = 4,

    /// <summary>Motion flags.</summary>
    MotionFlags = 8,
}
