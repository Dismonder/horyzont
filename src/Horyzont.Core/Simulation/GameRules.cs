using Horyzont.Core.Protocol;

namespace Horyzont.Core.Simulation;

/// <summary>
/// Gameplay limits. From the specification: 60 Hz, 8 m reach, 0.5–3 m tool radius, 5 terrain edits per second.
/// Assumptions to confirm: block rate, ray origin tolerance, capsule size and eye height (package 11), edit margin
/// (package 9). Constants of the operation semantics (edit margin, edit radius bound) are in <see cref="TerrainRules"/>, not here.
/// </summary>
public sealed record GameRules
{
    private readonly int _maxTimedStationsPerConstruct = ProtocolConstants.MaxTimedStationsPerConstruct;

    /// <summary>Simulation ticks per second.</summary>
    public int TickRate { get; init; } = 60;

    /// <summary>Reach of tools and block placement in metres.</summary>
    public float ReachMeters { get; init; } = 8f;

    /// <summary>Smallest terrain tool radius.</summary>
    public float MinToolRadius { get; init; } = 0.5f;

    /// <summary>Largest terrain tool radius.</summary>
    public float MaxToolRadius { get; init; } = 3f;

    /// <summary>Terrain edits refilled per second.</summary>
    public int TerrainEditsPerSecond { get; init; } = 5;

    /// <summary>Terrain edits available at once.</summary>
    public int TerrainEditBurst { get; init; } = 5;

    /// <summary>Block edits refilled per second.</summary>
    public int BlockEditsPerSecond { get; init; } = 10;

    /// <summary>Block edits available at once.</summary>
    public int BlockEditBurst { get; init; } = 10;

    /// <summary>Marker intents refilled per second (K5-27).</summary>
    public int MarkerIntentsPerSecond { get; init; } = 4;

    /// <summary>Marker intents available at once (K5-27).</summary>
    public int MarkerIntentBurst { get; init; } = 8;
    /// <summary>Item intents (move, drop) refilled per second (K6-29W D19).</summary>
    public int ItemIntentsPerSecond { get; init; } = 20;

    /// <summary>Item intents available at once (K6-29W D19).</summary>
    public int ItemIntentBurst { get; init; } = 40;

    /// <summary>Largest distance between an intent's ray origin and the player's eye.</summary>
    public float MaxRayOriginErrorMeters { get; init; } = 2f;

    /// <summary>Radius of the player capsule.</summary>
    public float PlayerCapsuleRadius { get; init; } = 0.4f;

    /// <summary>Height of the player capsule.</summary>
    public float PlayerCapsuleHeight { get; init; } = 1.8f;

    /// <summary>Eye height above the feet.</summary>
    public float EyeHeight { get; init; } = 1.62f;

    /// <summary>
    /// Highest terrain above the planet radius, metres. Core does not know the generator; the game sets this from the planet
    /// generator (K5-23). Ship blocks farther from the planet centre than radius + this + 2 m cannot be embedded in terrain.
    /// </summary>
    public float TerrainMaxReliefMeters { get; init; } = 300f;

    /// <summary>Largest distance from the centre of a refuel station cell to the bounding sphere of the ship, metres (K5-24 P3).</summary>
    public float RefuelRangeMeters { get; init; } = 15f;

    /// <summary>Most cells a door switch toggles together along the Y axis of a construct (K6-30 D6).</summary>
    public int MaxDoorColumnCells { get; init; } = 3;

    /// <summary>Largest eye distance to a chest for opening it (subscribing), metres (K6-30 D9).</summary>
    public float ContainerOpenRangeMeters { get; init; } = 6f;

    /// <summary>Largest eye distance at which an open chest stays open and takes item intents, metres (K6-30 D9).</summary>
    public float ContainerKeepRangeMeters { get; init; } = 8f;

    /// <summary>Largest eye distance to a chest that takes part in a quick stack, metres (K6-30 D9).</summary>
    public float QuickStackRangeMeters { get; init; } = 10f;

    /// <summary>Prop hits refilled per second (K6-31 D22).</summary>
    public int PropHitsPerSecond { get; init; } = 4;

    /// <summary>Prop hits available at once (K6-31 D22).</summary>
    public int PropHitBurst { get; init; } = 4;

