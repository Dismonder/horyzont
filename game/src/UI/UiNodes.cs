using Godot;

namespace Horyzont.Game.UI;

/// <summary>Small helpers for the UI scripts.</summary>
internal static class UiNodes
{
    /// <summary>Connects the pressed signal of a button found by its path.</summary>
    /// <param name="owner">The node that owns the button.</param>
    /// <param name="path">Path or unique name (<c>%Name</c>).</param>
    /// <param name="handler">The handler.</param>
    public static void Bind(Node owner, string path, Action handler) => owner.GetNode<BaseButton>(path).Pressed += handler;
}
