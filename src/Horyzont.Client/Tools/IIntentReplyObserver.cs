using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Tools;

/// <summary>Lets edit prediction inspect a reply before its pending intent is settled.</summary>
public interface IIntentReplyObserver
{
    /// <summary>Notes a reply before commit or revert.</summary>
    /// <param name="reply">Server reply.</param>
    void NoteReply(IntentReply reply);
}
