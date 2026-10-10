namespace Horyzont.Client.Tools;

/// <summary>Input of one physics tick. Steps are signed counts (wheel notches, key presses) since the last tick.</summary>
/// <param name="PrimaryPressed">The primary button went down since the last tick.</param>
/// <param name="PrimaryHeld">The primary button is down.</param>
/// <param name="SecondaryPressed">The secondary button went down since the last tick.</param>
/// <param name="SecondaryHeld">The secondary button is down.</param>
/// <param name="SelectTool">Tool chosen by a key, or <c>null</c>.</param>
/// <param name="RadiusSteps">Steps of the terrain tool radius.</param>
/// <param name="PaletteSteps">Steps in the block palette.</param>
/// <param name="MaterialSteps">Steps in the material palette.</param>
/// <param name="YawSteps">Quarter turns of the block about its up axis.</param>
/// <param name="UpSteps">Steps of the block up face.</param>
public readonly record struct ToolInputFrame(
    bool PrimaryPressed,
    bool PrimaryHeld,
    bool SecondaryPressed,
    bool SecondaryHeld,
    ToolKind? SelectTool,
    int RadiusSteps,
    int PaletteSteps,
    int MaterialSteps,
    int YawSteps,
    int UpSteps);
