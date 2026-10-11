using Godot;

using Horyzont.Client.Navigation;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Diagnostics;
using Horyzont.Game.Planet.Generators;

namespace Horyzont.Game.UI.Map;

/// <summary>
/// The surface heights of one planet for the planet map (K5-27 decision 12): a 128 × 64 equirectangular grid of the height above the
/// nominal radius, from the generator profile of the planet, and the hypsometric texture made from it. The generator is built on the
/// main thread; the rows are bisected on the <see cref="WorkerThreadPool"/> (each row with its own probe), and the texture is built on
/// the main thread when <see cref="Poll"/> sees the group finished. There is no fog of war, and player edits are not visible: the
/// map shows the generator, not the voxels. Created through <see cref="PlanetMapCache"/>.
/// </summary>
public sealed class PlanetMap
{
    private readonly PlanetParams _planet;
    private readonly float[] _heights = new float[PlanetMapProjection.Width * PlanetMapProjection.Height];
    private VoxelGeneratorGraph? _generator;
    private long _group = -1;
    private Exception? _failure;

    internal PlanetMap(PlanetParams planet)
    {
        _planet = planet;
    }

    /// <summary>Gets the planet.</summary>
    public PlanetParams Planet => _planet;

    /// <summary>Gets a value indicating whether the heights and the texture are ready.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Gets the exception of a failed sampling, if any.</summary>
    public Exception? Failure => _failure;

    /// <summary>Gets the texture (hypsometric colours); null until <see cref="IsReady"/>.</summary>
    public ImageTexture? Texture { get; private set; }

    /// <summary>Gets the heights above the nominal radius, row by row from the north (tests).</summary>
    public IReadOnlyList<float> Heights => _heights;

    /// <summary>Height above the nominal radius at a map point, from the nearest grid cell.</summary>
    /// <param name="u">Horizontal position, 0..1.</param>
    /// <param name="v">Vertical position, 0..1.</param>
    /// <returns>The height in metres; 0 before <see cref="IsReady"/>.</returns>
    public float HeightAt(float u, float v)
    {
        if (!IsReady)
        {
            return 0f;
        }

        var x = Math.Clamp((int)(u * PlanetMapProjection.Width), 0, PlanetMapProjection.Width - 1);
        var y = Math.Clamp((int)(v * PlanetMapProjection.Height), 0, PlanetMapProjection.Height - 1);
        return _heights[(y * PlanetMapProjection.Width) + x];
    }

    /// <summary>Starts the sampling on the worker pool. Main thread.</summary>
    internal void Start()
    {
        _generator = PlanetGeneratorV1.Create(_planet);
        _group = WorkerThreadPool.AddGroupTask(Callable.From<int>(SampleRow), PlanetMapProjection.Height, -1, false, "planet map");
    }

    /// <summary>Completes the sampling when the workers finished. Main thread.</summary>
    /// <returns>True once the map is ready.</returns>
    public bool Poll()
    {
        if (IsReady || _group < 0 || !WorkerThreadPool.IsGroupTaskCompleted(_group))
        {
            return IsReady;
        }

        WorkerThreadPool.WaitForGroupTaskCompletion(_group);
        _group = -1;
        _generator = null;
        if (_failure is not null)
        {
            return false;
        }

        var image = Image.CreateEmpty(PlanetMapProjection.Width, PlanetMapProjection.Height, false, Image.Format.Rgb8);
        for (var y = 0; y < PlanetMapProjection.Height; y++)
        {
            for (var x = 0; x < PlanetMapProjection.Width; x++)
            {
                image.SetPixel(x, y, Hypsometric(_heights[(y * PlanetMapProjection.Width) + x]));
            }
        }

        Texture = ImageTexture.CreateFromImage(image);
        IsReady = true;
        return true;
    }

    /// <summary>Hypsometric colour of a height above the nominal radius.</summary>
    /// <param name="height">Height in metres.</param>
    /// <returns>The colour.</returns>
    public static Color Hypsometric(float height)
    {
        if (height < -60f)
        {
            return new Color(0.08f, 0.2f, 0.45f).Lerp(new Color(0.1f, 0.35f, 0.6f), Mathf.Clamp((height + 300f) / 240f, 0f, 1f));
        }

        if (height < 0f)
        {
            return new Color(0.2f, 0.55f, 0.6f).Lerp(new Color(0.35f, 0.65f, 0.35f), (height + 60f) / 60f);
        }

        if (height < 100f)
        {
            return new Color(0.35f, 0.65f, 0.35f).Lerp(new Color(0.55f, 0.45f, 0.3f), height / 100f);
        }

        return new Color(0.55f, 0.45f, 0.3f).Lerp(new Color(0.95f, 0.95f, 0.95f), Mathf.Clamp((height - 100f) / 200f, 0f, 1f));
    }

    private void SampleRow(int row)
    {
        if (_generator is null)
        {
            return;
        }

        try
        {
            using var probe = new GeneratorProbe(_generator);
            var radius = _planet.RadiusMeters;
            var v = (row + 0.5f) / PlanetMapProjection.Height;
            for (var column = 0; column < PlanetMapProjection.Width; column++)
            {
                var direction = PlanetMapProjection.Direction((column + 0.5f) / PlanetMapProjection.Width, v);
                var surface = SurfaceBisection.FindOutermostSurfaceRadius(
                    p => probe.Sample((int)MathF.Round(p.X), (int)MathF.Round(p.Y), (int)MathF.Round(p.Z)).Sdf,
                    direction,
                    radius,
                    PlanetGeneratorV1.CaveMinDepth,
                    out _);
                _heights[(row * PlanetMapProjection.Width) + column] = surface - radius;
            }
        }
#pragma warning disable CA1031 // A worker must not throw into the engine: the failure is kept and shown by the panel.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failure = ex;
        }
    }
}
