using System.Globalization;

using Godot;

using Horyzont.Client.Survival;
using Horyzont.Game.Client.Inventory;

namespace Horyzont.Game.Client.Survival;

/// <summary>
/// The vitals of the local player at the bottom of the screen, above the hotbar (K6-33 D3): a red bar of health and a yellow bar of stamina with whole numbers, three
/// food slots with an icon, a thin bar of the time left and the minutes (a slot that may be eaten again blinks), and the state of the rest. The layout is a first
/// version for the owner to judge; the final HUD is K6-39. The data come from a source function (<see cref="VitalsHudSource"/> in the game) or are set directly
/// (<see cref="Apply"/>, for tests and screenshots); drawing is by <c>_Draw</c> of small controls, no assets.
/// </summary>
public sealed partial class VitalsHud : CanvasLayer
{
    internal const float BarWidth = 230f;
    internal const float BarHeight = 22f;
    private const double BlinkSeconds = 0.6;

    private readonly Func<VitalsHudData>? _source;
    private readonly Func<SuitHudData>? _suitSource;
    private readonly FoodSlotView[] _food = new FoodSlotView[3];
    private HBoxContainer? _row;
    private SuitHud? _suit;
    private VitalsBarView? _health;
    private VitalsBarView? _stamina;
    private Label? _rest;
    private VitalsHudData _shown;
    private bool _hasShown;
    private double _clock;

    /// <summary>Initializes a new instance of the <see cref="VitalsHud"/> class.</summary>
    /// <param name="source">Builds the data of each frame; null when the data are set with <see cref="Apply"/>.</param>
    /// <param name="suitSource">Builds the data of the suit row (K6-34); null when the world has no hazards, then no suit row is shown.</param>
    public VitalsHud(Func<VitalsHudData>? source = null, Func<SuitHudData>? suitSource = null)
    {
        _source = source;
        _suitSource = suitSource;
        Name = "VitalsHud";
        Layer = 8;
    }

    /// <summary>Gets the suit row above the bars, or null before the HUD is ready (tests).</summary>
    public SuitHud? Suit => _suit;

    /// <summary>Gets the text on the health bar, for example <c>85 / 110</c> (tests).</summary>
    public string HealthText => _health?.Text ?? string.Empty;

    /// <summary>Gets the text on the stamina bar (tests).</summary>
    public string StaminaText => _stamina?.Text ?? string.Empty;

    /// <summary>Gets the text of the rest line, empty when nothing is shown (tests).</summary>
    public string RestText => _rest?.Text ?? string.Empty;

    /// <summary>Gets the time text under a food slot, empty for an empty slot (tests).</summary>
    /// <param name="index">The slot, 0 to 2.</param>
    /// <returns>The text.</returns>
    public string FoodText(int index) => _food[index]?.TimeText ?? string.Empty;

    /// <summary>Gets a value indicating whether a food slot holds food (tests).</summary>
    /// <param name="index">The slot, 0 to 2.</param>
    /// <returns>Whether it is filled.</returns>
    public bool FoodFilled(int index) => _food[index]?.Data.Filled ?? false;

    /// <summary>Gets a value indicating whether the stamina bar shows the exhaustion (tests).</summary>
    public bool ShowsExhausted => _stamina?.Pulsing ?? false;

    /// <summary>Gets a value indicating whether the whole HUD is shown (tests).</summary>
    public bool IsShown => _row?.Visible ?? false;

