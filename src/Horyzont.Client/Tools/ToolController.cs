using System.Numerics;

using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Tools;

/// <summary>
/// The tools of the local player, independent of the engine: settings, targeting, preview with hints, pacing and sending of intents.
/// One call of <see cref="Update"/> per client physics tick. The same class drives players and bots (K4-19). Aiming and the preview
/// do not allocate in steady state; an intent is allocated only when it is sent.
/// </summary>
public sealed class ToolController
{
    private const float DirectionTolerance = 0.01f;
    private const float DefaultRadius = 1f;

    private readonly GameRules _rules;
    private readonly IToolWorldView _world;
    private readonly IIntentSender _sender;
    private readonly PendingIntentTracker _pending;
    private readonly BlockPalette _blocks;
    private readonly MaterialPalette _materials;
    private readonly ILogger<ToolController> _logger;
    private readonly IntentPacer _pacer;
    private readonly ToolPreviewBuilder _previews;
    private ToolTarget _target;

    /// <summary>Initializes a new instance of the <see cref="ToolController"/> class.</summary>
    /// <param name="rules">Game rules (the defaults of the same build as the server).</param>
    /// <param name="world">World view for targeting and hints.</param>
    /// <param name="sender">Where intents go.</param>
    /// <param name="pending">Tracker of the intents that wait for the server.</param>
    /// <param name="blocks">Block palette.</param>
    /// <param name="materials">Material palette.</param>
    /// <param name="logger">Logger.</param>
    public ToolController(
        GameRules rules, IToolWorldView world, IIntentSender sender, PendingIntentTracker pending, BlockPalette blocks, MaterialPalette materials,
        ILogger<ToolController> logger)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pacer = new IntentPacer(rules);
        _previews = new ToolPreviewBuilder(rules, world);
        Settings = new ToolSettings(
            ToolKind.TerrainManipulator, Math.Clamp(DefaultRadius, rules.MinToolRadius, rules.MaxToolRadius), materials.First(), blocks.First(),
            BlockOrientation.Identity);
    }

    /// <summary>Gets the current tool and its parameters.</summary>
    public ToolSettings Settings { get; private set; }

    /// <summary>Gets the preview of the latest update.</summary>
    public ToolPreview Preview { get; private set; }

    /// <summary>Gets the tracker of the intents that wait for the server (the reply of the session goes to it).</summary>
    public PendingIntentTracker Pending => _pending;

    /// <summary>Gets a value indicating whether the latest action was refused because too many intents wait for the server (HUD).</summary>
    public bool WaitingForServer { get; private set; }

    /// <summary>One physics tick: applies setting steps, targets, builds the preview, sends at most one intent.</summary>
    /// <param name="input">Input since the previous tick.</param>
    /// <param name="aim">Aim of the local player.</param>
    /// <param name="clientTick">Client physics tick, not decreasing.</param>
    public void Update(in ToolInputFrame input, in AimRay aim, long clientTick)
    {
        _pending.Tick(clientTick);
        ApplySettingSteps(input);
        WaitingForServer = false;
        if (!IsUsable(aim))
        {
            Preview = default;
            return;
        }

        var primary = input.PrimaryPressed || input.PrimaryHeld;
        var secondary = input.SecondaryPressed || input.SecondaryHeld;
        if (Settings.Tool == ToolKind.TerrainManipulator)
        {
            Preview = _previews.ForTerrainTool(aim, Settings, fill: secondary && !primary);
            if (Preview.Kind == ToolPreviewKind.Sphere && (primary || secondary))
            {
                var repeat = primary ? !input.PrimaryPressed : !input.SecondaryPressed;
                if (Begin(PaceClass.Terrain, repeat, clientTick))
                {
                    Send(
                        new TerrainToolIntent(
                            aim.Planet, aim.Eye, aim.Direction, Settings.Radius, primary ? TerrainEditMode.Remove : TerrainEditMode.Add,
                            primary ? (byte)0 : Settings.FillMaterial),
                        clientTick);
                }
            }

            return;
        }

        _target = ToolTargeting.TryTarget(aim, _world, _rules, out var target) ? target : default;
        Preview = _previews.ForBuilder(aim, Settings, _target);
        if (primary && _target.Kind == ToolTargetKind.ConstructCell)
        {
            if (Begin(PaceClass.Block, !input.PrimaryPressed, clientTick))
            {
                Send(new RemoveBlockIntent(_target.Construct, _target.Cell), clientTick);
            }
        }
        else if (!primary && secondary && Preview.Kind != ToolPreviewKind.None && Begin(PaceClass.Block, !input.SecondaryPressed, clientTick))
        {
            BlockTarget place = _target.Kind == ToolTargetKind.ConstructCell
                ? new ConstructFaceTarget(_target.Construct, _target.Cell, _target.Face)
                : new TerrainTarget(aim.Planet, aim.Eye, aim.Direction, aim.LookForward);
            Send(new PlaceBlockIntent(place, Settings.BlockType, Settings.Orientation), clientTick);
        }
    }

    /// <summary>The aim is not available this tick (no body, or <see cref="AimRayBuilder.TryBuild"/> refused): clears the preview.</summary>
    /// <param name="clientTick">Client physics tick.</param>
    public void ClearAim(long clientTick)
    {
        _pending.Tick(clientTick);
        WaitingForServer = false;
        Preview = default;
    }

    private static bool IsUsable(in AimRay aim) =>
        IsFinite(aim.Eye) && IsFinite(aim.Feet) && IsDirection(aim.Direction) && IsDirection(aim.LookForward);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool IsDirection(Vector3 v) => IsFinite(v) && MathF.Abs(v.Length() - 1f) <= DirectionTolerance;

    private void ApplySettingSteps(in ToolInputFrame input)
    {
        var settings = Settings;
        if (input.SelectTool is { } tool && Enum.IsDefined(tool))
        {
            settings = settings with { Tool = tool };
        }

        if (input.RadiusSteps != 0)
        {
            settings = settings.WithRadiusSteps(input.RadiusSteps, _rules);
        }

        if (input.PaletteSteps != 0)
        {
            settings = settings with { BlockType = _blocks.Step(settings.BlockType, input.PaletteSteps) };
        }

        if (input.MaterialSteps != 0)
        {
            settings = settings with { FillMaterial = _materials.Step(settings.FillMaterial, input.MaterialSteps) };
        }

        if (input.YawSteps != 0)
        {
            settings = settings with { Orientation = OrientationPicker.Yaw(settings.Orientation, input.YawSteps) };
        }

        if (input.UpSteps != 0)
        {
            settings = settings with { Orientation = OrientationPicker.Up(settings.Orientation, input.UpSteps) };
        }

        Settings = settings;
    }

    /// <summary>Checks that an intent can go out now and takes its place in the rate mirror.</summary>
    private bool Begin(PaceClass paceClass, bool repeat, long clientTick)
    {
        if (!_sender.CanSend)
        {
            return false;
        }

        if (_pending.IsFull)
        {
            WaitingForServer = true;
            _logger.TooManyPending(PendingIntentTracker.MaxPending);
            return false;
        }

        return _pacer.TryAcquire(paceClass, repeat, clientTick);
    }

    private void Send(Intent intent, long clientTick)
    {
        var seq = _sender.Submit(intent);
        _pending.Add(seq, intent, Preview, clientTick);
        _logger.IntentSent(seq, intent);
    }
}
