namespace Horyzont.Core.Items;

/// <summary>Who may use a container.</summary>
public enum ContainerAccess : byte
{
    /// <summary>Every player.</summary>
    Shared = 1,

    /// <summary>Only the owner.</summary>
    Private = 2,
}
