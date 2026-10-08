namespace Horyzont.Core.Intents;

/// <summary>A request of a player; the server validates it and resolves it into an operation.</summary>
public abstract record Intent
{
    private protected Intent()
    {
    }
}
