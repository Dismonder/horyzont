using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Horyzont.Client.Ships;
using Horyzont.Client.Tools;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Ships;

/// <summary>K5-25 decisions 18, 20 and 23: seat keys, the own seat, the seat view and the pilot loop on the client.</summary>
public sealed class SeatClientTests
{
    private static readonly Vector3 _origin = new(0, 4000, 0);
    private static readonly CellPos _cockpit = new(2, 2, 0);
    private static readonly EntityId _ownEntity = new(77);

    private static AimRay AimAt(CellPos cell)
    {
        var from = _origin + new Vector3(2.5f, 1.62f, -2f);
        var to = _origin + new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        return new AimRay(WorldFactory.HomePlanet, from, Vector3.Normalize(to - from), -Vector3.UnitZ, from - new Vector3(0, 1.62f, 0));
    }

    private static (SeatInteraction Seats, FakeSender Sender, OwnSeat Own) Create()
    {
        var state = ShipFixtures.CreateWorld(ShipFixtures.Thirty, _origin, ConstructMode.Ship);
        var sender = new FakeSender();
        var own = new OwnSeat();
        own.OnServerHello(new ServerHello(default, default, 60, new PlayerId(1), _ownEntity, [], [], []));
        var pending = new PendingIntentTracker(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance);
        return (new SeatInteraction(new View(state), sender, pending, ShipFixtures.ContentFor(state), own, new GameRules()), sender, own);
    }

    private static OperationApplied Reparent(EntityId entity, FrameRef parent, CellPos? seat) =>
        new(default, default, new EntityReparentOp(entity, parent, 1, Vector3.Zero, Quaternion.Identity, Vector3.Zero, seat), []);

    [Fact]
    public void SeatInteraction_AimedCockpit_SendsEnterWithTheEyeRay_SeatedSendsExit()
    {
        var (seats, sender, own) = Create();
        var aim = AimAt(_cockpit);

        Assert.Equal(SeatUseResult.Sent, seats.Use(aim, 3));
        Assert.Equal(new SeatEnterIntent(ShipFixtures.ConstructId, _cockpit, aim.Eye, aim.Direction), Assert.Single(sender.Intents));

        own.OnMessage(Reparent(_ownEntity, FrameRef.Of(ShipFixtures.ConstructId), _cockpit));
        Assert.True(seats.Seated);
        Assert.Equal(SeatUseResult.Sent, seats.Use(null, 4));
        Assert.Equal(new SeatExitIntent(ShipFixtures.ConstructId), sender.Intents[^1]);
        Assert.Equal(SeatUseResult.NoSeat, seats.Evict(aim, 5));
    }

    [Fact]
    public void SeatInteraction_NotASeatOrNothing_SendsNothing_EvictSends16()
    {
        var (seats, sender, _) = Create();

        Assert.Equal(SeatUseResult.NoSeat, seats.Use(AimAt(new CellPos(2, 1, 1)), 3));
        Assert.Equal(SeatUseResult.NoSeat, seats.Use(null, 3));
        Assert.Empty(sender.Intents);

        Assert.Equal(SeatUseResult.Sent, seats.Evict(AimAt(_cockpit), 4));
        Assert.IsType<SeatEvictIntent>(Assert.Single(sender.Intents));
    }

    [Fact]
    public void OwnSeat_FollowsOwnReparentsOnly()
    {
        var own = new OwnSeat();
        own.OnServerHello(new ServerHello(default, default, 60, new PlayerId(1), _ownEntity, [], [], []));

        own.OnMessage(Reparent(new EntityId(5), FrameRef.Of(ShipFixtures.ConstructId), _cockpit));
        Assert.False(own.TryGetOwnSeat(out _, out _));
        own.OnMessage(Reparent(_ownEntity, FrameRef.Of(ShipFixtures.ConstructId), _cockpit));
        Assert.True(own.TryGetOwnSeat(out var construct, out var cell));
        Assert.Equal((ShipFixtures.ConstructId, _cockpit), (construct, cell));
        own.OnMessage(Reparent(_ownEntity, FrameRef.Of(WorldFactory.HomePlanet), null));
        Assert.False(own.TryGetOwnSeat(out _, out _));
    }

    [Fact]
    public void SeatCamera_SeatedBodyAndCameraUseRenderedShipPose()
    {
        var rendered = new PoseD(new Vector3D(10, 4000, 3), QuaternionD.FromFloat(Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f)));
        var predicted = rendered with { Position = rendered.Position + new Vector3D(0, 0, 40) };
        var pilot = new FixedPose(ShipFixtures.ConstructId, predicted);
        var rules = SeatRules.Default;

