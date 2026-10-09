namespace Horyzont.Game.Constructs;

/// <summary>What a <see cref="ConstructWorld"/> builds.</summary>
public enum ConstructWorldMode
{
    /// <summary>Render meshes and collision bodies (solo, host, client).</summary>
    RenderAndCollision,

    /// <summary>Collision bodies only: no mesh instances and no visuals (dedicated server).</summary>
    CollisionOnly,
}
