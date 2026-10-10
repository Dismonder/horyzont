using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Server;

/// <summary>Handles owned client message types on the server main thread.</summary>
public interface IServerClientMessageHandler
{
    /// <summary>Handles a message; all registered handlers still receive it.</summary>
    /// <param name="player">Sending player.</param>
    /// <param name="message">Message.</param>
    /// <returns>True only for a type owned by this handler.</returns>
    bool Handle(PlayerId player, Message message);
}
