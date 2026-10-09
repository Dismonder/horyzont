using Godot;

using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Constructs.Sources;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Constructs;

/// <summary>
/// Wires the constructs of K2-12 into a process that runs a server (K2-13 step 0, gate decision D5): the block content, the
/// <see cref="ConstructWorld"/> over <c>ServerRuntime.State</c> (one instance, shared by the server and the local client of a solo or
/// host process, no second copy of the state), the construct changes of every applied operation, the viewers from the online players
/// and the shutdown (step 7a of the canonical order, after the terrain and before the transport). A dedicated server builds collision
/// bodies only.
/// </summary>
public sealed class HostConstructs : IServerTickParticipant, IDisposable
{
    /// <summary>Ticks between two updates of the viewers (the world also refreshes its ranges on its own every 30 frames).</summary>
    public const int ViewerIntervalTicks = 30;

    private const int MaxViewers = 64;

    private readonly ILoggerFactory _loggers;
    private readonly ILogger<HostConstructs> _logger;
    private readonly bool _render;
    private readonly System.Numerics.Vector3[] _viewers = new System.Numerics.Vector3[MaxViewers];
    private ServerRuntime? _runtime;
    private BlockVisuals? _visuals;
    private int _tick;

    /// <summary>Initializes a new instance of the <see cref="HostConstructs"/> class.</summary>
    /// <param name="parent">Node the construct world is added to (<c>/Root/Server</c>).</param>
    /// <param name="catalog">The block content, loaded at start.</param>
    /// <param name="render">Whether meshes are drawn (false: collision bodies only).</param>
    /// <param name="loggers">Loggers.</param>
    public HostConstructs(Node parent, BlockTypeCatalog catalog, bool render, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loggers);
        Catalog = catalog;
        _render = render;
        _loggers = loggers;
        _logger = loggers.CreateLogger<HostConstructs>();
        World = new ConstructWorld { Name = "Constructs" };
        parent.AddChild(World);
    }

    /// <summary>Gets the block content.</summary>
    public BlockTypeCatalog Catalog { get; }

    /// <summary>Gets the construct world; usable after <see cref="Attach"/>.</summary>
    public ConstructWorld World { get; }

    /// <summary>Gets a value indicating whether <see cref="Attach"/> ran.</summary>
    public bool IsAttached => _runtime is not null;

    /// <summary>Registers the shutdown step and the tick participant; call after the runtime is created and before it starts.</summary>
    /// <param name="runtime">The server runtime.</param>
    public void Register(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.AddTickParticipant(this);
        runtime.AddAfterTerrainShutdown(World.ShutdownAsync);
    }

    /// <summary>Connects the world to the loaded state and shows the constructs of the home planet; call once, after the server started.</summary>
    /// <param name="runtime">The running server runtime.</param>
    public void Attach(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (_runtime is not null)
        {
            throw new InvalidOperationException("The constructs are already attached.");
        }

        var state = runtime.State;
        var table = BlockRenderTable.Build(state.BlockTypes.Entries, Catalog);
        _visuals = _render ? BlockVisuals.Create(Catalog) : null;
        var options = new ConstructWorldOptions { Mode = _render ? ConstructWorldMode.RenderAndCollision : ConstructWorldMode.CollisionOnly };
        World.Initialize(new WorldStateConstructSource(state), table, _visuals, options, _loggers.CreateLogger<ConstructWorld>());
        runtime.Simulation.Applied += OnApplied;
        _runtime = runtime;
        if (state.Planets.All.Count > 0)
        {
            World.RebuildAll(FrameRef.Of(state.Planets.All[0].Id));
        }
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        if (_runtime is null || _tick++ % ViewerIntervalTicks != 0)
        {
            return;
        }

        var state = runtime.State;
        var count = 0;
        foreach (var player in state.Players.Online)
        {
            if (count < MaxViewers && state.TryToPlanetFrame(player.Frame, player.Position, out _, out var position))
            {
                _viewers[count++] = position;
            }
        }

        World.SetViewers(_viewers.AsSpan(0, count));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_runtime is { } runtime)
        {
            runtime.Simulation.Applied -= OnApplied;
            _runtime = null;
        }

        _visuals?.Dispose();
        _visuals = null;
    }

    private void OnApplied(AppliedOperation applied)
    {
#pragma warning disable CA1031 // The operation is already applied and journaled: a failure of the view must not fail the simulation; it is logged and the view resyncs on the next rebuild.
        try
        {
            World.OnApplied(applied.Operation, applied.ConstructChanges);
        }
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogViewUpdateFailed(applied.Id.Value, ex);
        }
    }
}

internal static partial class HostConstructsLog
{
    [LoggerMessage(EventId = 9301, Level = LogLevel.Error, Message = "the construct view failed to apply operation {OpId}")]
    internal static partial void LogViewUpdateFailed(this ILogger logger, ulong opId, Exception exception);
}
