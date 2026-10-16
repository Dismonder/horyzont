using System.Globalization;

using Godot;

using Horyzont.Client.Crafting;
using Horyzont.Client.Inventory;
using Horyzont.Core.Items;

using ToolHud = Horyzont.Game.Client.Tools.ToolHud;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The panel of an open furnace or refinery on the inventory screen (K6-32): the input grid, the output grid, the recipe the station works
/// on with a progress bar of the current piece, a "Choose" button at every recipe of the station the player knows and "Take all" for the
/// output. The layout is static; the look awaits the assessment of the owner. It only shows <see cref="StationSession"/>; every move is an
/// intent that the server answers. The bar is computed from the start tick of the station and the tick of the server (decision D23).
/// </summary>
public sealed partial class StationPanel : PanelContainer
{
    private const int SlotColumns = 4;

    private readonly InventoryNode _node;
    private readonly Func<Godot.Container, string, SlotView> _newSlot;
    private readonly List<SlotView> _input = [];
    private readonly List<SlotView> _output = [];
    private readonly List<Button> _choose = [];
    private readonly List<string> _recipeKeys = [];
    private Label? _title;
    private Label? _inputTitle;
    private Label? _outputTitle;
    private Label? _active;
    private Label? _status;
    private ProgressBar? _progress;
    private GridContainer? _inputGrid;
    private GridContainer? _outputGrid;
    private VBoxContainer? _recipes;
    private Button? _takeAll;
    private string? _shownRecipe;

