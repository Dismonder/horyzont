using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Movement;

/// <summary>Real input/snapshot codecs and unreliable transport, with ideal sphere collision instead of Godot.</summary>
public sealed class MotionLinkTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(14, 50, 0, 0)]
    [InlineData(140, 50, 0, 0)]
    [InlineData(1400, 50, 0, 0)]
    [InlineData(14, 50, 10, 0.01)]
    [InlineData(140, 50, 10, 0.01)]
    [InlineData(1400, 50, 10, 0.01)]
    [InlineData(14, 100, 10, 0.01)]
    [InlineData(140, 100, 10, 0.01)]
    [InlineData(1400, 100, 10, 0.01)]
    public void Move_Rtt100msAndFivePercentLoss_ConvergesWithoutCorrectionJumps(int seed, int latencyMs, int jitterMs, double duplication)
    {
        using var link = new Fixture(new LinkConditions(TimeSpan.FromMilliseconds(latencyMs), TimeSpan.FromMilliseconds(jitterMs), 0.05, duplication, seed));
        for (uint tick = 1; tick <= 3900; tick++)
        {
            link.Step(tick, tick <= 3600);
        }

        output.WriteLine($"seed={seed} RTT={latencyMs * 2} jitter={jitterMs} dup={duplication} snapshots={link.Delivered} corrections={link.Corrections} snaps={link.Snaps} max_jump={link.MaxCorrectionJump:F6} max_offset={link.MaxOffset:F6} underruns={link.Remote.SampleUnderruns} final_error={Vector3.Distance(link.Client.Feet, link.Server.Feet):F6} convergence_snapshots={link.ConvergenceSnapshots}");
        Assert.True(link.Delivered > 900);
        Assert.True(link.Converged);
        Assert.InRange(link.ConvergenceSnapshots, 0, 2);
        Assert.True(Vector3.Distance(link.Client.Feet, link.Server.Feet) < 0.01f);
        Assert.True(link.MaxCorrectionJump <= 0.02f);
        Assert.Equal(0, link.UnjustifiedSnaps);
    }

    [Fact]
    public void Host_PerfectLink_HasNoSpuriousCorrections()
    {
        using var link = new Fixture(LinkConditions.Perfect);
        for (uint tick = 1; tick <= 3600; tick++) { link.Step(tick, true); }
        Assert.Equal(0, link.Corrections);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly PlanetParams Planet = new(new PlanetId(1), new SystemId(1), 14, "core:planet/1");
        private static readonly MovementRules Rules = new();
        private readonly FakeTimeProvider _time = new();
        private readonly InMemoryTransport _serverTransport;
        private readonly InMemoryTransport _clientTransport;
        private readonly Receiver _serverReceiver;
        private readonly Receiver _clientReceiver;
        private readonly SortedDictionary<uint, PlayerInput> _queued = [];
        private readonly InputSequencer _sequencer = new();
        private readonly PredictionBuffer _history = new(new CorrectionPolicy(), NullLogger<PredictionBuffer>.Instance);
        private readonly PresentationCorrection _presentation = new();
        private readonly OwnStateValidator _validator;
        private readonly PredictionEntry[] _replay = new PredictionEntry[128];
        private readonly PredictedState[] _states = new PredictedState[128];
        private readonly ByteWriter _writer = new();
        private uint _lastSeq;
        private uint _snapshotId;
        private uint _tick;
        private int _starved;
        private bool _stopped;

        public Fixture(LinkConditions conditions)
        {
            var network = new InMemoryNetwork(_time, conditions);
            _serverTransport = network.CreateTransport("server", TransportOptions.Default);
            _clientTransport = network.CreateTransport("client", TransportOptions.Default);
            _serverReceiver = new Receiver(OnInputs);
            _clientReceiver = new Receiver(OnSnapshot);
            _serverTransport.Listen(new InMemoryEndpoint("server"));
            _clientTransport.Connect(new InMemoryEndpoint("server"));
            for (var i = 0; i < 40 && !_clientReceiver.Connected; i++)
            {
                _time.Advance(TimeSpan.FromMilliseconds(20));
                _serverTransport.Poll(_serverReceiver);
                _clientTransport.Poll(_clientReceiver);
            }

            Assert.True(_clientReceiver.Connected);
            var initial = new Vector3(0, Planet.RadiusMeters, 0);
            Client = Server = new PredictedState(initial, Vector3.Zero, Quaternion.Identity, 0, MovementMode.Walking, true);
            var hello = new ServerHello(default, new Tick(0), 60, new PlayerId(1), new EntityId(1), [Planet], [], []);
            _validator = OwnStateValidator.FromHello(hello, NullLogger<OwnStateValidator>.Instance);
        }

        public PredictedState Client { get; private set; }
        public PredictedState Server { get; private set; }
        public RemoteMotionBuffer Remote { get; } = new();
        public int Delivered { get; private set; }
        public int Corrections { get; private set; }
        public int Snaps { get; private set; }
        public int UnjustifiedSnaps { get; private set; }
        public float MaxCorrectionJump { get; private set; }
        public float MaxOffset { get; private set; }
        public int ConvergenceSnapshots { get; private set; }
        public bool Converged { get; private set; }

        public void Step(uint tick, bool moving)
        {
            _tick = tick;
            _serverTransport.Poll(_serverReceiver);
            var input = MotorInput.Neutral(0, 0);
            if (_queued.Count > 0)
            {
                using var entries = _queued.GetEnumerator();
                Assert.True(entries.MoveNext());
                var entry = entries.Current;
                _lastSeq = entry.Key;
                _queued.Remove(entry.Key);
                Assert.True(MotorInput.TryFrom(entry.Value, Rules, out input));
                _starved = 0;
                Server = Integrate(Server, input);
            }
            else if (++_starved > Rules.MaxStarvedTicks)
            {
                Server = Integrate(Server, input);
            }

            if (tick % 3 == 0)
            {
                Send(_serverTransport, _serverReceiver.Connection, new Snapshot(++_snapshotId, new Tick(tick), 0, _lastSeq,
                    [new SnapshotFrame(FrameRef.Of(Planet.Id), 0)],
                    [new EntityState(new EntityId(1), (EntityStateFields)15, 0, 0, Server.Feet, Server.BodyRotation, Server.Velocity, (byte)Server.Mode)]));
            }

            _clientTransport.Poll(_clientReceiver);
            var buttons = moving && tick % 600 >= 300 ? InputButtons.Sprint : InputButtons.None;
            if (moving && tick % 240 == 0) { buttons |= InputButtons.Jump; }
            var wire = new PlayerInput(tick, 0, moving ? 1 : 0, 0, 0, buttons);
            Assert.True(MotorInput.TryFrom(wire, Rules, out var local));
            var seq = _sequencer.Record(tick, local);
            _validator.NoteSent(seq);
            if (!_history.IsPaused)
            {
                Client = Integrate(Client, local);
                _history.Record(seq, local, Client);
            }

            while (_sequencer.TryTakeBatch(out var first, out var inputs))
            {
                Send(_clientTransport, _clientReceiver.Connection, new InputBatch(first, inputs));
            }

            _presentation.Advance(1f / 60);
            Remote.TrySample(tick - 6, out _);
            _time.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
        }

        private void OnInputs(Message message)
        {
            var batch = Assert.IsType<InputBatch>(message);
            for (var i = 0; i < batch.Inputs.Count; i++)
            {
                var seq = batch.FirstInputSeq + (uint)i;
                if (seq > _lastSeq) { _queued.TryAdd(seq, batch.Inputs[i]); }
            }
        }

        private void OnSnapshot(Message message)
        {
            var snapshot = Assert.IsType<Snapshot>(message);
            if (_validator.Validate(snapshot, out var own) != OwnStateVerdict.Accepted) { return; }
            Delivered++;
            var server = own.ToPredicted(0, own.Mode is MovementMode.Walking or MovementMode.Sprinting);
            Remote.Push(new RemoteMotionSample(new EntityId(2), FrameRef.Of(Planet.Id), 0, snapshot.ServerTick,
                server.Feet, server.BodyRotation, server.Velocity, (byte)server.Mode));
            var before = Client;
            var visible = before.Feet + _presentation.PositionOffset;
            var historyCount = _history.CopyUnacknowledged(_replay);
            var found = false;
            for (var i = 0; i < historyCount; i++) { found |= _replay[i].Seq == own.LastProcessedInputSeq; }
            if (_history.Reconcile(own, 0, server.OnFloor) == AckOutcome.Correct)
            {
                Corrections++;
                Client = server;
                var count = _history.CopyUnacknowledged(_replay);
                for (var i = 0; i < count; i++) { Client = Integrate(Client, _replay[i].Input); _states[i] = Client; }
                _history.ReplaceUnacknowledgedStates(_states.AsSpan(0, count));
                _presentation.Reconcile(before, Client, !found);
                if (_presentation.Snapped)
                {
                    Snaps++;
                    if (found && Vector3.Distance(before.Feet, Client.Feet) < 1f
                        && MathF.Abs(Quaternion.Dot(before.BodyRotation, Client.BodyRotation)) > MathF.Cos(MathF.PI / 8)) { UnjustifiedSnaps++; }
                }
                else
                {
                    MaxCorrectionJump = MathF.Max(MaxCorrectionJump, Vector3.Distance(visible, Client.Feet + _presentation.PositionOffset));
                    MaxOffset = MathF.Max(MaxOffset, _presentation.PositionOffset.Length());
                }
            }

            if (_tick > 3630 && server.Velocity.Length() < 0.001f)
            {
                _stopped = true;
            }
            if (_stopped && !Converged)
            {
                ConvergenceSnapshots++;
                Converged = Vector3.Distance(Client.Feet, server.Feet) < 0.01f;
            }
        }

        private void Send(InMemoryTransport transport, ConnectionId connection, Message message)
        {
            _writer.Reset();
            MessageCodec.Write(_writer, message);
            transport.Send(connection, TransportChannel.Unreliable, _writer.WrittenSpan);
        }

        private static PredictedState Integrate(in PredictedState state, in MotorInput input)
        {
            var result = CharacterMotor.Step(input, new MotorContext(state.Feet, state.Velocity, state.OnFloor, state.Mode, Planet, false), Rules, 1f / 60);
            var feet = state.Feet + result.DesiredVelocity / 60f;
            var velocity = result.DesiredVelocity;
            var floor = feet.Length() <= Planet.RadiusMeters;
            if (floor)
            {
                var up = Vector3.Normalize(feet);
                feet = up * Planet.RadiusMeters;
                velocity -= up * Vector3.Dot(velocity, up);
            }
            return new PredictedState(feet, velocity, result.BodyRotation, 0, result.Mode, floor);
        }

        public void Dispose() { _clientTransport.Dispose(); _serverTransport.Dispose(); }
    }

    private sealed class Receiver(Action<Message> received) : ITransportEvents
    {
        public ConnectionId Connection { get; private set; }
        public bool Connected { get; private set; }
        public void OnConnected(ConnectionId connection) { Connection = connection; Connected = true; }
        public void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason) { Connected = false; }
        public void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload) => received(MessageCodec.Read(payload));
    }
}
