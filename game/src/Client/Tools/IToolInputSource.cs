using Horyzont.Client.Tools;

namespace Horyzont.Game.Client.Tools;

/// <summary>Where the tools of the local player get their input: the keyboard and mouse reader, or a script in a test.</summary>
public interface IToolInputSource
{
    /// <summary>Takes the input accumulated since the previous call (one call per physics tick).</summary>
    /// <returns>The frame. The wheel notches arrive in <see cref="ToolInputFrame.RadiusSteps"/>; <see cref="ToolNode"/> routes them to the radius or to the palette.</returns>
    ToolInputFrame Take();
}
