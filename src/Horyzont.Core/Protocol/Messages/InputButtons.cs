namespace Horyzont.Core.Protocol.Messages;

/// <summary>Buttons held in an input.</summary>
[Flags]
public enum InputButtons : ushort
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>Jump.</summary>
    Jump = 1,

    /// <summary>Sprint.</summary>
    Sprint = 2,

    /// <summary>Crouch.</summary>
    Crouch = 4,

    /// <summary>Primary action.</summary>
    Primary = 8,

    /// <summary>Secondary action.</summary>
    Secondary = 16,
}
