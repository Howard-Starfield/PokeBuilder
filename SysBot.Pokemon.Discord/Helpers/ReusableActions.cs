using Discord;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class ReusableActions
{
    private static readonly string[] separator = [",", ", ", " "];
    private static readonly SemaphoreSlim _dmRateLimiter = new(1, 1);
    private static readonly ConcurrentDictionary<ulong, IDMChannel> _dmChannels = new();
    private static DateTime _lastDmTime = DateTime.MinValue;
    private const int MinDmDelayMs = 2000;

    public static async Task EchoAndReply(this ISocketMessageChannel channel, string msg)
    {
        // Announce it in the channel the command was entered only if it's not already an echo channel.
        EchoUtil.Echo(msg);
        if (!EchoModule.IsEchoChannel(channel))
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
    }

    public static RequestSignificance GetFavor(this IUser user)
    {
        var mgr = SysCordSettings.Manager;
        if (user.Id == mgr.Owner)
            return RequestSignificance.Owner;
        if (mgr.CanUseSudo(user.Id))
            return RequestSignificance.Favored;
        if (user is SocketGuildUser g)
            return mgr.GetSignificance(g.Roles);
        return RequestSignificance.None;
    }

    public static string GetFormattedShowdownText(PKM pkm)
    {
        var newShowdown = new List<string>();
        var showdown = ShowdownParsing.GetShowdownText(pkm);
        foreach (var line in showdown.Split('\n'))
            newShowdown.Add(line);

        if (pkm.IsEgg)
            newShowdown.Add("\nPokémon is an egg");
        if (pkm.Ball > (int)Ball.None)
            newShowdown.Insert(newShowdown.FindIndex(z => z.Contains("Nature")), $"Ball: {(Ball)pkm.Ball} Ball");
        if (pkm.IsShiny)
        {
            var index = newShowdown.FindIndex(x => x.Contains("Shiny: Yes"));
            if (pkm.ShinyXor == 0 || pkm.FatefulEncounter)
                newShowdown[index] = "Shiny: Square\r";
            else newShowdown[index] = "Shiny: Star\r";
        }

        newShowdown.InsertRange(1, [$"OT: {pkm.OriginalTrainerName}", $"TID: {pkm.DisplayTID}", $"SID: {pkm.DisplaySID}", $"OTGender: {(Gender)pkm.OriginalTrainerGender}", $"Language: {(LanguageID)pkm.Language}"]);
        return Format.Code(string.Join("\n", newShowdown).TrimEnd());
    }

    public static IReadOnlyList<string> GetListFromString(string str)
    {
        // Extract comma separated list
        return str.Split(separator, StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<IDMChannel?> GetOrCreateDMAsync(IUser user)
    {
        try
        {
            if (_dmChannels.TryGetValue(user.Id, out var channel))
                return channel;

            var timeSinceLastDm = DateTime.Now - _lastDmTime;
            if (timeSinceLastDm.TotalMilliseconds < MinDmDelayMs)
            {
                var remainingDelay = MinDmDelayMs - (int)timeSinceLastDm.TotalMilliseconds;
                await Task.Delay(remainingDelay).ConfigureAwait(false);
            }

            var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
            _dmChannels[user.Id] = dm;
            _lastDmTime = DateTime.Now;
            return dm;
        }
        catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
        {
            LogUtil.LogError($"Opening DMs too fast when creating DM channel for user {user.Username} ({user.Id}). Waiting 5 seconds...", nameof(GetOrCreateDMAsync));
            await Task.Delay(5000).ConfigureAwait(false);

            try
            {
                var dm = await user.CreateDMChannelAsync().ConfigureAwait(false);
                _dmChannels[user.Id] = dm;
                _lastDmTime = DateTime.Now;
                return dm;
            }
            catch (Exception retryEx)
            {
                LogUtil.LogError($"Failed to create DM channel after retry: {retryEx.Message}", nameof(GetOrCreateDMAsync));
                return null;
            }
        }
        catch (ObjectDisposedException)
        {
            LogUtil.LogError("Discord client is disposed. Cannot create DM channel.", nameof(GetOrCreateDMAsync));
            return null;
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to create DM channel: {ex.Message}", nameof(GetOrCreateDMAsync));
            return null;
        }
    }

    public static async Task RepostPKMAsShowdownAsync(this ISocketMessageChannel channel, IAttachment att, SocketUserMessage userMessage)
    {
        if (!EntityDetection.IsSizePlausible(att.Size))
            return;
        var result = await NetUtil.DownloadPKMAsync(att).ConfigureAwait(false);
        if (!result.Success)
            return;

        var pkm = result.Data!;
        await channel.SendPKMAsShowdownSetAsync(pkm, userMessage).ConfigureAwait(false);
    }

    public static async Task SendPKMAsShowdownSetAsync(this ISocketMessageChannel channel, PKM pkm, SocketUserMessage userMessage)
    {
        var txt = GetFormattedShowdownText(pkm);
        bool canGmax = pkm is PK8 pk8 && pk8.CanGigantamax;
        var speciesImageUrl = TradeExtensions<PK9>.PokeImg(pkm, canGmax, false);

        var embed = new EmbedBuilder()
            .WithTitle("Pokémon Showdown Set")
            .WithDescription(txt)
            .WithColor(Color.Blue)
            .WithThumbnailUrl(speciesImageUrl)
            .Build();

        var botMessage = await channel.SendMessageAsync(embed: embed).ConfigureAwait(false); // Send the embed
        var warningMessage = await channel.SendMessageAsync("This message will self-destruct in 15 seconds. Please copy your data.").ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        });

        _ = Task.Run(async () =>
        {
            await Task.Delay(20000).ConfigureAwait(false);
            await botMessage.DeleteAsync().ConfigureAwait(false);
            await warningMessage.DeleteAsync().ConfigureAwait(false);
        });
    }

    public static async Task SendPKMAsync(this IMessageChannel channel, PKM pkm, string msg = "")
    {
        // Create a unique filename for each Pokémon
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = $"{uniqueId}_{PathUtil.CleanFileName(pkm.FileName)}";
        var tmp = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            // Write the file
            var data = new byte[pkm.SIZE_PARTY];
            pkm.WriteDecryptedDataParty(data);
            await File.WriteAllBytesAsync(tmp, data);

            // Send the file and WAIT for it to complete
            await channel.SendFileAsync(tmp, msg);

            // Add a small delay to ensure Discord processes each file separately
            await Task.Delay(700);
        }
        finally
        {
            // Make sure we attempt to delete the temp file even if an exception occurs
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting temporary file: {ex.Message}");
            }
        }
    }

    public static async Task<bool> SendDirectMessageAsync(this IUser user, string? message = null, Embed? embed = null)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
                return false;

            const int maxRetries = 3;
            int delayMs = 2500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await dm.SendMessageAsync(message, embed: embed).ConfigureAwait(false);
                    _lastDmTime = DateTime.Now;
                    await Task.Delay(750).ConfigureAwait(false);
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    LogUtil.LogError("Discord client is disposed. Cannot send DM.", nameof(SendDirectMessageAsync));
                    return false;
                }
                catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
                {
                    LogUtil.LogError($"Opening DMs too fast! Waiting 5 seconds before retry. User: {user.Username} ({user.Id})", nameof(SendDirectMessageAsync));
                    _dmChannels.TryRemove(user.Id, out _);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(5000).ConfigureAwait(false);
                        dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
                        if (dm == null)
                            return false;
                        continue;
                    }

                    return false;
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    LogUtil.LogError($"Cannot send messages to user {user.Username} ({user.Id}). DMs may be disabled.", nameof(SendDirectMessageAsync));
                    return false;
                }
                catch (HttpException ex)
                {
                    if (attempt == maxRetries)
                    {
                        LogUtil.LogError($"Failed to DM {user.Username} after {maxRetries} attempts: {ex.Message}", nameof(SendDirectMessageAsync));
                        return false;
                    }

                    LogUtil.LogInfo($"Discord error sending DM to {user.Username}, retrying in {delayMs}ms (attempt {attempt}/{maxRetries})", nameof(SendDirectMessageAsync));
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    delayMs *= 2;
                }
            }

            return false;
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task<bool> SendDirectFileAsync(this IUser user, string filePath, string message = "", Embed? embed = null)
    {
        await _dmRateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
            if (dm == null)
                return false;

            const int maxRetries = 3;
            int delayMs = 2500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await dm.SendFileAsync(filePath, message, embed: embed).ConfigureAwait(false);
                    _lastDmTime = DateTime.Now;
                    await Task.Delay(750).ConfigureAwait(false);
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    LogUtil.LogError("Discord client is disposed. Cannot send DM file.", nameof(SendDirectFileAsync));
                    return false;
                }
                catch (HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode.Value == (DiscordErrorCode)40003)
                {
                    LogUtil.LogError($"Opening DMs too fast while sending a file! Waiting 5 seconds before retry. User: {user.Username} ({user.Id})", nameof(SendDirectFileAsync));
                    _dmChannels.TryRemove(user.Id, out _);
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(5000).ConfigureAwait(false);
                        dm = await GetOrCreateDMAsync(user).ConfigureAwait(false);
                        if (dm == null)
                            return false;
                        continue;
                    }

                    return false;
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    LogUtil.LogError($"Cannot send files to user {user.Username} ({user.Id}). DMs may be disabled.", nameof(SendDirectFileAsync));
                    return false;
                }
                catch (HttpException ex)
                {
                    if (attempt == maxRetries)
                    {
                        LogUtil.LogError($"Failed to DM file to {user.Username} after {maxRetries} attempts: {ex.Message}", nameof(SendDirectFileAsync));
                        return false;
                    }

                    LogUtil.LogInfo($"Discord error sending DM file to {user.Username}, retrying in {delayMs}ms (attempt {attempt}/{maxRetries})", nameof(SendDirectFileAsync));
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    delayMs *= 2;
                }
            }

            return false;
        }
        finally
        {
            _dmRateLimiter.Release();
        }
    }

    public static async Task SendPKMAsync(this IUser user, PKM pkm, string msg = "")
    {
        // Create a unique filename for each Pokémon
        var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = $"{uniqueId}_{PathUtil.CleanFileName(pkm.FileName)}";
        var tmp = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            // Write the file
            var data = new byte[pkm.SIZE_PARTY];
            pkm.WriteDecryptedDataParty(data);
            await File.WriteAllBytesAsync(tmp, data);

            // Send via the shared DM helper so DM channel creation is rate limited.
            await user.SendDirectFileAsync(tmp, msg).ConfigureAwait(false);
        }
        finally
        {
            // Make sure we attempt to delete the temp file even if an exception occurs
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting temporary file: {ex.Message}");
            }
        }
    }

    public static string StripCodeBlock(string str) => str
        .Replace("`\n", "")
        .Replace("\n`", "")
        .Replace("`", "")
        .Trim();
}
