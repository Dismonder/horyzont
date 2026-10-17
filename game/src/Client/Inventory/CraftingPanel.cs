using System.Globalization;

using Godot;

using Horyzont.Client.Crafting;
using Horyzont.Core.World.Blocks;

using ToolHud = Horyzont.Game.Client.Tools.ToolHud;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The crafting panel on the inventory screen (K6-32): the recipes of the hands, or of the workbench, kitchen or fabricator that is open,
/// that the player knows. A row shows the result, the ingredients with "have / need" (the bag and the hotbar; the chests nearby are counted
/// by the server), the level a workbench recipe needs, a count box and the "Make" button. A row without enough ingredients is dimmed but
/// can still be pressed (the server decides). The layout is static; the look awaits the assessment of the owner. It only shows
/// <see cref="CraftingSession"/> and <see cref="RecipeList"/> and sends their calls.
/// </summary>
public sealed partial class CraftingPanel : PanelContainer
{
    private readonly InventoryNode _node;
    private readonly List<Button> _make = [];
    private readonly List<SpinBox> _counts = [];
    private readonly List<string> _rowTexts = [];
    private readonly List<string> _rowKeys = [];
    private readonly Dictionary<string, int> _remembered = new(StringComparer.Ordinal);
    private Label? _title;
    private Label? _status;
    private VBoxContainer? _rows;

    /// <summary>Creates the panel, hidden.</summary>
    /// <param name="node">The inventory whose crafting session the panel shows.</param>
    public CraftingPanel(InventoryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        Name = "CraftingPanel";
        Visible = false;
    }

    /// <summary>Gets the text of the heading.</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>Gets the line under the heading: the refusal of the server or the note that no recipe is known.</summary>
    public string StatusText => _status?.Text ?? string.Empty;

    /// <summary>Gets the number of recipe rows on show.</summary>
    public int RowCount => _make.Count;

    /// <summary>Gets the text of a row (result, ingredients, level), lines joined with a line feed.</summary>
    /// <param name="row">Row index.</param>
    /// <returns>The text.</returns>
    public string RowText(int row) => _rowTexts[row];

    /// <summary>Gets the key of the recipe of a row.</summary>
    /// <param name="row">Row index.</param>
    /// <returns>The recipe key.</returns>
    public string RecipeKey(int row) => _rowKeys[row];

    /// <summary>Gets the "Make" button of a row.</summary>
    /// <param name="row">Row index.</param>
    /// <returns>The button.</returns>
    public Button MakeButton(int row) => _make[row];

    /// <summary>Sets the count box of a row.</summary>
    /// <param name="row">Row index.</param>
    /// <param name="count">Pieces.</param>
    public void SetCount(int row, int count) => _counts[row].Value = count;

    /// <summary>Presses the "Make" button of a row.</summary>
    /// <param name="row">Row index.</param>
    public void PressMake(int row) => _make[row].EmitSignal(BaseButton.SignalName.Pressed);

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Border, 1, new Color(0.05f, 0.07f, 0.1f, 0.6f), 10f));
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(340f, 0f) };
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        _title = new Label();
        HudTheme.StyleLabel(_title, 15, HudTheme.Muted);
        column.AddChild(_title);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        HudTheme.StyleLabel(_status, 13, HudTheme.Danger);
        column.AddChild(_status);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 360f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);
    }

    /// <summary>Shows the recipes of the open station, or hides the panel while a chest or a station with time takes its place.</summary>
    public void Update()
    {
        var hidden = _node.Chests.IsOpen || _node.Stations.IsOpen || _node.Stations.IsOpening;
        Visible = !hidden;
        if (hidden || _rows is null)
        {
            return;
        }

        var crafting = _node.Crafting;
        var kind = crafting.Kind;
        _title!.Text = Tr("CRAFT_TITLE_" + kind.ToString().ToUpperInvariant());
        var views = _node.Recipes.Visible(kind);
        _status!.Text = crafting.ShowsLocked
            ? Tr("CRAFT_LOCKED")
            : crafting.LastRefusal != Horyzont.Core.Intents.RejectReason.None
                ? Tr(ToolHud.RejectKey(crafting.LastRefusal))
                : views.Count == 0 ? Tr("CRAFT_NONE") : string.Empty;

        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        _make.Clear();
        _counts.Clear();
        _rowTexts.Clear();
        _rowKeys.Clear();
        foreach (var view in views)
        {
            AddRow(view, kind);
        }
    }

    private void AddRow(RecipeView view, StationKind kind)
    {
        var recipe = view.Recipe;
        var row = new PanelContainer { Name = "Recipe" + _make.Count };
        row.AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Border, 1, HudTheme.SlotBackground, 8f));
        if (!view.Affordable)
        {
            row.Modulate = new Color(1f, 1f, 1f, 0.55f);
        }

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        line.AddChild(text);
        var lines = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, "{0} x{1}", NameOf(recipe.Result.ItemKey), recipe.Result.Count),
            string.Join(", ", view.Ingredients.Select(i => string.Format(CultureInfo.InvariantCulture, "{0} {1}/{2}", NameOf(i.ItemKey), i.Have, i.Need))),
        };
        if (kind != StationKind.Hands)
        {
            lines.Add(Tr("CRAFT_NEARBY"));
        }

        if (kind == StationKind.Workbench && recipe.Level > 1)
        {
            lines.Add(string.Format(CultureInfo.InvariantCulture, Tr("CRAFT_LEVEL"), recipe.Level));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = lines[i] };
            HudTheme.StyleLabel(label, i == 0 ? 15 : 13, i == 0 ? HudTheme.Accent : HudTheme.Muted);
            text.AddChild(label);
        }

        _rowTexts.Add(string.Join('\n', lines));
        _rowKeys.Add(recipe.Key);

        var count = new SpinBox { MinValue = 1, MaxValue = 50, Step = 1, Value = _remembered.GetValueOrDefault(recipe.Key, 1) };
        var key = recipe.Key;
        count.ValueChanged += value => _remembered[key] = (int)value;
        line.AddChild(count);
        _counts.Add(count);

        var make = new Button { Text = Tr("CRAFT_MAKE") };
        HudTheme.StyleButton(make, HudTheme.Accent);
        make.Pressed += () =>
        {
            _ = _node.Crafting.Craft(recipe, (ushort)count.Value);
            _node.Refresh();
        };
        line.AddChild(make);
        _make.Add(make);

        _rows!.AddChild(row);
    }

    private string NameOf(string itemKey) => _node.Inventory.TryGetTypeId(itemKey, out var type) ? _node.ItemName(type) : itemKey;
}
