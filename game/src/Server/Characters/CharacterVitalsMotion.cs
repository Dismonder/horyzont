using Horyzont.Core.Ids;
using Horyzont.Server.Survival;

namespace Horyzont.Game.Server.Characters;

/// <summary>
/// The motion of the server bodies for the vitals service (K6-33 D13): the mode, the velocity and the feet of the latest state of a spawned character. The seat of a
/// player is read by the service from the state of the player, so <see cref="VitalsMotion.Attached"/> is false here.
/// </summary>
/// <param name="source">The characters of the server, null before the world has started (then nobody has a body).</param>
public sealed class CharacterVitalsMotion(Func<IServerCharacterSource?> source) : IVitalsMotionSource
{
    /// <inheritdoc />
    public bool TryGetMotion(PlayerId player, out VitalsMotion motion)
    {
        if (source() is { } characters && characters.TryGetState(player, out var state))
        {
            motion = new VitalsMotion(state.Mode, state.Velocity, state.Feet, false);
            return true;
        }

        motion = default;
        return false;
    }
}
