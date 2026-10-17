using System.Globalization;

using Godot;

using Horyzont.Core.Content;
using Horyzont.Core.Items;

namespace Horyzont.Game.Client.Inventory;

/// <summary>The tooltip of a stack: name (amber), category, number of items and the stack limit.</summary>
public sealed partial class ItemTooltip : PanelContainer
{
    private readonly Label _title = new();
    private readonly Label _body = new();

    /// <summary>Creates the tooltip, hidden.</summary>
    public ItemTooltip()
    {
        Name = "ItemTooltip";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Accent, 1, new Color(0.04f, 0.06f, 0.09f, 0.95f), 8f));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        HudTheme.StyleLabel(_title, 16, HudTheme.Accent);
        HudTheme.StyleLabel(_body, 13, HudTheme.Muted);
        box.AddChild(_title);
        box.AddChild(_body);
        AddChild(box);
    }

    /// <summary>Gets the name line.</summary>
    public string Title => _title.Text;

    /// <summary>Gets the lines under the name.</summary>
    public string Body => _body.Text;

    /// <summary>Shows the tooltip of a stack next to the cursor.</summary>
    /// <param name="node">The inventory, for names and definitions.</param>
    /// <param name="stack">The stack.</param>
    /// <param name="cursor">Cursor position in the viewport.</param>
    public void ShowFor(InventoryNode node, ItemStack stack, Vector2 cursor)
    {
        ArgumentNullException.ThrowIfNull(node);
        _title.Text = node.ItemName(stack.Type);
        var lines = new List<string>(3);
        if (node.Inventory.TryGetDef(stack.Type, out var def))
        {
            lines.Add(Tr("INV_CATEGORY") + ": " + node.CategoryName(def.Category));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Tr("INV_COUNT")}: {stack.Count}   {Tr("INV_STACK")}: {def.MaxStack}"));
            if ((def.Flags & ItemFlags.Tool) != 0)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Tier {def.ToolTier}"));
            }
        }

        _body.Text = string.Join('\n', lines);
        Visible = true;
        ResetSize();
        Follow(cursor);
    }

    /// <summary>Moves the tooltip to the cursor, keeping it inside the viewport.</summary>
    /// <param name="cursor">Cursor position in the viewport.</param>
    public void Follow(Vector2 cursor)
    {
        var viewport = GetViewportRect().Size;
        var position = cursor + new Vector2(18f, 18f);
        position.X = Math.Min(position.X, viewport.X - Size.X - 4f);
        position.Y = Math.Min(position.Y, viewport.Y - Size.Y - 4f);
        Position = position;
    }

    /// <summary>Hides the tooltip.</summary>
    public void HideTip() => Visible = false;
}