    /// <summary>Creates the panel, hidden.</summary>
    /// <param name="node">The inventory whose station session the panel shows.</param>
    /// <param name="newSlot">Creates a slot in a container, wired to the clicks and the tooltip of the screen.</param>
    public StationPanel(InventoryNode node, Func<Godot.Container, string, SlotView> newSlot)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(newSlot);
        _node = node;
        _newSlot = newSlot;
        Name = "StationPanel";
        Visible = false;
    }

    /// <summary>Gets the slots of the input, in slot order.</summary>
    public IReadOnlyList<SlotView> InputSlots => _input;

    /// <summary>Gets the slots of the output, in slot order.</summary>
    public IReadOnlyList<SlotView> OutputSlots => _output;

    /// <summary>Gets the text of the heading.</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>Gets the line that names the recipe the station works on.</summary>
    public string ActiveText => _active?.Text ?? string.Empty;

    /// <summary>Gets the line under the recipes (the refusal of the server).</summary>
    public string StatusText => _status?.Text ?? string.Empty;

    /// <summary>Gets the number of "Choose" buttons.</summary>
    public int ChooseCount => _choose.Count;

    /// <summary>Gets the bar of the current piece.</summary>
    public ProgressBar? Bar => _progress;

    /// <summary>Presses the "Take all" button.</summary>
    public void PressTakeAll() => _takeAll?.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Presses the "Choose" button of a recipe row.</summary>
    /// <param name="row">Row index.</param>
    public void PressChoose(int row) => _choose[row].EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Gets the key of the recipe of a row.</summary>
    /// <param name="row">Row index.</param>
    /// <returns>The recipe key.</returns>
    public string RecipeKey(int row) => _recipeKeys[row];

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", HudTheme.Panel(HudTheme.Border, 1, new Color(0.05f, 0.07f, 0.1f, 0.6f), 10f));
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        _title = new Label();
        HudTheme.StyleLabel(_title, 15, HudTheme.Muted);
        column.AddChild(_title);

        _inputTitle = new Label();
        HudTheme.StyleLabel(_inputTitle, 13, HudTheme.Muted);
        column.AddChild(_inputTitle);
        _inputGrid = NewGrid(column, "Input");
        _outputTitle = new Label();
        HudTheme.StyleLabel(_outputTitle, 13, HudTheme.Muted);
        column.AddChild(_outputTitle);
        _outputGrid = NewGrid(column, "Output");

        _active = new Label();
        HudTheme.StyleLabel(_active, 14, HudTheme.Accent);
        column.AddChild(_active);
        _progress = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0f, 14f) };
        column.AddChild(_progress);

        _takeAll = new Button();
        HudTheme.StyleButton(_takeAll, HudTheme.Accent);
        _takeAll.Pressed += () =>
        {
            _ = _node.Stations.TakeAll();
            _node.Refresh();
        };
        column.AddChild(_takeAll);

        _recipes = new VBoxContainer();
        _recipes.AddThemeConstantOverride("separation", 6);
        column.AddChild(_recipes);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        HudTheme.StyleLabel(_status, 13, HudTheme.Danger);
        column.AddChild(_status);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!Visible || _progress is null)
        {
            return;
        }

        var stations = _node.Stations;
        var fraction = stations.Progress;
        _progress.Visible = fraction is not null;
        _progress.Value = (fraction ?? 0.0) * 100.0;

        // The recipe changes with the replicated state of the block, which does not refresh the screen by itself.
        var key = stations.ActiveRecipe?.Key;
        if (key != _shownRecipe)
        {
            _node.Refresh();
        }
    }

    /// <summary>Shows the open station, or hides the panel when none is open.</summary>
    /// <param name="held">The items in hand (the source slot is dimmed).</param>
    /// <param name="interaction">The mouse logic (slots in transit are dimmed).</param>
    public void Update(HeldStack? held, InventoryInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var stations = _node.Stations;
        Visible = stations.IsOpen;
        if (!stations.IsOpen || stations.Input is not { } input || stations.Output is not { } output || _inputGrid is null || _outputGrid is null)
        {
            return;
        }

        Fill(_input, _inputGrid, "In", input, held, interaction);
        Fill(_output, _outputGrid, "Out", output, held, interaction);

        _title!.Text = Tr("CRAFT_TITLE_" + stations.Kind.ToString().ToUpperInvariant());
        _inputTitle!.Text = Tr("CRAFT_INPUT");
        _outputTitle!.Text = Tr("CRAFT_OUTPUT");
        _takeAll!.Text = Tr("INV_TAKE_ALL");
        var active = stations.ActiveRecipe;
        _shownRecipe = active?.Key;
        _active!.Text = active is null
            ? Tr("CRAFT_NO_RECIPE")
            : string.Format(CultureInfo.InvariantCulture, Tr("CRAFT_ACTIVE"), NameOf(active.Result.ItemKey));
        _status!.Text = stations.LastRefusal == Horyzont.Core.Intents.RejectReason.None ? string.Empty : Tr(ToolHud.RejectKey(stations.LastRefusal));
        RebuildRecipes();
    }

    private static GridContainer NewGrid(Godot.Container parent, string name)
    {
        var grid = new GridContainer { Name = name, Columns = SlotColumns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        parent.AddChild(grid);
        return grid;
    }

    private void Fill(List<SlotView> views, GridContainer grid, string prefix, ClientContainer container, HeldStack? held, InventoryInteraction interaction)
    {
        while (views.Count < container.Size)
        {
            views.Add(_newSlot(grid, $"{prefix}{views.Count}"));
        }

        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var present = i < container.Size;
            view.Visible = present;
            if (!present)
            {
                continue;
            }

            var slotRef = new SlotRef(container.Id, (ushort)i);
            var stack = container.Get(i);
            var visual = stack.IsEmpty ? default : _node.VisualOf(stack.Type);
            view.Ref = slotRef;
            view.Set(stack, visual, -1, selected: false, dim: interaction.IsPending(slotRef) || held?.Source == slotRef, disabled: false);
        }
    }

    private void RebuildRecipes()
    {
        foreach (var child in _recipes!.GetChildren())
        {
            _recipes.RemoveChild(child);
            child.QueueFree();
        }

        _choose.Clear();
        _recipeKeys.Clear();
        var active = _node.Stations.ActiveRecipe?.Key;
        foreach (var view in _node.Recipes.Visible(_node.Stations.Kind))
        {
            var recipe = view.Recipe;
            var line = new HBoxContainer { Name = "Recipe" + _choose.Count };
            line.AddThemeConstantOverride("separation", 8);
            var text = new Label
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Text = Describe(view),
            };
            HudTheme.StyleLabel(text, 13, view.Affordable ? HudTheme.Text : HudTheme.Muted);
            line.AddChild(text);
            var choose = new Button { Text = Tr("CRAFT_SELECT"), Disabled = recipe.Key == active };
            HudTheme.StyleButton(choose, HudTheme.AccentTool);
            choose.Pressed += () =>
            {
                _ = _node.Stations.SelectRecipe(recipe);
                _node.Refresh();
            };
            line.AddChild(choose);
            _recipes.AddChild(line);
            _choose.Add(choose);
            _recipeKeys.Add(recipe.Key);
        }
    }

    private string Describe(RecipeView view)
    {
        var recipe = view.Recipe;
        var ingredients = string.Join(", ", view.Ingredients.Select(i => string.Format(CultureInfo.InvariantCulture, "{0} x{1}", NameOf(i.ItemKey), i.Need)));
        var text = string.Format(
            CultureInfo.InvariantCulture, "{0} x{1} <- {2} ({3} s)", NameOf(recipe.Result.ItemKey), recipe.Result.Count, ingredients, recipe.TimeSeconds);
        if (view.Fuel.Count > 0)
        {
            var fuel = string.Join(", ", view.Fuel.Select(f => string.Format(CultureInfo.InvariantCulture, "{0} x{1}", NameOf(f.ItemKey), f.Need)));
            text += $"\n{Tr("CRAFT_FUEL")}: {fuel}";
        }

        return text;
    }

    private string NameOf(string itemKey) => _node.Inventory.TryGetTypeId(itemKey, out var type) ? _node.ItemName(type) : itemKey;
}
