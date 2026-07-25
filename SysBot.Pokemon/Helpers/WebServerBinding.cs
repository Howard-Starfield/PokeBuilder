using System;
using System.Collections.Generic;

namespace SysBot.Pokemon.Helpers;

public static class WebServerBinding
{
    public static IReadOnlyList<string> GetHttpPrefixes(int port, bool allowExternalConnections)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        if (allowExternalConnections)
            return [$"http://+:{port}/"];

        return
        [
            $"http://localhost:{port}/",
            $"http://127.0.0.1:{port}/",
        ];
    }
}
