namespace Horyzont.Client.Camera;

/// <summary>Placement of the third-person camera relative to the eye, in the camera basis.</summary>
/// <param name="Behind">Distance behind the eye along the view direction in metres.</param>
/// <param name="Right">Offset to the right in metres.</param>
/// <param name="Up">Offset upwards in metres.</param>
public readonly record struct TppOffsets(float Behind, float Right, float Up)
{
    /// <summary>Gets the default over-the-shoulder placement: 3.5 m behind, 0.5 m to the right, 0.2 m up.</summary>
    public static TppOffsets Default => new(3.5f, 0.5f, 0.2f);
}
