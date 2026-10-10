using GdUnit4;

using Godot;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class InputMapTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void InputMap_ControlsAreBoundToTheDocumentedKeys()
    {
        var expected = new Dictionary<string, Key>
        {
            ["move_forward"] = Key.W,
            ["move_back"] = Key.S,
            ["move_left"] = Key.A,
            ["move_right"] = Key.D,
            ["jump"] = Key.Space,
            ["sprint"] = Key.Shift,
            ["crouch"] = Key.Ctrl,
            ["camera_toggle"] = Key.V,
            ["dev_fly_toggle"] = Key.F,
            ["dev_camera_toggle"] = Key.F8,
            ["dev_camera_orbit"] = Key.F9,
            ["release_mouse"] = Key.Escape,
        };

        foreach (var (action, key) in expected)
        {
            AssertThat(InputMap.HasAction(action)).IsTrue();
            var bound = InputMap.ActionGetEvents(action).OfType<InputEventKey>().Any(e => e.PhysicalKeycode == key);
            AssertThat(bound).IsTrue();
        }
    }
}
