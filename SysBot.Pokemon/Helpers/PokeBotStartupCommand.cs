using System;
using System.IO;

namespace SysBot.Pokemon.Helpers;

public static class PokeBotStartupCommand
{
    public const string WindowsRunValueName = "PokeBuilder";

    public static string Build(string executablePath, string configPath)
    {
        var executable = NormalizePath(executablePath, nameof(executablePath));
        var config = NormalizePath(configPath, nameof(configPath));
        return $"\"{executable}\" \"{config}\"";
    }

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A path is required.", parameterName);
        if (path.Contains('"'))
            throw new ArgumentException("Windows startup paths cannot contain quotation marks.", parameterName);

        return Path.GetFullPath(path);
    }
}
