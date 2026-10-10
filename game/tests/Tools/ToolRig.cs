using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Game.Boot;
using Horyzont.Game.Client.Character;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Tests.Support;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Tools;

/// <summary>
/// A solo game with characters and tools (the K2-13 scenarios): the player stands on the terrain looking down at the ground ahead,
/// the tool input is scripted, and replies and server applications are recorded.
/// </summary>
internal sealed class ToolRig
{
    /// <summary>Pitch that makes the eye ray meet flat ground a few metres ahead.</summary>
    public const float LookDownRadians = -0.7f;

    private ToolRig(ModeFixture fixture)
    {
        Fixture = fixture;
        Composition = fixture.Composition;
        Tools = Composition.Tools!;
        Player = Composition.LocalPlayer!;
        Tools.InputSource = Script;
    }

    public ModeFixture Fixture { get; }

    public GameComposition Composition { get; }

    public ToolNode Tools { get; }

    public LocalPlayer Player { get; }

    public ScriptedTools Script { get; } = new();

    public List<IntentReply> Replies { get; } = [];

    public PlayerId PlayerId => Composition.Client!.Hello!.YourPlayer;

    public static async Task<ToolRig> StartSoloAsync(EngineTestScene scene, string dir, Guid world, bool lookDown = true)
    {
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", world.ToString());
        return await FinishStartAsync(fixture, lookDown);
    }

    public static async Task<ToolRig> StartHostAsync(EngineTestScene scene, string dir, Guid world)
    {
        var fixture = await ModeFixture.StartWithCharactersAsync(
            scene, dir, true, false, "--host", "--bind", "127.0.0.1", "--port", ModeFixture.NextPort().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", world.ToString());
        return await FinishStartAsync(fixture, true);
    }

    private static async Task<ToolRig> FinishStartAsync(ModeFixture fixture, bool lookDown)
    {
        if (fixture.StartExit is not null || !await fixture.WaitInGame() || !await fixture.WaitUntil(() => fixture.Composition.LocalPlayer!.IsActive, 120))
        {
            throw new InvalidOperationException("The game did not reach the playing state.");
        }

        var rig = new ToolRig(fixture);
        if (!await fixture.WaitTerrainIdle())
        {
            throw new InvalidOperationException("The terrain did not settle.");
        }

        rig.Tools.IntentAnswered += (reply, _) => rig.Replies.Add(reply);
        if (lookDown)
        {
            var pitched = false;
            rig.Player.InputSource = new Scripted(_ =>
            {
                if (pitched)
                {
                    return LocalInputSample.None;
                }

                pitched = true;
                return new LocalInputSample(0f, 0f, InputButtons.None, 0f, LookDownRadians);
            });
            if (!await fixture.WaitUntil(() => rig.Tools.Preview.Kind == ToolPreviewKind.Sphere, 30))
            {
                throw new InvalidOperationException("The tool does not aim at the terrain.");
            }
        }

        return rig;
    }

    /// <summary>Waits until the server answered <paramref name="count"/> intents of the tools in total.</summary>
    public Task<bool> WaitReplies(int count, double seconds = 30) => Fixture.WaitUntil(() => Replies.Count >= count, seconds);

    public Task<bool> WaitTicks(long ticks, double seconds = 30)
    {
        var target = Tools.Ticks + ticks;
        return Fixture.WaitUntil(() => Tools.Ticks >= target, seconds);
    }

    public sealed class ScriptedTools : IToolInputSource
    {
        private readonly Queue<ToolInputFrame> _frames = new();

        public Func<ToolInputFrame>? Repeat { get; set; }

        public void Press(ToolInputFrame frame) => _frames.Enqueue(frame);

        public ToolInputFrame Take() => _frames.Count > 0 ? _frames.Dequeue() : Repeat?.Invoke() ?? default;
    }

    public sealed class Scripted(Func<uint, LocalInputSample> sample) : ILocalInputSource
    {
        private uint _n;

        public LocalInputSample Take() => sample(++_n);
    }

    public static ToolInputFrame Frame(
        bool primaryPressed = false, bool primaryHeld = false, bool secondaryPressed = false, bool secondaryHeld = false, ToolKind? select = null,
        int radiusSteps = 0, int materialSteps = 0, int yawSteps = 0, int upSteps = 0) =>
        new(primaryPressed, primaryHeld, secondaryPressed, secondaryHeld, select, radiusSteps, 0, materialSteps, yawSteps, upSteps);

    public static NumVector3 Feet(ToolRig rig) => rig.Player.PredictedFeet;
}
