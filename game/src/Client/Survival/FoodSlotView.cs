using Godot;

using Horyzont.Game.Client.Inventory;

namespace Horyzont.Game.Client.Survival;

/// <summary>One food slot: the icon of the item, a thin bar of the time left and the minutes under it.</summary>
internal sealed partial class FoodSlotView : Control
{
    private const float Side = 44f;
    private bool _blinkOn = true;

    public FoodSlotData Data { get; private set; }

    public string TimeText { get; private set; } = string.Empty;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Side, Side + 18f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(FoodSlotData data, string remainingWord)
    {
        Data = data;
        TimeText = data.Filled ? data.Time : string.Empty;
        TooltipText = data.Filled ? string.Concat(data.Name, ": ", data.Time, " ", remainingWord) : string.Empty;
        QueueRedraw();
    }

    public void SetBlinkPhase(bool on)
    {
        if (on != _blinkOn)
        {
            _blinkOn = on;
            if (Data.Filled && Data.Blinking)
            {
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        var box = new Rect2(Vector2.Zero, new Vector2(Side, Side));
        DrawStyleBox(HudTheme.Panel(Data.Filled && Data.Blinking && _blinkOn ? HudTheme.Accent : null, 1, HudTheme.SlotBackground, 0f), box);
        if (!Data.Filled)
        {
            return;
        }

        var alpha = Data.Blinking && !_blinkOn ? 0.45f : 1f;
        ItemIconPainter.Draw(this, new Rect2(new Vector2(6f, 6f), new Vector2(Side - 12f, Side - 12f)), Data.Visual, alpha);
        var bar = new Rect2(new Vector2(0f, Side + 2f), new Vector2(Side, 3f));
        DrawRect(bar, HudTheme.SlotBackground);
        DrawRect(new Rect2(bar.Position, new Vector2(Side * (float)Math.Clamp(Data.Fraction, 0.0, 1.0), bar.Size.Y)), HudTheme.Accent);
        var font = ThemeDB.FallbackFont;
        var size = 12;
        var width = font.GetStringSize(Data.Time, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2((Side - width) * 0.5f, Side + 16f), Data.Time, HorizontalAlignment.Left, -1, size, HudTheme.Muted);
    }
}
