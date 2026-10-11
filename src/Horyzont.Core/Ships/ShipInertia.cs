namespace Horyzont.Core.Ships;

/// <summary>
/// Symmetric inertia tensor about the centre of mass in construct axes, kg·m². Off-diagonal entries are the tensor entries
/// themselves (the negated products of inertia): Ixy = −Σ m·x·y.
/// </summary>
/// <param name="Xx">Entry xx = Σ m·(y² + z²).</param>
/// <param name="Yy">Entry yy = Σ m·(x² + z²).</param>
/// <param name="Zz">Entry zz = Σ m·(x² + y²).</param>
/// <param name="Xy">Entry xy = yx.</param>
/// <param name="Xz">Entry xz = zx.</param>
/// <param name="Yz">Entry yz = zy.</param>
public readonly record struct ShipInertia(float Xx, float Yy, float Zz, float Xy, float Xz, float Yz);
