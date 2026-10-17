namespace Horyzont.Core.BlockEntities;

/// <summary>State of a functional block (K6-30). A closed hierarchy: only this assembly derives from it.</summary>
public abstract record BlockEntityState
{
    private protected BlockEntityState()
    {
    }

    /// <summary>Kind of the state.</summary>
    public abstract BlockEntityKind Kind { get; }
}
