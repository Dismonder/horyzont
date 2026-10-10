namespace Horyzont.Client;

/// <summary>How the local camera is attached.</summary>
public enum CameraMode : byte
{
    /// <summary>Behind the shoulder of the character (default).</summary>
    ThirdPerson = 0,

    /// <summary>At the eyes of the character.</summary>
    FirstPerson = 1,

    /// <summary>Developer camera detached from the body (development builds only).</summary>
    DevFree = 2,
}
