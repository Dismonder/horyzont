using Godot;

namespace Horyzont.Game.Testing;

/// <summary>Base class of engine tests: gives access to the scene tree and a node that is freed after each test.</summary>
public abstract class EngineTestBase
{
    /// <summary>The scene tree.</summary>
    public SceneTree Tree { get; internal set; } = null!;

    /// <summary>Node to add test content under; freed after the test.</summary>
    public Node3D Host { get; internal set; } = null!;

    /// <summary>Writes a line to the test output.</summary>
    /// <param name="line">Text.</param>
    protected static void Report(string line) => GD.Print("    " + line);

    /// <summary>Waits for the next idle frame.</summary>
    /// <returns>A task completed on the main thread.</returns>
    protected async Task NextFrame() => await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);

    /// <summary>Waits for physics frames (new bodies are visible to ray queries after one).</summary>
    /// <param name="count">Number of frames.</param>
    /// <returns>A task completed on the main thread.</returns>
    protected async Task PhysicsFrames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Tree.ToSignal(Tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>Waits frame by frame until the condition holds.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="maxFrames">Give up after this many frames.</param>
    /// <param name="what">Description for the failure message.</param>
    /// <returns>A task that fails when the condition never holds.</returns>
    protected async Task WaitUntil(Func<bool> condition, int maxFrames, string what)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return;
            }

            await NextFrame();
        }

        EngineAssert.True(condition(), $"timed out after {maxFrames} frames waiting for {what}");
    }
}
