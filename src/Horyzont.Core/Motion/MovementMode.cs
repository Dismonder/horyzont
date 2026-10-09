namespace Horyzont.Core.Motion;

/// <summary>Movement mode of a character. The byte value is carried in <c>EntityState.MotionFlags</c>.</summary>
public enum MovementMode : byte
{
    /// <summary>The body does not move (after spawn until terrain collision exists, and after a rescue).</summary>
    Frozen = 0,

    /// <summary>On the floor, at most walking speed.</summary>
    Walking = 1,

    /// <summary>On the floor, sprinting.</summary>
    Sprinting = 2,

    /// <summary>In the air and still rising after a jump.</summary>
    Jumping = 3,

    /// <summary>In the air and not rising.</summary>
    Falling = 4,

    /// <summary>Developer flight: no gravity, free direction.</summary>
    DevFlying = 5,
}
