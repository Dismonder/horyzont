using System.Numerics;

using Horyzont.Client.Props;
using Horyzont.Client.Tests.Tools;
using Horyzont.Client.Tools;
using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Props;

/// <summary>A tool controller with the prop interaction over a flat planet (radius 4000 m) and props placed by the test.</summary>
internal sealed class PropRig
{
    private uint _next = 1;

    public PropRig()
    {
        World = new FakeToolWorldView(4000f);
        Player = World.Join();
        Feet = World.State.Players.TryGet(Player, out var player) ? player.Position : default;
        Up = Vector3.Normalize(Feet);
        Side = Vector3.Normalize(Vector3.Cross(Up, Vector3.UnitZ));
        Content = SurvivalContentFactory.CreateTestWithProps();
        Held = new HeldTool { HotbarSlot = 3 };
        Picker = new PropPicker(Source, States);
        Interaction = new PropInteraction(Picker, States, Content, Held, World.Rules);
        Controller = new ToolController(
            World.Rules, World, Sender, Tracker,
            new BlockPalette([new RegistryEntry(World.Stone, "core:stone_block"), new RegistryEntry(9, "core:glass")]),
            new MaterialPalette([new RegistryEntry(1, "core:stone"), new RegistryEntry(2, "core:dirt")]),
            NullLogger<ToolController>.Instance)
        {
            Props = Interaction,
        };
    }

    public FakeToolWorldView World { get; }

    public PlayerId Player { get; }

    public Vector3 Feet { get; }

    public Vector3 Up { get; }

    public Vector3 Side { get; }

    public SurvivalContent Content { get; }

    public FakeSource Source { get; } = new();

    public ClientPropStates States { get; } = new();

    public HeldTool Held { get; }

    public PropPicker Picker { get; }

    public PropInteraction Interaction { get; }

    public FakeSender Sender { get; } = new();

    public PendingIntentTracker Tracker { get; } = new(NoEditPrediction.Instance, NullLogger<PendingIntentTracker>.Instance);

    public ToolController Controller { get; }

    /// <summary>Puts a prop on the ground at a horizontal distance from the feet.</summary>
    public PropInstance Add(PropKindV1 kind, float meters, float scale = 1f)
    {
        var anchor = Vector3.Normalize(Feet + (Side * meters)) * Feet.Length();
        var id = PropId.Create(0, 100 + (int)_next, 100, 0);
        _next++;
        var prop = new PropInstance(id, kind, anchor, Vector3.Normalize(anchor), scale);
        Source.Props.Add(prop);
        return prop;
    }

    /// <summary>The aim at a point of the prop: <paramref name="alongTrunk"/> metres above its anchor.</summary>
    public AimRay AimAt(PropInstance prop, float alongTrunk)
    {
        var target = prop.Anchor + (prop.Up * alongTrunk);
        var eye = AimRayBuilder.EyeFromFeet(Feet, World.Rules);
        Assert.True(AimRayBuilder.TryBuild(FakeToolWorldView.Planet, Feet, eye, target - eye, -Vector3.UnitZ, World, World.Rules, out var ray));
        return ray;
    }

    public void MarkHarvested(PropInstance prop) =>
        States.Apply(new PropStates(FakeToolWorldView.Planet, [new PropRegionState(prop.Id.Region, [new HarvestedPropEntry(prop.Id, new Tick(1))])]));

    public static ToolInputFrame Input(bool pressed = false, bool held = false, ToolKind? select = null) =>
        new(pressed, held || pressed, false, false, select, 0, 0, 0, 0, 0);

    internal sealed class FakeSender : IIntentSender
    {
        private uint _seq = 100;

        public bool CanSend { get; set; } = true;

        public List<Intent> Intents { get; } = [];

        public uint Submit(Intent intent)
        {
            Intents.Add(intent);
            return ++_seq;
        }
    }

    internal sealed class FakeSource : IPropSource
    {
        public List<PropInstance> Props { get; } = [];

        public void CollectInSphere(PlanetId planet, Vector3 center, float radius, List<PropInstance> result)
        {
            foreach (var prop in Props)
            {
                if (Vector3.Distance(prop.Anchor, center) <= radius)
                {
                    result.Add(prop);
                }
            }
        }
    }

    internal sealed class HeldTool : IHeldTool
    {
        public int HotbarSlot { get; set; }

        public int ToolTier { get; set; }
    }
}
