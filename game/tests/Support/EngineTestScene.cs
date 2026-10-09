using Godot;

namespace Horyzont.Game.Tests.Support;

/// <summary>
/// A temporary node under the scene tree root for an engine test. Disposing removes the node and waits one frame, so every
/// test cleans up after itself (engine tests share one Godot process).
/// </summary>
public sealed class EngineTestScene : IAsyncDisposable
{
    private readonly Node _root;

    private EngineTestScene(Node root) => _root = root;

    /// <summary>Node every test node is added to.</summary>
    public Node Root => _root;

    /// <summary>Creates the temporary node under the scene tree root. Call from a test marked <c>[RequireGodotRuntime]</c>.</summary>
    public static EngineTestScene Create(string name = "EngineTest")
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var node = new Node { Name = name };
        tree.Root.AddChild(node);
        return new EngineTestScene(node);
    }

    /// <summary>Adds a node under the temporary root and returns it.</summary>
    public T Add<T>(T node)
        where T : Node
    {
        _root.AddChild(node);
        return node;
    }

    /// <summary>Waits for the given number of process frames.</summary>
    public async Task WaitFrames(int frames)
    {
        var tree = _root.GetTree();
        for (var i = 0; i < frames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>
    /// Waits frame by frame until <paramref name="condition"/> holds; returns false after <paramref name="maxFrames"/> frames.
    /// No sleeping: the limit is counted in frames.
    /// </summary>
    public async Task<bool> WaitFramesUntil(Func<bool> condition, int maxFrames)
    {
        for (var i = 0; i < maxFrames; i++)
        {
            if (condition())
            {
                return true;
            }

            await WaitFrames(1);
        }

        return condition();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (GodotObject.IsInstanceValid(_root))
        {
            var tree = _root.GetTree();
            _root.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }
}
