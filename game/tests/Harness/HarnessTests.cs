using GdUnit4;

using Godot;

using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Harness;

[TestSuite]
public class HarnessTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void Harness_EngineRuns_RealTIsDouble()
    {
#if GODOT_REAL_T_IS_DOUBLE
        const bool isDouble = true;
#else
        const bool isDouble = false;
#endif
        AssertThat(isDouble).IsTrue();
        AssertThat(sizeof(double) == System.Runtime.InteropServices.Marshal.SizeOf<double>()).IsTrue();
        AssertThat(ClassDB.ClassExists("VoxelLodTerrain")).IsTrue();
        AssertThat(Variant.From(new Vector3(1, 2, 3)).Obj is Vector3 v && v.X == 1).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Harness_TemporaryScene_IsRemovedOnDispose()
    {
        Node? added;
        await using (var scene = EngineTestScene.Create())
        {
            added = scene.Add(new Node3D());
            AssertThat(await scene.WaitFramesUntil(() => added.IsInsideTree(), 5)).IsTrue();
        }

        AssertThat(GodotObject.IsInstanceValid(added) && added.IsInsideTree()).IsFalse();
    }
}
