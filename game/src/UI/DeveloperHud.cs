using System.Globalization;
using System.Text;

using Godot;

using Horyzont.Client.Diagnostics;
using Horyzont.Core.Intents;
using Horyzont.Game.Client.Tools;

namespace Horyzont.Game.UI;

/// <summary>
/// The developer HUD (F3, hidden by default): FPS, tick, tick duration, bandwidth, round-trip time and the state of the tools.
/// It reads through <see cref="HudSampler"/> four times per second, outside the simulation, and never touches the world.
/// </summary>
public sealed partial class DeveloperHud : CanvasLayer
{
    private const double RefreshSeconds = 0.25;
    private const double KiB = 1024.0;
    private static readonly StringName _toggle = new("dev_hud_toggle");

    private readonly StringBuilder _text = new();
    private HudSampler? _sampler;
    private Func<ToolNode?>? _tools;
    private double _sinceRefresh;
    private Label? _metrics;
    private Label? _toolsLabel;

    /// <summary>Gets a value indicating whether the HUD is shown.</summary>
    public bool IsShown => Visible;

    /// <summary>Gets the text of the metrics label (diagnostics and tests).</summary>
    public string MetricsText => _metrics?.Text ?? string.Empty;

    /// <summary>Gets the text of the tools label (diagnostics and tests).</summary>
    public string ToolsText => _toolsLabel?.Text ?? string.Empty;

    /// <inheritdoc />
    public override void _Ready()
    {
        _metrics = GetNode<Label>("%MetricsLabel");
        _toolsLabel = GetNode<Label>("%ToolsLabel");
        Visible = false;
        SetProcess(false);
    }

    /// <summary>Follows a session.</summary>
    /// <param name="sampler">Sampler of the session; null stops following.</param>
    /// <param name="tools">The tools of the session, if any.</param>
    public void Bind(HudSampler? sampler, Func<ToolNode?>? tools)
    {
        _sampler = sampler;
        _tools = tools;
        _sinceRefresh = RefreshSeconds;
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(_toggle) && !@event.IsEcho())
        {
            Visible = !Visible;
            SetProcess(Visible);
            _sinceRefresh = RefreshSeconds;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_sampler is null || _metrics is null || _toolsLabel is null)
        {
            return;
        }

        _sampler.NoteRenderDelta(delta);
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshSeconds)
        {
            return;
        }

        _sinceRefresh = 0;
        var sample = _sampler.Sample();
        _text.Clear();
        AppendField(sample.Present, HudFields.Fps, "ui.hud_fps", sample.Fps, "F0");
        _text.Append("   ").Append(Tr("ui.hud_tick")).Append(' ');
        if ((sample.Present & HudFields.ServerTick) != 0)
        {
            _text.Append(sample.Tick.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            _text.Append('—');
        }

        _text.Append("   ").Append(Tr("ui.hud_tick_ms")).Append(' ');
        if ((sample.Present & HudFields.ServerTickMilliseconds) != 0)
        {
            _text.Append(sample.ServerTickMilliseconds.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms");
        }
        else
        {
            _text.Append('—');
        }

        _text.Append('\n').Append(Tr("ui.hud_rtt")).Append(' ');
        if ((sample.Present & HudFields.Rtt) != 0)
        {
            _text.Append(sample.RttMilliseconds.ToString("F0", CultureInfo.InvariantCulture)).Append(" ms");
        }
        else
        {
            _text.Append('—');
        }

        _text.Append("   ").Append(Tr("ui.hud_bandwidth")).Append(' ');
        if ((sample.Present & HudFields.Bandwidth) != 0)
        {
            _text.Append((sample.SentBytesPerSecond / KiB).ToString("F1", CultureInfo.InvariantCulture)).Append(" / ")
                .Append((sample.ReceivedBytesPerSecond / KiB).ToString("F1", CultureInfo.InvariantCulture)).Append(" KiB/s");
        }
        else
        {
            _text.Append('—');
        }

        if (sample.ReadOnly)
        {
            _text.Append("   [").Append(Tr("ui.hud_read_only")).Append(']');
        }

        _metrics.Text = _text.ToString();
        _toolsLabel.Text = FormatTools(sample);
    }

    private string FormatTools(HudSample sample)
    {
        if ((sample.Present & HudFields.Tools) == 0)
        {
            return "—";
        }

        _text.Clear();
        _text.Append(string.Format(CultureInfo.InvariantCulture, Tr("ui.hud_tools"), sample.PendingIntents, sample.Rejections));
        var refusal = _tools?.Invoke()?.Pending?.LastRejection ?? RejectReason.None;
        if (refusal != RejectReason.None)
        {
            _text.Append('\n').Append(Tr(ToolHud.RejectKey(refusal)));
            if (HudRejectNotice.IsServerLimit(refusal))
            {
                _text.Append('\n').Append(Tr("ui.hud_limits_note"));
            }
        }

        return _text.ToString();
    }

    private void AppendField(HudFields present, HudFields field, string key, double value, string format)
    {
        _text.Append(Tr(key)).Append(' ');
        if ((present & field) != 0)
        {
            _text.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }
        else
        {
            _text.Append('—');
        }
    }
}
