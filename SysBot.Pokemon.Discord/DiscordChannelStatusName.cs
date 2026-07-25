using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon.Discord;

internal static class DiscordChannelStatusName
{
    private const int DiscordChannelNameLimit = 100;

    private static readonly string[] KnownStatusEmojis =
    [
        "✅",
        "❌",
        "🟢",
        "🔴",
        "🟩",
        "🟥",
        "☑️",
        "✔️",
        "✖️",
        "⛔",
    ];

    internal static string Apply(
        string currentName,
        string desiredEmoji,
        string configuredOnlineEmoji,
        string configuredOfflineEmoji)
    {
        string statusEmoji = desiredEmoji.Trim();
        string baseName = RemoveStatusPrefixes(
            currentName,
            configuredOnlineEmoji,
            configuredOfflineEmoji);

        if (statusEmoji.Length == 0)
            return baseName;

        int availableNameLength = Math.Max(0, DiscordChannelNameLimit - statusEmoji.Length);
        if (baseName.Length > availableNameLength)
            baseName = baseName[..availableNameLength].TrimEnd();

        return $"{statusEmoji}{baseName}";
    }

    private static string RemoveStatusPrefixes(
        string channelName,
        string configuredOnlineEmoji,
        string configuredOfflineEmoji)
    {
        string name = channelName.Trim();
        string[] prefixes = KnownStatusEmojis
            .Append(configuredOnlineEmoji)
            .Append(configuredOfflineEmoji)
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(prefix => prefix.Length)
            .ToArray();

        bool removed;
        do
        {
            removed = false;
            foreach (string prefix in prefixes)
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                name = name[prefix.Length..].TrimStart(' ', '-', '—', '|', '・');
                removed = true;
                break;
            }
        } while (removed);

        return name;
    }
}
