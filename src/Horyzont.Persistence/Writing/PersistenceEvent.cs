namespace Horyzont.Persistence.Writing;

/// <summary>Result of the writer thread for the server loop, read with <see cref="PersistenceWriter.TryDequeueEvent"/>.</summary>
public abstract record PersistenceEvent
{
    private protected PersistenceEvent()
    {
    }
}
