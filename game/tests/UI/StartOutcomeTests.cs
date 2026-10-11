using GdUnit4;

using Horyzont.Client.UI;
using Horyzont.Core.Ids;
using Horyzont.Game.UI;
using Horyzont.Server;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.UI;

[TestSuite]
public class StartOutcomeTests
{
    [TestCase]
    public void Start_AllWorldStartResults_ShowsExpectedScreen()
    {
        (WorldStartResult Result, MenuState Screen, UiStartKind Kind)[] cases =
        [
            (new WorldStartResult.Started(), MenuState.Playing, UiStartKind.Started),
            (new WorldStartResult.StartedReadOnly(ReadOnlyReason.TerrainSemanticsMismatch, "terrain 1 vs 2, 6 operations, op 42"), MenuState.Error, UiStartKind.StartedReadOnly),
            (new WorldStartResult.Corrupted("bad page", ["a.db", "b.db"]), MenuState.Error, UiStartKind.Corrupted),
            (new WorldStartResult.FromNewerVersion(9, 5), MenuState.Error, UiStartKind.FromNewerVersion),
            (new WorldStartResult.ReplayFailed(new OpId(42), "bad op"), MenuState.Error, UiStartKind.ReplayFailed),
            (new WorldStartResult.Aborted(), MenuState.Stopping, UiStartKind.Aborted),
            (new WorldStartResult.StartFailed("checkpoint failed"), MenuState.Error, UiStartKind.StartFailed),
        ];

        foreach (var (result, screen, kind) in cases)
        {
            var menu = new MenuController();
            menu.OpenWorlds(UiLaunchMode.Solo);
            menu.BeginLaunch();
            var outcome = WorldStartOutcomeMapper.Map(result);
            menu.CompleteStart(outcome);

            AssertThat(outcome.Kind).IsEqual(kind);
            AssertThat(menu.State).IsEqual(screen);

            // An error never ends the UI process and never starts a local join.
            AssertThat(menu.IsBlocked).IsEqual(screen != MenuState.Playing);
        }

        var newer = WorldStartOutcomeMapper.Map(new WorldStartResult.FromNewerVersion(9, 5));
        AssertThat(newer.SchemaVersion == 9).IsTrue();
        AssertThat(newer.SupportedVersion == 5).IsTrue();
        AssertThat(WorldStartOutcomeMapper.Map(new WorldStartResult.ReplayFailed(new OpId(42), "bad op")).OpId == 42UL).IsTrue();
        AssertThat(WorldStartOutcomeMapper.Map(new WorldStartResult.Corrupted("x", ["a.db"])).BackupCount == 1).IsTrue();
        AssertThat(WorldStartOutcomeMapper.Map(new WorldStartResult.StartedReadOnly(ReadOnlyReason.TerrainSemanticsMismatch, "m")).TerrainSemanticsMismatch).IsTrue();
    }
}
