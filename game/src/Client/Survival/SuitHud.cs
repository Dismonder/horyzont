using Godot;

using Horyzont.Client.Survival;
using Horyzont.Core.Survival;
using Horyzont.Game.Client.Inventory;

namespace Horyzont.Game.Client.Survival;

/// <summary>
/// The suit of the local player above the bars of health and stamina (K6-34): a row of five resource bars (heating orange, cooling blue, filter green, shield purple, oxygen white), under each
/// the icon word of its hazard (grey when the hazard is present, coloured when it acts, pulsing red when it reaches the player), and a line with the shelter, the generator and the life support.
/// A first layout for the owner to judge; the final HUD is K6-39. Drawn by small controls, no assets. The data are set with <see cref="Apply"/>.
/// </summary>
public sealed partial class SuitHud : VBoxContainer
{
    internal const float BarWidth = 112f;
    internal const float BarHeight = 18f;

    private static readonly string[] _resourceKeys = ["HUD_SUIT_HEATING", "HUD_SUIT_COOLING", "HUD_SUIT_FILTER", "HUD_SUIT_SHIELD", "HUD_SUIT_OXYGEN"];
    private static readonly string[] _hazardKeys = ["HUD_HAZARD_COLD", "HUD_HAZARD_HEAT", "HUD_HAZARD_TOXINS", "HUD_HAZARD_RADIATION", "HUD_HAZARD_VACUUM"];
    private static readonly Color[] _colors =
    [
        new Color(1.00f, 0.60f, 0.20f), new Color(0.40f, 0.75f, 1.00f), new Color(0.40f, 0.85f, 0.40f), new Color(0.70f, 0.45f, 0.90f), new Color(0.95f, 0.95f, 0.95f),
    ];

    private readonly VitalsBarView[] _bars = new VitalsBarView[5];
    private readonly Label[] _hazards = new Label[5];
    private readonly byte[] _hazardState = new byte[5]; // 0 absent, 1 present, 2 active, 3 exposed
    private HBoxContainer? _barRow;
    private HBoxContainer? _hazardRow;
    private Label? _status;
    private SuitHudData _shown;
    private bool _hasShown;
    private double _clock;

    /// <summary>Gets a value indicating whether the suit HUD is shown (tests).</summary>
    public bool IsShown => _barRow?.Visible ?? false;

    /// <summary>Gets the text on a resource bar, for example <c>Heating  75 / 100</c> (tests).</summary>
    /// <param name="index">The resource, 0 to 4.</param>
    /// <returns>The text.</returns>
    public string BarText(int index) => _bars[index]?.Text ?? string.Empty;

    /// <summary>Gets the state of a hazard icon: 0 absent, 1 present, 2 active, 3 exposed (tests).</summary>
    /// <param name="index">The hazard, 0 to 4.</param>
    /// <returns>The state.</returns>
    public int HazardState(int index) => _hazardState[index];

    /// <summary>Gets the text of the line with the shelter, the generator and the life support, empty when nothing is shown (tests).</summary>
    public string StatusText => _status?.Text ?? string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        Name = "SuitHud";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 2);

        _barRow = new HBoxContainer { Name = "Bars", Alignment = AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        _barRow.AddThemeConstantOverride("separation", 4);
        AddChild(_barRow);
        _hazardRow = new HBoxContainer { Name = "Hazards", Alignment = AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        _hazardRow.AddThemeConstantOverride("separation", 4);
        AddChild(_hazardRow);
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new VitalsBarView(_colors[i], BarWidth, BarHeight) { Name = $"Resource{i}" };
            _barRow.AddChild(_bars[i]);
            _hazards[i] = new Label
            {
                Name = $"Hazard{i}",
                CustomMinimumSize = new Vector2(BarWidth, 0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            HudTheme.StyleLabel(_hazards[i], 11, HudTheme.Muted);
            _hazardRow.AddChild(_hazards[i]);
        }

        _status = new Label { Name = "Status", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        HudTheme.StyleLabel(_status, 12, HudTheme.AccentTool);
        AddChild(_status);
        _barRow.Visible = false;
        Visible = false;
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
        {
            _hasShown = false;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _clock += delta;
        var pulse = 0.5f + (0.5f * (float)Math.Sin(_clock * 6.0));
        for (var i = 0; i < _hazards.Length; i++)
        {
            if (_hazardState[i] == 3 && _hazards[i] is { } label)
            {
                label.Modulate = new Color(1f, 0.55f + (0.45f * pulse), 0.55f + (0.45f * pulse));
            }
        }
    }

    /// <summary>Shows the data of one frame; redraws only what changed.</summary>
    /// <param name="data">The data.</param>
    public void Apply(in SuitHudData data)
    {
        if (_barRow is null || _hazardRow is null || _status is null)
        {
            return;
        }

        if (_hasShown && data == _shown)
        {
            return;
        }

        _shown = data;
        _hasShown = true;
        Visible = data.Visible;
        _barRow.Visible = data.Visible;
        if (!data.Visible)
        {
            return;
        }

        for (var i = 0; i < _bars.Length; i++)
        {
            var resource = (SuitResource)(i + 1);
            var value = data.Values[resource];
            var capacity = data.Capacity[resource];
            _bars[i].Set(value, capacity, string.Concat(Tr(_resourceKeys[i]), "  ", VitalsHudText.Pair(value, capacity)), false);
            var bit = 1 << i;
            var state = (data.Exposed & bit) != 0 ? 3 : (data.Active & bit) != 0 ? 2 : (data.Present & bit) != 0 ? 1 : 0;
            _hazardState[i] = (byte)state;
            var label = _hazards[i];
            label.Text = state == 0 ? string.Empty : Tr(_hazardKeys[i]);
            label.Modulate = Colors.White;
            label.AddThemeColorOverride("font_color", state switch { 3 => HudTheme.Danger, 2 => _colors[i], _ => HudTheme.Muted });
        }

        _status.Text = StatusLine(data);
        _status.Visible = _status.Text.Length > 0;
    }

    private string StatusLine(in SuitHudData data)
    {
        var line = string.Empty;
        if (data.Sheltered)
        {
            line = string.Concat(Tr("HUD_SHELTER"), " ", SuitHudText.ShelterHits(data.ShelterHits));
        }

        if (data.Generator)
        {
            line = line.Length == 0 ? Tr("HUD_OXYGEN_GENERATOR") : string.Concat(line, "   ", Tr("HUD_OXYGEN_GENERATOR"));
        }

        if (data.LifeSupport)
        {
            line = line.Length == 0 ? Tr("HUD_LIFE_SUPPORT") : string.Concat(line, "   ", Tr("HUD_LIFE_SUPPORT"));
        }

        return line;
    }
}