        var pilotShip = SeatView.ShipPose(ShipFixtures.ConstructId, pilot, rendered);
        var passengerShip = SeatView.ShipPose(new EntityId(99), pilot, rendered);

        Assert.Equal(predicted, pilotShip);
        Assert.Equal(rendered, passengerShip);
        var body = SeatView.Body(pilotShip, _cockpit, 0, rules);
        var camera = SeatView.Camera(pilotShip, _cockpit, 0, rules, 0f, 0f);
        var expectedFeet = FrameMath.Compose(predicted, FrameMath.FromFloat(SeatPose.Feet(_cockpit, 0, rules), Quaternion.Identity)).Position;
        Assert.True(Vector3D.Distance(expectedFeet, body.Position) < 1e-6);
        Assert.InRange(Vector3D.Distance(body.Position, camera.Position), rules.SeatedEyeHeight - 1e-4, rules.SeatedEyeHeight + 1e-4);

        // The look is clamped: 3 rad of yaw is the same as 100°.
        var far = SeatView.Camera(pilotShip, _cockpit, 0, rules, 3f, 0f);
        var limit = SeatView.Camera(pilotShip, _cockpit, 0, rules, SeatView.MaxYaw, 0f);
        Assert.Equal(limit, far);
    }

    [Fact]
    public void ShipPilotInput_AxesAndToggles()
    {
        var input = new ShipPilotInput();

        var control = input.Build(1f, -1f, 1f, 0f, 12f, -6f, false, true, false);

        Assert.Equal(new ShipControl(127, -127, -127, 64, -127, 0, ShipButtons.Assist), control);
        Assert.Equal(ShipButtons.Assist | ShipButtons.PulseDrive, input.Build(0, 0, 0, 0, 0, 0, true, false, false).Buttons);
        Assert.Equal(ShipButtons.PulseDrive, input.Build(0, 0, 0, float.NaN, 0, 0, false, false, true).Buttons);
    }

    [Fact]
    public void ShipPilot_FirstCorrectionStarts_ThenPredictsAndSendsEveryTick()
    {
        var stats = FlightFixtures.Thirty();
        var prediction = new ShipPrediction(
            ClientFlightEnvironment.InPlanet(FlightFixtures.Home, 0, FlightRules.Default), (in PoseD _, in PoseD _) => ShipSweepResult.Free,
            FlightRules.Default, 60, NullLogger.Instance);
        var sent = new List<(uint First, int Count)>();
        var pilot = new ShipPilot(prediction, (first, inputs) => sent.Add((first, inputs.Count)));
        var climb = new ShipControl(0, 127, 0, 0, 0, 0, ShipButtons.Assist);

        pilot.Tick(1, climb);
        Assert.Empty(sent);
        pilot.Begin(ShipFixtures.ConstructId, stats);
        pilot.Tick(1, climb);
        Assert.False(prediction.HasState);
        Assert.False(pilot.PoseOverride.TryGetPose(ShipFixtures.ConstructId, out _));
        var start = FlightFixtures.AboveHome(80);
        FrameMath.TryToFloat(start.Pose, FrameKind.Planet, out var position, out var rotation);
        pilot.OnCorrection(new ShipCorrection(
            ShipFixtures.ConstructId, 1, 0, position, rotation, Vector3.Zero, Vector3.Zero, start.Fuel, start.Flags, ShipTerrainGate.Ready,
            ShipLaunchBlock.None, float.PositiveInfinity));
        pilot.Tick(2, climb);
        pilot.Tick(3, climb);

        Assert.True(prediction.HasState);
        Assert.True(prediction.Motion.LinearVelocity.Y > 0);
        Assert.True(pilot.PoseOverride.TryGetPose(ShipFixtures.ConstructId, out _));
        Assert.Equal([(1u, 1), (1u, 2), (1u, 3)], sent);

        pilot.End();
        Assert.False(prediction.HasState);
        Assert.False(pilot.PoseOverride.TryGetPose(ShipFixtures.ConstructId, out _));
    }

    private sealed class FixedPose(EntityId ship, PoseD pose) : IClientShipPoseOverride
    {
        public bool TryGetPose(EntityId id, out PoseD value)
        {
            value = pose;
            return id == ship;
        }
    }

    private sealed class FakeSender : IIntentSender
    {
        private uint _seq = 10;

        public bool CanSend => true;

        public List<Intent> Intents { get; } = [];

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }

    private sealed class View(WorldState state) : IToolWorldView
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
}