    /// <inheritdoc />
    public override void _Ready()
    {
        var root = new VBoxContainer { Name = "Box", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        root.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        root.OffsetBottom = -112f; // above the hotbar: slots, name and margin
        root.GrowVertical = Control.GrowDirection.Begin;
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        _rest = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        HudTheme.StyleLabel(_rest, 14, HudTheme.AccentTool);
        root.AddChild(_rest);

        _suit = new SuitHud();
        root.AddChild(_suit);

        _row = new HBoxContainer { Name = "Row", Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _row.AddThemeConstantOverride("separation", 14);
        root.AddChild(_row);

        var bars = new VBoxContainer { Name = "Bars", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        bars.AddThemeConstantOverride("separation", 4);
        _row.AddChild(bars);
        _health = new VitalsBarView(HudTheme.Danger) { Name = "Health" };
        _stamina = new VitalsBarView(new Color(0.91f, 0.83f, 0.30f)) { Name = "Stamina" };
        bars.AddChild(_health);
        bars.AddChild(_stamina);

        var foods = new HBoxContainer { Name = "Food", MouseFilter = Control.MouseFilterEnum.Ignore };
        foods.AddThemeConstantOverride("separation", 6);
        _row.AddChild(foods);
        for (var i = 0; i < _food.Length; i++)
        {
            _food[i] = new FoodSlotView { Name = $"Food{i}" };
            foods.AddChild(_food[i]);
        }

        _row.Visible = false;
        _rest.Visible = false;
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
        if (_suitSource is { } suitSource && _suit is not null)
        {
            _suit.Apply(suitSource());
        }

        if (_source is { } source)
        {
            Apply(source());
        }

        var blink = ((int)(_clock / BlinkSeconds) & 1) == 0;
        _stamina?.Animate(_clock);
        foreach (var slot in _food)
        {
            slot?.SetBlinkPhase(blink);
        }
    }

    /// <summary>Shows the data of one frame; redraws only what changed.</summary>
    /// <param name="data">The data.</param>
    public void Apply(in VitalsHudData data)
    {
        if (_row is null || _health is null || _stamina is null || _rest is null)
        {
            return;
        }

        if (_hasShown && SameData(data))
        {
            return;
        }

        _shown = data with { Food = [.. data.Food] };
        _hasShown = true;
        _row.Visible = data.Visible;
        if (!data.Visible)
        {
            _rest.Visible = false;
            return;
        }

        _health.Set(data.Health, data.HealthMax, string.Concat(Tr("HUD_HEALTH"), "  ", VitalsHudText.Pair(data.Health, data.HealthMax)), false);
        var staminaText = string.Concat(Tr("HUD_STAMINA"), "  ", VitalsHudText.Pair(data.Stamina, data.StaminaMax));
        if (data.Exhausted)
        {
            staminaText = string.Concat(Tr("HUD_EXHAUSTED"), "  ", VitalsHudText.Pair(data.Stamina, data.StaminaMax));
        }

        _stamina.Set(data.Stamina, data.StaminaMax, staminaText, data.Exhausted);
        for (var i = 0; i < _food.Length; i++)
        {
            _food[i].Set(i < data.Food.Count ? data.Food[i] : default, Tr("HUD_FOOD_REMAINING"));
        }

        var rest = RestLine(data);
        _rest.Text = rest;
        _rest.Visible = rest.Length > 0;
    }

    private string RestLine(in VitalsHudData data)
    {
        if (data.RestedTime.Length > 0)
        {
            return string.Concat(Tr("HUD_RESTED"), " ", data.RestedTime, "   ", Tr("HUD_COMFORT"), " ", data.Comfort.ToString(CultureInfo.InvariantCulture));
        }

        if (data.Resting)
        {
            return string.Concat(
                Tr("HUD_RESTING"), " ", data.WarmupSeconds.ToString(CultureInfo.InvariantCulture), " / ", data.WarmupTotalSeconds.ToString(CultureInfo.InvariantCulture), " s");
        }

        return string.Empty;
    }

    private bool SameData(in VitalsHudData data)
    {
        if (data.Visible != _shown.Visible || data.Health != _shown.Health || data.HealthMax != _shown.HealthMax || data.Stamina != _shown.Stamina
            || data.StaminaMax != _shown.StaminaMax || data.Exhausted != _shown.Exhausted || data.Resting != _shown.Resting
            || data.WarmupSeconds != _shown.WarmupSeconds || data.WarmupTotalSeconds != _shown.WarmupTotalSeconds || data.RestedTime != _shown.RestedTime
            || data.Comfort != _shown.Comfort || data.Food.Count != _shown.Food.Count)
        {
            return false;
        }

        for (var i = 0; i < data.Food.Count; i++)
        {
            if (!data.Food[i].Equals(_shown.Food[i]))
            {
                return false;
            }
        }

        return true;
    }
}
