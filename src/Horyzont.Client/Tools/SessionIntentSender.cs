using Horyzont.Core.Intents;
using Horyzont.Net.Session;

namespace Horyzont.Client.Tools;

/// <summary>Sends intents through a <see cref="ClientSession"/> that is in game.</summary>
/// <param name="session">The session.</param>
public sealed class SessionIntentSender(ClientSession session) : IIntentSender
{
    private readonly ClientSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public bool CanSend => _session.State == ClientState.InGame;

    /// <inheritdoc />
    public uint Submit(Intent intent) => _session.SubmitIntent(intent);
}
