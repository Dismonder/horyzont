namespace Horyzont.Client.UI;

/// <summary>Prevents gameplay input while a menu or modal owns focus.</summary>
public interface IGameplayInputGate
{
    /// <summary>Gets whether gameplay input is blocked.</summary>
    bool IsBlocked { get; }
}
