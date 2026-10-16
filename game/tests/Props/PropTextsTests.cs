using GdUnit4;

using Godot;

using Horyzont.Client.Props;
using Horyzont.Core.Intents;
using Horyzont.Game.Client.Props;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Props;

/// <summary>K6-31 D6: the texts of harvesting exist in both languages (the translations are imported without a window).</summary>
[TestSuite]
public class PropTextsTests
{
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Texts_ExistInBothLanguages()
    {
        await using var scene = EngineTestScene.Create();
        _ = scene;
        var keys = Enum.GetValues<PropPromptKind>().Select(PropPromptHud.KeyOf)
            .Append(ToolHud.RejectKey(RejectReason.ToolTierTooLow))
            .Concat(["ITEM_CORE_WOOD", "ITEM_CORE_FIBER", "ITEM_CORE_CRYSTAL", "ITEM_CORE_STONE"]);
        foreach (var locale in new[] { "en", "pl" })
        {
            TranslationServer.SetLocale(locale);
            foreach (var key in keys)
            {
                var text = TranslationServer.Translate(key).ToString();
                AssertThat(text).IsNotEqual(key);
                AssertThat(text.Length).IsGreater(0);
            }

            // The tier text takes the number; the other hints do not.
            AssertThat(TranslationServer.Translate(PropPromptHud.KeyOf(PropPromptKind.ToolTierTooLow)).ToString()).Contains("{0}");
        }

        TranslationServer.SetLocale("pl");
        AssertThat(TranslationServer.Translate(PropPromptHud.KeyOf(PropPromptKind.CutTree)).ToString()).IsEqual("[LPM] Zetnij drzewo");
        AssertThat(TranslationServer.Translate(ToolHud.RejectKey(RejectReason.ToolTierTooLow)).ToString()).IsEqual("Za słabe narzędzie");
        AssertThat(TranslationServer.Translate("ITEM_CORE_WOOD").ToString()).IsEqual("Drewno");
        TranslationServer.SetLocale("en");
        AssertThat(TranslationServer.Translate(PropPromptHud.KeyOf(PropPromptKind.BreakRock)).ToString()).IsEqual("[LMB] Break the rock");
        AssertThat(TranslationServer.Translate(ToolHud.RejectKey(RejectReason.ToolTierTooLow)).ToString()).IsEqual("Tool too weak");
    }
}
