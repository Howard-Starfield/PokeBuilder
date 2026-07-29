using SysBot.Base;
using SysBot.Pokemon.Mcp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms;

internal static class McpControlPlaneService
{
    private static readonly SemaphoreSlim Lifecycle = new(1, 1);
    private static PokeBotMcpHost? _host;
    private static TradeOrchestrator? _orchestrator;

    public static ITradeControlApi? CurrentApi => _orchestrator;

    internal static TradeOrchestrator? CurrentOrchestrator => _orchestrator;

    public static async Task TryStartAsync(Main mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        await Lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_orchestrator is not null)
                return;

            var directory = Path.GetDirectoryName(ConfigLoader.ConfigPath);
            if (string.IsNullOrWhiteSpace(directory))
                directory = Environment.CurrentDirectory;
            Directory.CreateDirectory(directory);

            var store = new SqliteTradePlanStore(
                Path.Combine(directory, "trade-control.sqlite3"));
            store.Initialize();
            var clock = new SystemTradeControlClock();
            var runtime = new CurrentTradeRuntime(() => mainForm.Runner);
            var plans = new TradePlanApplicationService(
                store,
                clock,
                new Uuid7TradeControlIdGenerator());
            var orchestrator = new TradeOrchestrator(
                store,
                plans,
                runtime,
                new SysBotTradeQueueAdapter(runtime),
                clock,
                new Uuid7TradeOperationIdGenerator());
            orchestrator.RecoverNonterminalOperations();
            _orchestrator = orchestrator;

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                PokeBotMcpHost.TokenEnvironmentVariable)))
            {
                LogUtil.LogInfo(
                    $"Shared durable trade orchestration is active; MCP transport is disabled. Set {PokeBotMcpHost.TokenEnvironmentVariable} to a high-entropy token to enable it.",
                    "MCP");
                return;
            }

            var host = new PokeBotMcpHost();
            var port = PokeBotMcpHost.ReadConfiguredPort();
            try
            {
                await host.StartAsync(
                    orchestrator,
                    new() { Port = port }).ConfigureAwait(false);
            }
            catch
            {
                await host.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _host = host;
            LogUtil.LogInfo(
                $"MCP control plane listening on http://127.0.0.1:{port}/mcp.",
                "MCP");
        }
        catch (Exception ex)
        {
            LogUtil.LogError(
                _orchestrator is null
                    ? $"Durable trade orchestration did not start: {ex.Message}"
                    : $"MCP transport did not start; shared website orchestration remains active: {ex.Message}",
                "MCP");
        }
        finally
        {
            Lifecycle.Release();
        }
    }

    public static async Task StopAsync()
    {
        await Lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            var host = _host;
            var orchestrator = _orchestrator;
            _host = null;
            _orchestrator = null;
            if (host is not null)
                await host.DisposeAsync().ConfigureAwait(false);
            orchestrator?.Dispose();
        }
        finally
        {
            Lifecycle.Release();
        }
    }
}
