using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Ships;

/// <summary>When a player body starts the suit glide (K5-25 decision 19); the same rule on the server and the client.</summary>
public static class EjectRules
{
    /// <summary>Smallest initial speed of a glide, m/s.</summary>
    public const float GlideMinSpeed = 0.5f;

    /// <summary>A reparent from a construct frame to a planet frame with a velocity above <see cref="GlideMinSpeed"/>: an ejection.</summary>
    /// <param name="op">The reparent.</param>
    /// <param name="previousFrame">Kind of the frame the entity left.</param>
    /// <returns>True when the body glides.</returns>
    public static bool StartsGlide(EntityReparentOp op, FrameKind previousFrame)
    {
        ArgumentNullException.ThrowIfNull(op);
        return previousFrame == FrameKind.Construct && op.NewParent.Kind == FrameKind.Planet && op.LocalVelocity.Length() > GlideMinSpeed;
    }
}
