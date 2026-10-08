namespace Horyzont.Core.Operations;

/// <summary>A change of the world state, stored in the journal. The only way to change the world.</summary>
public abstract record Operation
{
    private protected Operation()
    {
    }

    /// <summary>Operation code.</summary>
    public abstract OperationKind Kind { get; }
}
