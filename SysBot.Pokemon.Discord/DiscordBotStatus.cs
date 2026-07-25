using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon.Discord;

internal static class DiscordBotStatus
{
    internal static string GetCurrent(IEnumerable<(bool IsRunning, bool IsConnected)> bots)
    {
        return bots.Any(bot => bot.IsRunning && bot.IsConnected)
            ? "Online"
            : "Offline";
    }
}
