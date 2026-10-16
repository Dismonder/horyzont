using Godot;

using Horyzont.Game.Client.Inventory;

namespace Horyzont.Game.Client.Survival;

/// <summary>A bar with its text inside; pulses and dims while the player is exhausted.</summary>
internal sealed partial class VitalsBarView : Control
{
    private readonly Color _color;
    private float _value;
    private float _max = 1f;
    private bool _pulsing;
    private float _pulse = 1f;

    public VitalsBarView(Color color) => _color = color;

    public string Text { get; private set; } = string.Empty;

    public bool Pulsing => _pulsing;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(VitalsHud.BarWidth, VitalsHud.BarHeight);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(float value, float max, string text, bool pulsing)
    {
        _value = float.IsFinite(value) ? value : 0f;
        _max = float.IsFinite(max) && max > 0f ? max : 1f;
        Text = text;
        _pulsing = pulsing;
        QueueRedraw();
    }

    public void Animate(double clock)
    {
        var pulse = _pulsing ? 0.55f + (0.25f * (float)Math.Sin(clock * 6.0)) : 1f;
        if (Math.Abs(pulse - _pulse) > 0.01f)
        {
            _pulse = pulse;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, new Vector2(VitalsHud.BarWidth, VitalsHud.BarHeight));
        DrawStyleBox(HudTheme.Panel(margin: 0f), rect);
        var fraction = Math.Clamp(_value / _max, 0f, 1f);
        if (fraction > 0f)
        {
            var fill = new Rect2(new Vector2(2f, 2f), new Vector2((VitalsHud.BarWidth - 4f) * fraction, VitalsHud.BarHeight - 4f));
            DrawRect(fill, new Color(_color.R, _color.G, _color.B, _color.A * _pulse));
        }

        var font = ThemeDB.FallbackFont;
        var size = 13;
        var width = font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2((VitalsHud.BarWidth - width) * 0.5f, (VitalsHud.BarHeight * 0.5f) + (size * 0.36f)), Text, HorizontalAlignment.Left, -1, size, HudTheme.Text);
    }
}
