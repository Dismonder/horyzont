using Godot;

using Horyzont.Game.Testing;
using Horyzont.Testing;

namespace Horyzont.Game.Tests.Constructs;

/// <summary>
/// Frame-time tests of a 64³ construct. Headless they measure the CPU side (meshing hand-off, mesh upload to the dummy renderer,
/// collision shapes); the GPU side is measured by <see cref="ConstructHitchManual"/>.
/// </summary>
public sealed class ConstructHitchTests : EngineTestBase
{
    [EngineTest(Slow = true)]
    public async Task Construct64_LoadIn_MaxFrame50ms()
    {
        Report($"physics engine: {ProjectSettings.GetSetting("physics/3d/physics_engine")}");
        foreach (var pattern in new[] { ConstructPattern.Solid, ConstructPattern.Building, ConstructPattern.Random35 })
        {
            await ConstructHitchScenarios.LoadInAsync(this, pattern, assertLimit: true);
        }

        // Worst case for the face count: reported, not gated (P3).
        await ConstructHitchScenarios.LoadInAsync(this, ConstructPattern.Checkerboard, assertLimit: false);
    }

    [EngineTest(Slow = true)]
    public async Task Construct64_EditStream_MaxFrame50ms_RemeshP99Under100ms() =>
        await ConstructHitchScenarios.EditStreamAsync(this, assertLimits: true);
}

/// <summary>Same scenarios with a window and a GPU (<c>godot --path game -- --engine-tests --manual --filter=ConstructHitchManual</c>); nothing is gated, the numbers go to the PR.</summary>
public sealed class ConstructHitchManual : EngineTestBase
{
    [EngineTest(Manual = true)]
    public async Task AllScenarios_ReportFrameTimes()
    {
        Report($"physics engine: {ProjectSettings.GetSetting("physics/3d/physics_engine")}, driver: {RenderingServer.GetCurrentRenderingDriverName()}, " +
               $"adapter: {RenderingServer.GetVideoAdapterName()}");
        // The first draw with the block shader compiles pipelines; that is a start-up cost outside this package, so it is paid before measuring.
        Report("warm-up (not part of the result):");
        await ConstructHitchScenarios.LoadInAsync(this, ConstructPattern.Solid, assertLimit: false, withCamera: true);
        foreach (var pattern in Enum.GetValues<ConstructPattern>())
        {
            await ConstructHitchScenarios.LoadInAsync(this, pattern, assertLimit: false, withCamera: true);
        }

        await ConstructHitchScenarios.EditStreamAsync(this, assertLimits: false, withCamera: true);
    }
}
