using Godot;

using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Game.Frames;

namespace Horyzont.Game.Client.Tools;

/// <summary>A translucent sphere where the terrain manipulator would edit: blue when the local hint is green, red when the server would refuse.</summary>
public sealed partial class TerrainToolPreview : MeshInstance3D
{
    private static readonly Color _ok = new(0.25f, 0.75f, 1f, 0.35f);
    private static readonly Color _refused = new(1f, 0.25f, 0.2f, 0.35f);

    private readonly ToolNode _tools;
    private readonly StandardMaterial3D _material;
    private RejectReason _shownHint = (RejectReason)255;

    /// <summary>Creates the preview.</summary>
    /// <param name="tools">The tools whose preview is drawn.</param>
    public TerrainToolPreview(ToolNode tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        Name = "TerrainPreview";
        Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 };
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = _ok,
        };
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        TopLevel = true;
        Visible = false;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var preview = _tools.Preview;
        if (preview.Kind != ToolPreviewKind.Sphere)
        {
            Visible = false;
            return;
        }

        Visible = true;
        GlobalPosition = PlanetFrames.ToGodot(_tools.Planet, preview.Center);
        Scale = Vector3.One * preview.Radius;
        if (preview.Hint != _shownHint)
        {
            _shownHint = preview.Hint;
            _material.AlbedoColor = preview.Hint == RejectReason.None ? _ok : _refused;
        }
    }
}
