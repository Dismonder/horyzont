using Godot;

using Horyzont.Client.Survival;
using Horyzont.Core.Survival;

namespace Horyzont.Game.Client.Inventory;

/// <summary>
/// The suit rows of the equipment panel (K6-34): one line per resource with its value, its capacity (with the modules worn) and the drain multiplier, for example
/// <c>Heating  75 / 200  x0.6</c>. A first layout for the owner to judge; the numbers come from <see cref="SuitHudText.ResourceLine"/>. Drawn by labels, no assets.
/// </summary>
public sealed partial class SuitPanel : VBoxContainer
{
    private static readonly string[] _resourceKeys = ["HUD_SUIT_HEATING", "HUD_SUIT_COOLING", "HUD_SUIT_FILTER", "HUD_SUIT_SHIELD", "HUD_SUIT_OXYGEN"];

    private readonly Label[] _rows = new Label[5];
    private Label? _title;
    private SuitResources _shownValues;
    private SuitStats _shownStats;
    private bool _shown;

    /// <summary>Gets the number of resource rows.</summary>
    public int RowCount => _rows.Length;

    /// <summary>Gets the text of a row, for example <c>Heating  75 / 200  x0.6</c> (tests).</summary>
    /// <param name="index">The row, 0 to 4.</param>
    /// <returns>The text.</returns>
    public string RowText(int index) => _rows[index]?.Text ?? string.Empty;

    /// <summary>Gets the title of the panel (tests).</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "SuitPanel";
        AddThemeConstantOverride("separation", 2);
        _title = new Label { Name = "Title" };
        HudTheme.StyleLabel(_title, 14, HudTheme.Muted);
        AddChild(_title);
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i] = new Label { Name = $"Row{i}" };
            HudTheme.StyleLabel(_rows[i], 13, HudTheme.Text);
            AddChild(_rows[i]);
        }
    }

    /// <summary>Shows the values; sets the texts only when the numbers changed (the screen asks every frame, review C-D P3-4).</summary>
    /// <param name="values">The value of each resource.</param>
    /// <param name="stats">The capacity and the drain of the suit as worn.</param>
    /// <param name="force">Set the texts even when the numbers are the same (the screen rebuilt, the language may have changed).</param>
    public void Apply(in SuitResources values, in SuitStats stats, bool force = false)
    {
        if (_title is null || (!force && _shown && _shownValues == values && _shownStats == stats))
        {
            return;
        }

        _shown = true;
        _shownValues = values;
        _shownStats = stats;
        _title.Text = $"{Tr("SUIT_PANEL_TITLE")}  ({Tr("SUIT_DRAIN")})";
        for (var i = 0; i < _rows.Length; i++)
        {
            var resource = (SuitResource)(i + 1);
            _rows[i].Text = string.Concat(Tr(_resourceKeys[i]), "  ", SuitHudText.ResourceLine(values[resource], stats.Capacity[resource], stats.Drain[resource]));
        }
    }
}
