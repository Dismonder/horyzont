namespace Horyzont.Core.Intents;

/// <summary>Variant of a <see cref="BlockConfig"/>; the number is the variant byte of the intent (K6-30 D18).</summary>
public enum BlockConfigKind : byte
{
    /// <summary>The name of a teleport (K6-37; K6-30 answers it with invalid parameters).</summary>
    TeleportName = 1,

    /// <summary>The access of a chest container.</summary>
    ContainerAccess = 2,
}
