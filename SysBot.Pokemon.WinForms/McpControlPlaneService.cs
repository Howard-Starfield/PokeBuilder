using SysBot.Base;
using SysBot.Pokemon.Mcp;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms;

internal sealed record McpControlPlaneStatus(
    bool OrchestrationActive,
    bool TransportRunning,
    bool TokenConfigured,
    int Port,
    string Endpoint,
    string HealthEndpoint,
    string? LastError);

internal static class McpControlPlaneService
{
    private static readonly SemaphoreSlim Lifecycle = new(1, 1);
    private static readonly object StatusSync = new();
    private static PokeBotMcpHost? _host;
    private static TradeOrchestrator? _orchestrator;
    private static int _port = PokeBotMcpHost.DefaultPort;
    private static string? _lastError;

    public static ITradeControlApi? CurrentApi => _orchestrator;

    internal static TradeOrchestrator? CurrentOrchestrator => _orchestrator;

    public static event EventHandler? StatusChanged;

    public static McpControlPlaneStatus GetStatus()
    {
        lock (StatusSync)
        {
            var endpoint = $"http://127.0.0.1:{_port}";
            return new(
                _orchestrator is not null,
                _host?.IsRunning == true,
                HasValidTokenConfiguration(),
                _port,
                $"{endpoint}/mcp",
                $"{endpoint}/health",
                _lastError);
        }
    }

    public static async Task TryStartAsync(Main mainForm)
    {
        ArgumentNullException.ThrowIfNull(mainForm);

        await Lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_orchestrator is not null)
                return;

            SetStatus(PokeBotMcpHost.DefaultPort, null);
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
            PublishStatus();

            if (!HasValidTokenConfiguration())
            {
                LogUtil.LogInfo(
                    $"Shared durable trade orchestration is active; MCP transport is disabled. Set {PokeBotMcpHost.TokenEnvironmentVariable} to a high-entropy token to enable it.",
                    "MCP");
                PublishStatus();
                return;
            }

            var host = new PokeBotMcpHost();
            var port = PokeBotMcpHost.ReadConfiguredPort();
            SetStatus(port, null);
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
            PublishStatus();
            LogUtil.LogInfo(
                $"MCP control plane listening on http://127.0.0.1:{port}/mcp.",
                "MCP");
        }
        catch (Exception ex)
        {
            SetStatus(null, ex.Message);
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
            SetStatus(PokeBotMcpHost.DefaultPort, null);
            if (host is not null)
                await host.DisposeAsync().ConfigureAwait(false);
            orchestrator?.Dispose();
            PublishStatus();
        }
        finally
        {
            Lifecycle.Release();
        }
    }

    private static bool HasValidTokenConfiguration()
    {
        var token = Environment.GetEnvironmentVariable(
            PokeBotMcpHost.TokenEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(token) &&
            token.Length >= 32 &&
            !token.Any(char.IsWhiteSpace) &&
            token.Distinct().Count() >= 8;
    }

    private static void SetStatus(int? port, string? error)
    {
        lock (StatusSync)
        {
            if (port is not null)
                _port = port.Value;
            _lastError = error;
        }
        PublishStatus();
    }

    private static void PublishStatus() =>
        StatusChanged?.Invoke(null, EventArgs.Empty);
}
