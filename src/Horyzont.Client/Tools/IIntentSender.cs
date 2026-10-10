using Horyzont.Core.Intents;

namespace Horyzont.Client.Tools;

/// <summary>Where the tools send intents: the client session, or a fake in tests and bots.</summary>
public interface IIntentSender
{
    /// <summary>Gets a value indicating whether an intent can be sent now (the session is in game).</summary>
    bool CanSend { get; }

    /// <summary>Sends an intent.</summary>
    /// <param name="intent">The intent.</param>
    /// <returns>The client sequence number that the reply carries.</returns>
    uint Submit(Intent intent);
}
