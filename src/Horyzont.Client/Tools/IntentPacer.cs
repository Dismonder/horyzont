using Horyzont.Core.Simulation;

namespace Horyzont.Client.Tools;

/// <summary>
/// Mirror of the server rate limits on client ticks: terrain edits pass a token bucket and the sliding window of one second
/// (at most <see cref="GameRules.TerrainEditsPerSecond"/> in every window), block edits a token bucket. A held button repeats
/// every <see cref="TerrainRepeatTicks"/> or <see cref="BlockRepeatTicks"/> ticks, which leaves room for jitter between the client
/// and the server ticks. The client never sends an intent its mirror would refuse; the server enforces the limits anyway.
/// </summary>
public sealed class IntentPacer
{
    /// <summary>Ticks between repeats of a held terrain action (about 4.6 per second).</summary>
    public const int TerrainRepeatTicks = 13;

    /// <summary>Ticks between repeats of a held block action (4 per second).</summary>
    public const int BlockRepeatTicks = 15;

    /// <summary>Ticks between repeats of a held prop hit (4 per second, the rate of the server).</summary>
    public const int PropRepeatTicks = 15;

    private const int MaxCatchUpTicks = 1200;

    private readonly TokenBucket _terrain;
    private readonly TokenBucket _blocks;
    private readonly TokenBucket _props;
    private readonly SlidingWindowLimiter _window;
    private long _lastTick;
    private long _lastTerrainSend = long.MinValue / 2;
    private long _lastBlockSend = long.MinValue / 2;
    private long _lastPropSend = long.MinValue / 2;
    private bool _started;

    /// <summary>Initializes a new instance of the <see cref="IntentPacer"/> class.</summary>
    /// <param name="rules">Game rules (the same defaults as the server build).</param>
    public IntentPacer(GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _terrain = new TokenBucket(rules.TerrainEditsPerSecond, rules.TerrainEditBurst, rules.TickRate);
        _blocks = new TokenBucket(rules.BlockEditsPerSecond, rules.BlockEditBurst, rules.TickRate);
        _props = new TokenBucket(rules.PropHitsPerSecond, rules.PropHitBurst, rules.TickRate);
        _window = new SlidingWindowLimiter(Math.Max(1, rules.TerrainEditsPerSecond), rules.TickRate);
    }

    /// <summary>Asks to send one intent of a class at a client tick; a granted request is consumed.</summary>
    /// <param name="paceClass">Rate class.</param>
    /// <param name="repeat">True for a held button (waits for the repeat interval), false for a fresh press.</param>
    /// <param name="clientTick">Client physics tick, not decreasing.</param>
    /// <returns>True when the intent may be sent now.</returns>
    public bool TryAcquire(PaceClass paceClass, bool repeat, long clientTick)
    {
        Advance(clientTick);
        var terrain = paceClass == PaceClass.Terrain;
        var last = paceClass switch
        {
            PaceClass.Terrain => _lastTerrainSend,
            PaceClass.Prop => _lastPropSend,
            _ => _lastBlockSend,
        };
        var interval = paceClass switch
        {
            PaceClass.Terrain => TerrainRepeatTicks,
            PaceClass.Prop => PropRepeatTicks,
            _ => BlockRepeatTicks,
        };
        if (repeat && clientTick - last < interval)
        {
            return false;
        }

        var bucket = paceClass switch
        {
            PaceClass.Terrain => _terrain,
            PaceClass.Prop => _props,
            _ => _blocks,
        };
        if (!bucket.HasToken || (terrain && !_window.CanAccept((ulong)clientTick)))
        {
            return false;
        }

        bucket.Consume();
        if (terrain)
        {
            _window.Record((ulong)clientTick);
            _lastTerrainSend = clientTick;
        }
        else if (paceClass == PaceClass.Prop)
        {
            _lastPropSend = clientTick;
        }
        else
        {
            _lastBlockSend = clientTick;
        }

        return true;
    }

    private void Advance(long clientTick)
    {
        if (_started && clientTick > _lastTick)
        {
            var ticks = (int)Math.Min(clientTick - _lastTick, MaxCatchUpTicks);
            for (var i = 0; i < ticks; i++)
            {
                _terrain.Refill();
                _blocks.Refill();
                _props.Refill();
            }
        }

        _started = true;
        _lastTick = Math.Max(_lastTick, clientTick);
    }
}
