using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;

namespace Horyzont.Game.Server.Characters;

/// <summary>The input buffers of a <see cref="ServerSession"/> as an <see cref="IInputSource"/>.</summary>
/// <param name="session">The session whose buffers are read.</param>
public sealed class SessionInputSource(ServerSession session) : IInputSource
{
    private readonly ServerSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public bool TryTakeInput(PlayerId player, out PlayerInput input, out uint seq) => _session.TryTakeInput(player, out input, out seq);
}
