using GdUnit4;

using Godot;

using Horyzont.Client.Tools;
using Horyzont.Game.Client.Inventory;
using Horyzont.Game.Client.Survival;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Survival;

/// <summary>The vitals HUD with sample data (K6-33 D3), without a window: the texts, the slots and the states of the rest. How it looks is for the owner to judge.</summary>
[TestSuite]
public class VitalsHudTests
{
    private static readonly ItemVisual _berries = new("Be", new Color(0.7f, 0.2f, 0.4f), ToolKind.None);

    private static FoodSlotData Food(string name, double fraction, string time, bool blinking = false) => new(true, _berries, name, fraction, time, blinking);

    private static VitalsHudData Sample(bool visible = true, bool exhausted = false, bool resting = false, string rested = "", int comfort = 0) => new(
        visible, 85.9f, 110.4f, 40.2f, 121f, exhausted, resting, 12, 20, rested, comfort,
        [Food("Berries", 0.66, "6 min"), Food("Berry jam", 0.15, "3 min", blinking: true), default]);

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task VitalsHud_SampleData_ShowsBarsFoodAndRest()
    {
        TranslationServer.SetLocale("en");
        await using var scene = EngineTestScene.Create();
        var hud = scene.Add(new VitalsHud());
        await scene.WaitFrames(2);
        AssertThat(hud.IsShown).IsFalse();

        hud.Apply(Sample());
        await scene.WaitFrames(2);

        AssertThat(hud.IsShown).IsTrue();
        AssertThat(hud.HealthText).IsEqual("Health  85 / 110");
        AssertThat(hud.StaminaText).IsEqual("Stamina  40 / 121");
        AssertThat(hud.ShowsExhausted).IsFalse();
        AssertThat(hud.FoodFilled(0)).IsTrue();
        AssertThat(hud.FoodText(0)).IsEqual("6 min");
        AssertThat(hud.FoodFilled(1)).IsTrue();
        AssertThat(hud.FoodText(1)).IsEqual("3 min");
        AssertThat(hud.FoodFilled(2)).IsFalse();
        AssertThat(hud.FoodText(2)).IsEqual(string.Empty);
        AssertThat(hud.RestText).IsEqual(string.Empty);

        hud.Apply(Sample(exhausted: true));
        AssertThat(hud.ShowsExhausted).IsTrue();
        AssertThat(hud.StaminaText).IsEqual("Exhausted  40 / 121");

        hud.Apply(Sample(resting: true));
        AssertThat(hud.RestText).IsEqual("Resting 12 / 20 s");

        hud.Apply(Sample(rested: "11 min", comfort: 3));
        AssertThat(hud.RestText).IsEqual("Rested 11 min   Comfort 3");

        hud.Apply(Sample(visible: false));
        AssertThat(hud.IsShown).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task VitalsHud_Polish_UsesTheTranslations()
    {
        TranslationServer.SetLocale("pl");
        await using var scene = EngineTestScene.Create();
        var hud = scene.Add(new VitalsHud());
        await scene.WaitFrames(2);

        hud.Apply(Sample(rested: "11 min", comfort: 3));
        await scene.WaitFrames(2);

        AssertThat(hud.HealthText).IsEqual("Zdrowie  85 / 110");
        AssertThat(hud.StaminaText).IsEqual("Wytrzymałość  40 / 121");
        AssertThat(hud.RestText).IsEqual("Wypoczęty 11 min   Komfort 3");
        TranslationServer.SetLocale("en");
    }
}
