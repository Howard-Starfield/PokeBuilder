using System;
using System.Collections.Generic;

namespace SysBot.Pokemon.Helpers;

public static class PokeBotLaunchArguments
{
    public static string? FindConfigPath(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return argument;
        }

        return null;
    }
}
