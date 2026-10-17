namespace Horyzont.Core.Intents;

/// <summary>The setting a <see cref="BlockConfigIntent"/> changes. A closed hierarchy: only this assembly derives from it.</summary>
public abstract record BlockConfig
{
    private protected BlockConfig()
    {
    }

    /// <summary>Variant of the setting.</summary>
    public abstract BlockConfigKind Kind { get; }
}
