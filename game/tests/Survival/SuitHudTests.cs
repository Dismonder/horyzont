using GdUnit4;

using Godot;

using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;
using Horyzont.Game.Client.Survival;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Survival;

/// <summary>The suit HUD with sample data (K6-34 D3), without a window: the bars, the hazard icons and the status line. How it looks is for the owner to judge.</summary>
[TestSuite]
public class SuitHudTests
{
    private static SuitHudData Sample(
        bool visible = true, byte present = 0, byte active = 0, byte exposed = 0, bool sheltered = false, int hits = 0, bool generator = false, bool lifeSupport = false) =>
        new(visible, new SuitResources(75.9f, 100f, 100f, 100f, 40f), new SuitResources(100f, 100f, 100f, 100f, 200f), present, active, exposed, sheltered, hits, generator, lifeSupport);

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task SuitHud_SampleData_ShowsBarsAndIcons()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var hud = scene.Add(new VitalsHud());
        await scene.WaitFrames(2);
        var suit = hud.Suit!;

        // Without a suit source the row is never shown.
        hud.Apply(new VitalsHudData(true, 100f, 100f, 100f, 100f, false, false, 0, 0, string.Empty, 0, [default, default, default]));
        AssertThat(suit.IsShown).IsFalse();

        suit.Apply(Sample(present: 0b00001, active: 0b00001, exposed: 0b10000, sheltered: true, hits: 23, generator: true, lifeSupport: true));
        await scene.WaitFrames(2);

        AssertThat(suit.IsShown).IsTrue();
        AssertThat(suit.BarText(0)).IsEqual("Heating  75 / 100");
        AssertThat(suit.BarText(4)).IsEqual("Oxygen  40 / 200");
        AssertThat(suit.HazardState(0)).IsEqual(2); // cold acts: coloured
        AssertThat(suit.HazardState(1)).IsEqual(0);
        AssertThat(suit.HazardState(4)).IsEqual(3); // vacuum reaches the player: pulsing red
        AssertThat(suit.StatusText).IsEqual("Shelter 23/26   Oxygen generator   Life support");

        suit.Apply(Sample(present: 0b00010));
        AssertThat(suit.HazardState(1)).IsEqual(1); // present only: grey
        AssertThat(suit.StatusText).IsEqual(string.Empty);

        suit.Apply(Sample(visible: false));
        AssertThat(suit.IsShown).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task SuitHud_FromTheSource_ReadsTheVitals()
    {
        var vitals = new Horyzont.Client.Survival.ClientVitals();
        AssertThat(SuitHudSource.Build(vitals).Visible).IsFalse();

        vitals.Apply(
            new PlayerVitalsMessage(
                100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, new VitalsEnvironment(true, false, false, 0b00001, 0b00001, 0, 22),
                new SuitResources(50f, 100f, 100f, 100f, 100f), new SuitResources(100f, 100f, 100f, 100f, 100f), default, 0), 1000);
        var data = SuitHudSource.Build(vitals);

        AssertThat(data.Visible).IsTrue();
        AssertThat(data.Values.Heating).IsEqual(50f);
        AssertThat(data.Sheltered).IsTrue();
        AssertThat(data.ShelterHits).IsEqual(22);
        AssertThat(data.Active).IsEqual((byte)1);
        await Task.CompletedTask;
    }
}
