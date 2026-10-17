using Godot;

using Horyzont.Game.Constructs;

namespace Horyzont.Game.Client.Blocks;

/// <summary>
/// The lights of the lit lamps (K6-30 D7): at most <see cref="MaxLitLampsRendered"/> <see cref="OmniLight3D"/> nodes without shadows, put
/// on the lamps nearest to the camera; every other lit lamp has no light. A fixed pool of lights is reused, so lamps switching on and off
/// or the camera moving allocate no nodes. Lights live under this node, not under the constructs, and follow the lamps by position, so a
/// moving ship carries its lamps' lights along.
/// </summary>
public sealed partial class BlockLights : Node3D
{
    /// <summary>The most lights drawn at once (a game constant, not Core).</summary>
    public const int MaxLitLampsRendered = 32;

    private static readonly Comparison<(double Distance, LampInstance Lamp)> _byDistance = (a, b) => a.Distance.CompareTo(b.Distance);

    private readonly ConstructWorld _world;
    private readonly List<LampInstance> _lamps = [];
    private readonly List<(double Distance, LampInstance Lamp)> _ranked = [];
    private readonly List<OmniLight3D> _pool = [];
    private readonly List<OmniLight3D> _active = [];

    /// <summary>Creates the lights of a construct world.</summary>
    /// <param name="world">The world whose lit lamps get lights.</param>
    public BlockLights(ConstructWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>The lights currently placed on lamps, nearest lamp first.</summary>
    public IReadOnlyList<OmniLight3D> ActiveLights => _active;

    /// <summary>Number of light nodes that are switched on (equals <see cref="ActiveLights"/>; counted from the nodes to catch leaks).</summary>
    public int VisibleLightCount
    {
        get
        {
            var count = 0;
            foreach (var light in _pool)
            {
                if (light.Visible)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        var camera = GetViewport()?.GetCamera3D();
        if (camera is not null)
        {
            Update(camera.GlobalPosition);
        }
    }

    /// <summary>Puts the lights on the lamps nearest to the camera.</summary>
    /// <param name="cameraPosition">Camera position in the Godot world.</param>
    public void Update(Vector3 cameraPosition)
    {
        _lamps.Clear();
        _world.CollectLamps(_lamps);
        _ranked.Clear();
        foreach (var lamp in _lamps)
        {
            _ranked.Add((lamp.Position.DistanceSquaredTo(cameraPosition), lamp));
        }

        var count = Math.Min(MaxLitLampsRendered, _ranked.Count);
        _ranked.Sort(_byDistance);

        _active.Clear();
        for (var i = 0; i < count; i++)
        {
            var light = LightAt(i);
            var lamp = _ranked[i].Lamp;
            light.GlobalPosition = lamp.Position;
            light.OmniRange = lamp.Light.RangeMeters;
            light.LightEnergy = lamp.Light.Energy;
            light.LightColor = ToColor(lamp.Light.Color);
            light.Visible = true;
            _active.Add(light);
        }

        for (var i = count; i < _pool.Count; i++)
        {
            _pool[i].Visible = false;
        }
    }

    private static Color ToColor(uint rgba) => new(
        ((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);

    private OmniLight3D LightAt(int index)
    {
        while (_pool.Count <= index)
        {
            var light = new OmniLight3D { Name = $"Lamp{_pool.Count}", ShadowEnabled = false, Visible = false };
            AddChild(light);
            _pool.Add(light);
        }

        return _pool[index];
    }
}
