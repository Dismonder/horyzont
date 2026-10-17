using Godot;

using Horyzont.Client.Settings;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Settings;

/// <summary>Reads and writes the graphics settings file (<c>user://graphics.cfg</c>, a Godot ConfigFile). A missing or damaged file never stops the game: every bad key falls back to its default.</summary>
public static partial class GraphicsSettingsFile
{
    /// <summary>The default location of the settings file.</summary>
    public const string DefaultPath = "user://graphics.cfg";

    private const string Section = "graphics";

    /// <summary>Loads the settings.</summary>
    /// <param name="path">Godot path (<c>user://...</c>) or an absolute file path.</param>
    /// <param name="logger">Receives a warning for a damaged file or value.</param>
    /// <returns>Valid settings; defaults for everything that is missing or bad.</returns>
    public static GraphicsSettings Load(string path, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var fallback = GraphicsSettings.Default;
        var file = new ConfigFile();
        var error = file.Load(path);
        if (error == Error.FileNotFound || error == Error.CantOpen)
        {
            return fallback;
        }

        if (error != Error.Ok)
        {
            logger.LogGraphicsFileUnreadable(path, error);
            return fallback;
        }

        var mode = fallback.Mode;
        if (file.HasSectionKey(Section, "upscale"))
        {
            var text = file.GetValue(Section, "upscale").AsString();
            if (!GraphicsSettings.TryParseMode(text, out mode))
            {
                logger.LogGraphicsValueIgnored("upscale", text);
                mode = fallback.Mode;
            }
        }

        return new GraphicsSettings
        {
            Mode = mode,
            RenderScale = ReadNumber(file, "render_scale", fallback.RenderScale, GraphicsSettings.IsValidRenderScale, logger),
            Sharpness = ReadNumber(file, "fsr_sharpness", fallback.Sharpness, GraphicsSettings.IsValidSharpness, logger),
        };
    }

    /// <summary>Saves the settings (clamped).</summary>
    /// <param name="path">Godot path or absolute file path.</param>
    /// <param name="settings">Settings to store.</param>
    /// <param name="logger">Receives a warning when the file cannot be written.</param>
    /// <returns>True when the file was written.</returns>
    public static bool Save(string path, GraphicsSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        var valid = settings.Clamp();
        var file = new ConfigFile();
        file.SetValue(Section, "upscale", GraphicsSettings.ModeName(valid.Mode));
        file.SetValue(Section, "render_scale", valid.RenderScale);
        file.SetValue(Section, "fsr_sharpness", valid.Sharpness);
        var error = file.Save(path);
        if (error != Error.Ok)
        {
            logger.LogGraphicsFileNotWritten(path, error);
            return false;
        }

        return true;
    }

    private static float ReadNumber(ConfigFile file, string key, float fallback, Func<float, bool> isValid, ILogger logger)
    {
        if (!file.HasSectionKey(Section, key))
        {
            return fallback;
        }

        var value = file.GetValue(Section, key);
        if (value.VariantType is Variant.Type.Float or Variant.Type.Int)
        {
            var number = (float)value.AsDouble();
            if (isValid(number))
            {
                return number;
            }
        }

        logger.LogGraphicsValueIgnored(key, value.ToString());
        return fallback;
    }

    [LoggerMessage(EventId = 9601, Level = LogLevel.Warning, Message = "the graphics settings file '{Path}' cannot be read ({Error}); the defaults are used")]
    private static partial void LogGraphicsFileUnreadable(this ILogger logger, string path, Error error);

    [LoggerMessage(EventId = 9602, Level = LogLevel.Warning, Message = "the graphics setting '{Key}' has a bad value '{Value}'; the default is used")]
    private static partial void LogGraphicsValueIgnored(this ILogger logger, string key, string value);

    [LoggerMessage(EventId = 9603, Level = LogLevel.Warning, Message = "the graphics settings file '{Path}' could not be written ({Error})")]
    private static partial void LogGraphicsFileNotWritten(this ILogger logger, string path, Error error);
}
