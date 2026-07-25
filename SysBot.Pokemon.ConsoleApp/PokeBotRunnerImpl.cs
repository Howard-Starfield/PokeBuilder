using PKHeX.Core;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Twitch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.ConsoleApp;

/// <summary>
/// Bot Environment implementation with Integrations added.
/// </summary>
public class PokeBotRunnerImpl<T> : PokeBotRunner<T> where T : PKM, new()
{
    private readonly object _discordSync = new();
    private SysCord<T>? _discordBot;
    private CancellationTokenSource? _discordBotCts;
    private Task? _discordBotTask;
    private static TwitchBot<T>? Twitch;
    private readonly ProgramConfig _config;

    public PokeBotRunnerImpl(PokeTradeHub<T> hub, BotFactory<T> fac, ProgramConfig config) : base(hub, fac)
    {
        _config = config;
    }

    protected override void AddIntegrations()
    {
        AddDiscordBot(Hub.Config.Discord);
        AddTwitchBot(Hub.Config.Twitch);
    }

    private void AddDiscordBot(DiscordSettings config)
    {
        var token = config.Token;
        if (string.IsNullOrWhiteSpace(token))
            return;

        lock (_discordSync)
        {
            if (_discordBotTask is { IsCompleted: false })
                return;

            _discordBotCts?.Dispose();
            _discordBotCts = new CancellationTokenSource();
            _discordBot = new SysCord<T>(this, _config);
            _discordBotTask = Task.Run(() => _discordBot.MainAsync(token, _discordBotCts.Token), _discordBotCts.Token);
        }
    }

    private void AddTwitchBot(TwitchSettings config)
    {
        if (string.IsNullOrWhiteSpace(config.Token))
            return;
        if (Twitch != null)
            return; // already created

        if (string.IsNullOrWhiteSpace(config.Channel))
            return;
        if (string.IsNullOrWhiteSpace(config.Username))
            return;
        if (string.IsNullOrWhiteSpace(config.Token))
            return;

        Twitch = new TwitchBot<T>(config, Hub);
        if (config.DistributionCountDown)
            Hub.BotSync.BarrierReleasingActions.Add(() => Twitch.StartingDistribution(config.MessageStart));
    }

    protected override void ShutdownIntegrations()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_discordSync)
        {
            cts = _discordBotCts;
            task = _discordBotTask;
            _discordBotCts = null;
            _discordBotTask = null;
            _discordBot = null;
        }

        if (cts == null)
            return;

        try
        {
            cts.Cancel();
            task?.Wait(3000);
        }
        catch (AggregateException)
        {
            // Expected when canceling the Discord task during shutdown.
        }
        catch (OperationCanceledException)
        {
            // Expected when canceling the Discord task during shutdown.
        }
        finally
        {
            cts.Dispose();
        }
    }
}
