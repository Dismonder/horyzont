namespace Horyzont.Client.Props;

/// <summary>The tool in the selected hotbar slot of the local player, as far as hitting props needs it.</summary>
public interface IHeldTool
{
    /// <summary>Gets the selected hotbar slot (the slot the server reads the tool from).</summary>
    int HotbarSlot { get; }

    /// <summary>Gets the tier of the item in the selected slot; 0 for an empty slot, a resource or any item that is no tool (the hand).</summary>
    int ToolTier { get; }
}
