using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Ships;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Ships;

public sealed class ShipModeSwitchTests
{
    private static readonly Vector3 _origin = new(0, 4000, 0);
    private static readonly CellPos _cockpit = new(2, 2, 0);

    private sealed class FakeSender : IIntentSender
    {
        private uint _seq = 10;

        public bool CanSend { get; set; } = true;

        public List<Intent> Intents { get; } = [];

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }

    /// <summary>The constructs of a world state, a sphere for the terrain.</summary>
    private sealed class ShipWorldView(WorldState state) : IToolWorldView
    {
        public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters) => state.Planets.TryGet(planet, out parameters);

        public bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit) =>
            new FakeTerrainService().Raycast(planet, origin, direction, maxDistance, out hit);

        public bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit) =>
            ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(planet), origin, direction, maxDistance, out hit);

        public bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value) => state.Constructs.TryGet(construct, out value);

        public bool TryGetConstructPose(EntityId construct, out Vector3 origin, out Quaternion rotation)
        {
            origin = default;
            rotation = Quaternion.Identity;
            return false;
        }

        public bool OverlapsConstruct(PlanetId planet, Vector3 center, Quaternion rotation, EntityId? skip) => false;

        public int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination) => 0;
    }

    private sealed class Rig
    {
        public Rig(ConstructMode mode = ConstructMode.Static)
        {
            State = ShipFixtures.CreateWorld(ShipFixtures.Thirty, _origin, mode);
            Pending = new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance);
            Switch = new ShipModeSwitch(new ShipWorldView(State), Sender, Pending, ShipFixtures.ContentFor(State), new GameRules());
        }

        public WorldState State { get; }

        public FakeSender Sender { get; } = new();

        public PendingIntentTracker Pending { get; }

        public ShipModeSwitch Switch { get; }

        public static AimRay AimAt(CellPos cell, Vector3? eye = null)
        {
            var from = eye ?? (_origin + new Vector3(2.5f, 1.62f, -2f));
            var to = _origin + new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
            return new AimRay(WorldFactory.HomePlanet, from, Vector3.Normalize(to - from), -Vector3.UnitZ, from - new Vector3(0, 1.62f, 0));
        }
    }

    [Fact]
    public void Toggle_OnCockpit_SendsIntentForTheOppositeModeWithTheEyeRay()
    {
        var rig = new Rig();
        var aim = Rig.AimAt(_cockpit);

        var result = rig.Switch.Toggle(aim, 5);

        Assert.Equal(ShipModeToggleResult.Sent, result);
        var intent = Assert.IsType<ShipModeIntent>(Assert.Single(rig.Sender.Intents));
        Assert.Equal(new ShipModeIntent(ShipFixtures.ConstructId, _cockpit, ConstructMode.Ship, aim.Eye, aim.Direction), intent);
        Assert.Equal(1, rig.Pending.Count);
    }

    [Fact]
    public void Toggle_OnCockpitOfAShip_AsksForStatic()
    {
        var rig = new Rig(ConstructMode.Ship);

        Assert.Equal(ShipModeToggleResult.Sent, rig.Switch.Toggle(Rig.AimAt(_cockpit), 5));

        Assert.Equal(ConstructMode.Static, Assert.IsType<ShipModeIntent>(Assert.Single(rig.Sender.Intents)).TargetMode);
    }

    [Fact]
    public void Toggle_NotACockpitOrNothingAimed_SendsNothing()
    {
        var rig = new Rig();

        Assert.Equal(ShipModeToggleResult.NotACockpit, rig.Switch.Toggle(Rig.AimAt(new CellPos(2, 1, 1)), 5));
        var sky = Rig.AimAt(_cockpit) with { Direction = Vector3.UnitY };
        Assert.Equal(ShipModeToggleResult.NoConstruct, rig.Switch.Toggle(sky, 5));
        Assert.Empty(rig.Sender.Intents);
        Assert.Equal(0, rig.Pending.Count);
    }

    [Fact]
    public void Toggle_SessionNotInGameOrTooManyPending_SendsNothing()
    {
        var rig = new Rig();
        rig.Sender.CanSend = false;
        Assert.Equal(ShipModeToggleResult.CannotSend, rig.Switch.Toggle(Rig.AimAt(_cockpit), 5));
        rig.Sender.CanSend = true;

        for (var i = 0; i < PendingIntentTracker.MaxPending; i++)
        {
            rig.Pending.Add((uint)(100 + i), new RemoveBlockIntent(ShipFixtures.ConstructId, new CellPos(0, 0, 0)), default, 5);
        }

        Assert.Equal(ShipModeToggleResult.Waiting, rig.Switch.Toggle(Rig.AimAt(_cockpit), 5));
        Assert.Empty(rig.Sender.Intents);
    }

    [Fact]
    public void Observe_CockpitConstruct_ReportsModeAndStatsFromTheClientContent()
    {
        var rig = new Rig();

        rig.Switch.Observe(Rig.AimAt(new CellPos(2, 1, 1)));

        var aimed = rig.Switch.Aimed;
        Assert.NotNull(aimed);
        Assert.Equal(ConstructMode.Static, aimed.Mode);
        Assert.Equal(8210f, aimed.Stats.MassKg);
        Assert.Equal(120000f, aimed.Stats.Thrust.PosY);
        Assert.Equal(4, aimed.Stats.Seats.Count);

        rig.Switch.Observe(Rig.AimAt(_cockpit));
        Assert.Same(aimed.Stats, rig.Switch.Aimed!.Stats);

        rig.Switch.Observe(Rig.AimAt(_cockpit) with { Direction = Vector3.UnitY });
        Assert.Null(rig.Switch.Aimed);
    }

    [Fact]
    public void ToolInputFrame_ShipModeToggle_DefaultsToFalse()
    {
        var frame = new ToolInputFrame(false, false, false, false, null, 0, 0, 0, 0, 0);

        Assert.False(frame.ShipModeToggle);
    }
}
