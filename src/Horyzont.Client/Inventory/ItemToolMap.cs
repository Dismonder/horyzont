using Horyzont.Client.Tools;

namespace Horyzont.Client.Inventory;

/// <summary>Which tool an item selects when it sits in the selected hotbar slot (<c>visual.clientTool</c> of the content, decision D18).</summary>
public sealed class ItemToolMap
{
    private readonly Dictionary<string, ToolKind> _tools;

    /// <summary>Initializes a new instance of the <see cref="ItemToolMap"/> class.</summary>
    /// <param name="tools">Tool by item key.</param>
    public ItemToolMap(IReadOnlyDictionary<string, ToolKind> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = new Dictionary<string, ToolKind>(tools, StringComparer.Ordinal);
    }

    /// <summary>Gets the map of the shipped content: the multitool and the build tool.</summary>
    public static ItemToolMap Default { get; } = new(
        new Dictionary<string, ToolKind> { ["core:multitool"] = ToolKind.TerrainManipulator, ["core:build_tool"] = ToolKind.Builder });

    /// <summary>The tool of an item.</summary>
    /// <param name="key">Item key, or null.</param>
    /// <returns>The tool, or <see cref="ToolKind.None"/> for resources, unknown items and empty slots.</returns>
    public ToolKind ForKey(string? key) => key is not null && _tools.TryGetValue(key, out var tool) ? tool : ToolKind.None;

    /// <summary>The tool that the selected hotbar slot selects.</summary>
    /// <param name="inventory">The inventory.</param>
    /// <param name="selection">The hotbar selection.</param>
    /// <returns>The tool, <see cref="ToolKind.None"/> when the slot holds none.</returns>
    public ToolKind Resolve(ClientInventory inventory, HotbarSelection selection)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(selection);
        var stack = selection.SelectedStack(inventory);
        return stack.IsEmpty ? ToolKind.None : ForKey(inventory.KeyOf(stack.Type));
    }
}
