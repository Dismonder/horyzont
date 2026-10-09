using System.Globalization;

namespace Horyzont.Game.Boot;

/// <summary>Reads the user arguments (after <c>--</c>) of the developer modes. Input is untrusted: every value is validated.</summary>
public sealed class DevModeArguments
{
    /// <summary>Galaxy seed used when <c>--seed</c> is not given (the ASCII bytes of "HORYZONT").</summary>
    public const ulong DefaultGalaxySeed = 0x484F52595A4F4E54UL;

    private readonly string[] _args;

    /// <summary>Wraps the arguments.</summary>
    public DevModeArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _args = args;
    }

    /// <summary>Whether the flag is present.</summary>
    public bool Has(string flag) => Array.IndexOf(_args, flag) >= 0;

    /// <summary>Value after the option, or null when the option is absent.</summary>
    /// <exception cref="ArgumentException">The option is last or followed by another option.</exception>
    public string? ValueOf(string option)
    {
        var index = Array.IndexOf(_args, option);
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= _args.Length || _args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} needs a value.");
        }

        return _args[index + 1];
    }

    /// <summary>Galaxy seed from <c>--seed</c> (1 to 16 hexadecimal digits, optional 0x prefix) or the default.</summary>
    public ulong GalaxySeed()
    {
        var text = ValueOf("--seed");
        if (text is null)
        {
            return DefaultGalaxySeed;
        }

        var digits = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (digits.Length is < 1 or > 16 || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var seed))
        {
            throw new ArgumentException("--seed must be 1 to 16 hexadecimal digits.");
        }

        return seed;
    }

    /// <summary>Finite number in [<paramref name="min"/>, <paramref name="max"/>] from the option, or null when absent.</summary>
    public float? Number(string option, float min, float max)
    {
        var text = ValueOf(option);
        if (text is null)
        {
            return null;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value) || value < min || value > max)
        {
            throw new ArgumentException($"{option} must be a number from {min} to {max}.");
        }

        return value;
    }

    /// <summary>Directory from the option; must already exist when <paramref name="mustExist"/>, otherwise it is created.</summary>
    public string? Directory(string option, bool mustExist)
    {
        var path = ValueOf(option);
        if (path is null)
        {
            return null;
        }

        if (path.Length == 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException($"{option} is not a valid path.");
        }

        var full = Path.GetFullPath(path);
        if (mustExist)
        {
            if (!System.IO.Directory.Exists(full))
            {
                throw new ArgumentException($"{option}: directory '{full}' does not exist.");
            }
        }
        else
        {
            System.IO.Directory.CreateDirectory(full);
        }

        return full;
    }
}
