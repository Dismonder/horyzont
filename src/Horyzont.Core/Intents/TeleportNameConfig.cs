namespace Horyzont.Core.Intents;

/// <summary>A new teleport name, 1 to 32 UTF-8 bytes (K6-37 handles it).</summary>
/// <param name="Name">The name.</param>
public sealed record TeleportNameConfig(string Name) : BlockConfig
{
    /// <inheritdoc />
    public override BlockConfigKind Kind => BlockConfigKind.TeleportName;
}
