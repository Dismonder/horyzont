using Godot;

using Horyzont.Client.Tools;
using Horyzont.Core.Intents;

namespace Horyzont.Game.Client.Tools;

/// <summary>
/// The ghost of the block that the builder would place: a translucent cube in the pose of the preview (the same pose the server
/// computes, <c>ConstructPlacement.OnTerrain</c>), green when the local hint is green, red when the server would refuse.
/// </summary>
public sealed partial class BlockGhostPreview : MeshInstance3D
{
    private static readonly Color _ok = new(0.3f, 1f, 0.4f, 0.4f);
    private static readonly Color _refused = new(1f, 0.25f, 0.2f, 0.4f);

    private readonly ToolNode _tools;
    private readonly StandardMaterial3D _material;
    private RejectReason _shownHint = (RejectReason)255;

    /// <summary>Creates the ghost.</summary>
    /// <param name="tools">The tools whose preview is drawn.</param>
    public BlockGhostPreview(ToolNode tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        Name = "BlockGhost";
        Mesh = new BoxMesh { Size = Vector3.One * 1.02f };
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
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
        if (preview.Kind is not (ToolPreviewKind.NewConstructCell or ToolPreviewKind.ConstructCell))
        {
            Visible = false;
            return;
        }

        Visible = true;
        var planet = _tools.Space;
        GlobalTransform = new Transform3D(planet.ToGodotBasis(preview.Rotation), planet.ToGodot(preview.Center));
        if (preview.Hint != _shownHint)
        {
            _shownHint = preview.Hint;
            _material.AlbedoColor = preview.Hint == RejectReason.None ? _ok : _refused;
        }
    }
}