    /// <summary>
    /// Anchor lookups per second that need the generator (a cache miss of a candidate that may be in reach) one player may cause with
    /// <c>PropHitIntent</c>, refused hits included (K6-31 review fix; a refused hit does not use a hit token). Twice
    /// <see cref="PropHitsPerSecond"/>: a legitimate player needs at most one lookup per hit (the first hit at a new prop; the prop is
    /// cached after it, refused or not, and digging warms the cache too), so the budget only has to cover the pace of the client and a
    /// burst after a network stall; every lookup beyond that is the main thread's cost of someone who is not playing.
    /// </summary>
    public int PropProbesPerSecond { get; init; } = 8;

    /// <summary>Anchor lookups that need the generator available at once: twice <see cref="PropHitBurst"/> (K6-31 review fix).</summary>
    public int PropProbeBurst { get; init; } = 8;

    /// <summary>Ticks without a hit after which a worn prop has its full hit points again (K6-31 D10).</summary>
    public int PropDamageResetTicks { get; init; } = 1800;

    /// <summary>Most worn props the server remembers; the oldest is forgotten first (K6-31 D10).</summary>
    public int MaxDamagedProps { get; init; } = 4096;

    /// <summary>Anchors the prop field keeps in its cache (K6-31 D7).</summary>
    public int PropAnchorCacheSize { get; init; } = 4096;

    /// <summary>Margin added to the radius of a terrain edit when it removes props whose anchor lies inside, metres (K6-31 D12).</summary>
    public float PropAnchorMarginMeters { get; init; } = 0.5f;

    /// <summary>Radius around an anchor that must be free of players and constructs before a prop regrows, metres (K6-31 D14).</summary>
    public float PropRegrowClearanceMeters { get; init; } = 20f;

    /// <summary>Ticks between scans of the harvested props for regrowth (K6-31 D14).</summary>
    public int PropRegrowScanEveryTicks { get; init; } = 60;

    /// <summary>Harvested props examined per scan (K6-31 D14).</summary>
    public int PropRegrowScanBatch { get; init; } = 256;

    /// <summary>Most props regrown by one operation (K6-31 D14).</summary>
    public int MaxRegrowPerOperation { get; init; } = 64;

    /// <summary>Distance from a player at which a prop region starts to be synchronised, metres (K6-31 D17).</summary>
    public float PropSyncEnterMeters { get; init; } = 512f;

    /// <summary>Distance from a player beyond which a prop region is dropped from synchronisation, metres (K6-31 D17).</summary>
    public float PropSyncLeaveMeters { get; init; } = 576f;
    /// <summary>Largest number of pieces in one <c>CraftIntent</c> (K6-32 D12).</summary>
    public int MaxCraftCount { get; init; } = 50;

    /// <summary>Radius around the station cell (the eye of the player for the hands) in which chests are ingredient sources, metres (K6-32 D13).</summary>
    public float CraftContainerRangeMeters { get; init; } = 20f;

    /// <summary>Largest eye distance to the centre of the cell of a station for crafting at it, metres (K6-32 D14).</summary>
    public float StationUseRangeMeters { get; init; } = 8f;

    /// <summary>Radius around a workbench in which upgrades count towards its level, metres (K6-32 D14).</summary>
    public float StationUpgradeRangeMeters { get; init; } = 5f;

    /// <summary>Ticks between settlements of the observed stations with time (K6-32 D16).</summary>
    public int StationSettleEveryTicks { get; init; } = 10;

    /// <summary>
    /// The intensity of the hazards of the world (K6-34 D9, DO USTALENIA-3): a factor of the rates at which the hazards drain the suit, not of the damage at zero. Used between 0.5 and 2; a value outside is
    /// clamped where it is used (<see cref="Horyzont.Core.Survival.HazardField"/>). K6-39 moves it to the settings of the world.
    /// </summary>
    public float HazardIntensity { get; init; } = 1f;

    /// <summary>
    /// Most stations with time (furnace, refinery) in one construct: the state of each is in message 46, whose size is limited to
    /// <c>ProtocolConstants.MaxReliableMessageBytes</c> (K6-32, review A1 note a). Placing one more is <c>InvalidParameters</c>. A value above
    /// <see cref="ProtocolConstants.MaxTimedStationsPerConstruct"/> is refused (review B1, 7): the limit protects the protocol format.
    /// </summary>
    public int MaxTimedStationsPerConstruct
    {
        get => _maxTimedStationsPerConstruct;
        init
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ProtocolConstants.MaxTimedStationsPerConstruct);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxTimedStationsPerConstruct = value;
        }
    }
}
