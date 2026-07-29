using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Base;
using SysBot.Pokemon.Helpers;

namespace SysBot.Pokemon.WinForms.WebApi;

/// <summary>
/// Centralized restart management system that handles scheduled and manual restarts
/// with efficient timing and simplified state management.
/// </summary>
public static class RestartManager
{
    #region Private Fields
    private static readonly object _stateLock = new();
    private static RestartState _currentState = RestartState.Idle;
    private static System.Threading.Timer? _scheduleTimer;
    private static DateTime? _nextScheduledRestart;
    private static CancellationTokenSource? _restartCts;
    private static Main? _mainForm;
    private static int _tcpPort;
    private static bool _scheduleOwner;
    
    // Consolidated file paths
    private static string WorkingDirectory => Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
    private static string ScheduleConfigPath => Path.Combine(WorkingDirectory, "restart_schedule.json");
    private static string RestartFlagPath => Path.Combine(WorkingDirectory, "restart_in_progress.flag");
    private static string PreRestartPidsPath => Path.Combine(WorkingDirectory, "pre_restart_pids.json");
    #endregion

    #region Public Properties
    public static bool IsRestartInProgress
    {
        get { lock (_stateLock) return _currentState != RestartState.Idle; }
    }

    public static RestartState CurrentState
    {
        get { lock (_stateLock) return _currentState; }
    }

    public static DateTime? NextScheduledRestart
    {
        get { lock (_stateLock) return _nextScheduledRestart; }
    }
    #endregion

    #region Initialization
    public static void Initialize(Main mainForm, int tcpPort, bool scheduleOwner)
    {
        _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
        _tcpPort = tcpPort;
        _scheduleOwner = scheduleOwner;
        
        CheckPostRestartStartup();
        TryMigrateLegacySchedule();
        InitializeScheduledRestarts();
        
        LogUtil.LogInfo(
            "RestartManager",
            scheduleOwner
                ? "RestartManager initialized with scheduled-restart ownership"
                : "RestartManager initialized without scheduled-restart ownership");
    }

    public static void Shutdown()
    {
        lock (_stateLock)
        {
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;
            _restartCts?.Cancel();
            _restartCts = null;
            _currentState = RestartState.Idle;
            _nextScheduledRestart = null;
            _mainForm = null;
            _scheduleOwner = false;
        }
        
        LogUtil.LogInfo("RestartManager", "RestartManager shutdown completed");
    }
    #endregion

    #region Scheduled Restart Management
    public static void InitializeScheduledRestarts()
    {
        RefreshScheduleFromConfiguration();
    }

    public static RestartScheduleConfig GetScheduleConfig()
    {
        var hub = _mainForm?.Config.Hub;
        return hub is null
            ? new RestartScheduleConfig()
            : RestartScheduleConfig.FromHub(hub);
    }

    public static void UpdateScheduleConfig(RestartScheduleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var mainForm = _mainForm ?? throw new InvalidOperationException("RestartManager has not been initialized.");
        var cron = config.ResolveCronExpression();
        CronSchedule.Parse(cron);

        void Apply()
        {
            mainForm.Config.Hub.ScheduledRestartEnabled = config.Enabled;
            mainForm.Config.Hub.RestartCronSchedule = cron;
            mainForm.SaveCurrentConfig();
        }

        if (mainForm.InvokeRequired)
            mainForm.Invoke((Action)Apply);
        else
            Apply();

        LogUtil.LogInfo(
            "RestartManager",
            config.Enabled
                ? $"Saved restart schedule to the main configuration: {cron}"
                : "Disabled the restart schedule in the main configuration");
    }

    public static void RefreshScheduleFromConfiguration()
    {
        if (_mainForm is null)
            return;

        if (!_scheduleOwner)
        {
            ClearScheduleTimer();
            return;
        }

        UpdateScheduleTimerWithConfig(GetScheduleConfig());
    }

    private static void UpdateScheduleTimer()
    {
        RefreshScheduleFromConfiguration();
    }

    private static void UpdateScheduleTimerWithConfig(RestartScheduleConfig config)
    {
        DateTime? nextRestart = null;
        string? scheduleError = null;
        if (config.Enabled)
        {
            try
            {
                nextRestart = CronSchedule
                    .Parse(config.ResolveCronExpression())
                    .GetNextOccurrence(DateTime.Now);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                scheduleError = ex.Message;
            }
        }

        lock (_stateLock)
        {
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;
            _nextScheduledRestart = null;

            if (!config.Enabled)
            {
                LogUtil.LogInfo("RestartManager", "Scheduled restarts are disabled - timer cleared");
                return;
            }

            if (scheduleError is not null || nextRestart is null)
            {
                LogUtil.LogError(
                    "RestartManager",
                    $"Invalid restart cron schedule '{config.ResolveCronExpression()}': {scheduleError}");
                return;
            }

            _nextScheduledRestart = nextRestart;

            var delay = nextRestart.Value - DateTime.Now;
            if (delay.TotalMilliseconds > 0)
            {
                var timerDelay = delay > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delay;
                _scheduleTimer = new System.Threading.Timer(
                    OnScheduleTimerElapsed,
                    null,
                    timerDelay,
                    Timeout.InfiniteTimeSpan);
                LogUtil.LogInfo(
                    "RestartManager",
                    $"Next scheduled restart: {nextRestart:yyyy-MM-dd HH:mm:ss} ({config.ResolveCronExpression()})");
            }
        }
    }

    private static void ClearScheduleTimer()
    {
        lock (_stateLock)
        {
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;
            _nextScheduledRestart = null;
        }
    }

    private static void OnScheduleTimerElapsed(object? state)
    {
        DateTime? scheduled;
        lock (_stateLock)
            scheduled = _nextScheduledRestart;

        if (scheduled is null || DateTime.Now.AddSeconds(1) < scheduled.Value)
        {
            UpdateScheduleTimer();
            return;
        }

        OnScheduledRestart(state);
    }

    private static void OnScheduledRestart(object? state)
    {
        try
        {
            LogUtil.LogInfo("RestartManager", "Executing scheduled restart");
            
            // Start the restart process asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteFullRestartAsync(RestartReason.Scheduled);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("RestartManager", $"Scheduled restart failed: {ex.Message}");
                }
                finally
                {
                    UpdateScheduleTimer(); // Schedule next restart regardless of outcome
                }
            });
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Error in scheduled restart: {ex.Message}");
            UpdateScheduleTimer(); // Ensure timer is rescheduled
        }
    }

    private static void TryMigrateLegacySchedule()
    {
        try
        {
            if (_mainForm is null ||
                !File.Exists(ScheduleConfigPath) ||
                MainConfigContainsRestartSchedule())
            {
                return;
            }
            var mainForm = _mainForm;

            var json = File.ReadAllText(ScheduleConfigPath);
            var legacy = JsonSerializer.Deserialize<RestartScheduleConfig>(json);
            if (legacy is null || !legacy.Enabled)
                return;

            var cron = legacy.ResolveCronExpression();
            CronSchedule.Parse(cron);

            void Apply()
            {
                mainForm.Config.Hub.ScheduledRestartEnabled = true;
                mainForm.Config.Hub.RestartCronSchedule = cron;
                mainForm.SaveCurrentConfig();
            }

            if (mainForm.InvokeRequired)
                mainForm.Invoke((Action)Apply);
            else
                Apply();

            LogUtil.LogInfo(
                "RestartManager",
                $"Migrated legacy restart_schedule.json to the Automation settings using cron '{cron}'.");
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Failed to migrate legacy restart schedule: {ex.Message}");
        }
    }

    private static bool MainConfigContainsRestartSchedule()
    {
        try
        {
            if (!File.Exists(ConfigLoader.ConfigPath))
                return false;

            using var document = JsonDocument.Parse(File.ReadAllText(ConfigLoader.ConfigPath));
            return document.RootElement.TryGetProperty(nameof(ProgramConfig.Hub), out var hub) &&
                   hub.TryGetProperty(nameof(BaseConfig.ScheduledRestartEnabled), out _);
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region Manual Restart Management
    public static async Task<RestartResult> TriggerManualRestartAsync()
    {
        return await ExecuteFullRestartAsync(RestartReason.Manual);
    }
    #endregion

    #region Core Restart Logic
    private static async Task<RestartResult> ExecuteFullRestartAsync(RestartReason reason)
    {
        if (_mainForm == null)
        {
            return new RestartResult { Success = false, Error = "Main form not initialized" };
        }

        lock (_stateLock)
        {
            if (_currentState != RestartState.Idle)
            {
                return new RestartResult { Success = false, Error = "Restart already in progress" };
            }
            _currentState = RestartState.Preparing;
            _restartCts = new CancellationTokenSource();
        }

        var result = new RestartResult { Reason = reason };
        
        try
        {
            LogUtil.LogInfo("RestartManager", $"Starting {reason.ToString().ToLower()} restart process");

            // Phase 1: Discover all instances
            SetState(RestartState.DiscoveringInstances);
            var instances = DiscoverAllInstances();
            result.TotalInstances = instances.Count;
            
            LogUtil.LogInfo("RestartManager", $"Found {instances.Count} instances to restart");

            // Phase 2: Idle all bots
            SetState(RestartState.IdlingBots);
            await IdleAllBotsAsync(instances);

            // Phase 3: Wait for bots to become idle
            SetState(RestartState.WaitingForIdle);
            var allIdle = await WaitForBotsIdleAsync(instances);
            if (!allIdle)
            {
                LogUtil.LogInfo("RestartManager", "Timeout waiting for bots to idle, forcing stop");
                await ForceStopAllBotsAsync(instances);
            }

            // Phase 4: Restart slave instances
            SetState(RestartState.RestartingSlaves);
            var slaves = instances.Where(i => i.ProcessId != Environment.ProcessId).ToList();
            await RestartSlaveInstancesAsync(slaves, result);

            // Phase 5: Restart master instance
            SetState(RestartState.RestartingMaster);
            var master = instances.FirstOrDefault(i => i.ProcessId == Environment.ProcessId);
            if (master != null)
            {
                await RestartMasterInstanceAsync(result);
            }

            result.Success = true;
            
            LogUtil.LogInfo("RestartManager", $"{reason} restart completed successfully");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            LogUtil.LogError("RestartManager", $"{reason} restart failed: {ex.Message}");
        }
        finally
        {
            SetState(RestartState.Idle);
            _restartCts?.Dispose();
            _restartCts = null;
        }

        return result;
    }

    private static void SetState(RestartState newState)
    {
        lock (_stateLock)
        {
            _currentState = newState;
        }
        LogUtil.LogInfo("RestartManager", $"Restart state: {newState}");
    }

    private static List<InstanceInfo> DiscoverAllInstances()
    {
        var instances = new List<InstanceInfo>
        {
            new InstanceInfo
            {
                ProcessId = Environment.ProcessId,
                Port = _tcpPort,
                IsMaster = true
            }
        };

        try
        {
            var processes = Process.GetProcessesByName("PokeBot")
                .Where(p => p.Id != Environment.ProcessId);

            foreach (var process in processes)
            {
                try
                {
                    var instance = CreateInstanceFromProcess(process);
                    if (instance != null)
                    {
                        instances.Add(instance);
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("RestartManager", $"Failed to create instance from process {process.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Error discovering instances: {ex.Message}");
        }

        return instances;
    }

    private static InstanceInfo? CreateInstanceFromProcess(Process process)
    {
        try
        {
            var exePath = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return null;

            var portFile = Path.Combine(Path.GetDirectoryName(exePath)!, $"PokeBot_{process.Id}.port");
            if (!File.Exists(portFile))
                return null;

            var portText = File.ReadAllText(portFile).Trim();
            // Port file now contains TCP port on first line, web port on second line (for slaves)
            var lines = portText.Split('\n', '\r').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (lines.Length == 0 || !int.TryParse(lines[0], out var port))
                return null;

            return new InstanceInfo
            {
                ProcessId = process.Id,
                Port = port,
                IsMaster = false
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task IdleAllBotsAsync(List<InstanceInfo> instances)
    {
        await ExecuteCommandOnAllInstancesAsync(instances, BotControlCommand.Idle, "idle");
    }
    
    private static async Task ExecuteCommandOnAllInstancesAsync(List<InstanceInfo> instances, BotControlCommand command, string commandName)
    {
        var tasks = instances.Select(instance => ExecuteCommandOnInstanceAsync(instance, command, commandName));
        await Task.WhenAll(tasks);
    }
    
    private static async Task ExecuteCommandOnInstanceAsync(InstanceInfo instance, BotControlCommand command, string commandName)
    {
        try
        {
            if (instance.IsMaster)
            {
                ExecuteLocalCommand(command);
                LogUtil.LogInfo("RestartManager", $"Sent {commandName} command to local bots");
            }
            else
            {
                var response = await Task.Run(() => BotServer.QueryRemote(instance.Port, $"{commandName.ToUpper()}ALL"));
                if (response.StartsWith("ERROR"))
                {
                    LogUtil.LogError("RestartManager", $"Failed to {commandName} bots on port {instance.Port}: {response}");
                }
                else
                {
                    LogUtil.LogInfo("RestartManager", $"Sent {commandName} command to port {instance.Port}");
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Error {commandName}ing instance {instance.ProcessId} on port {instance.Port}: {ex.Message}");
        }
    }
    
    private static void ExecuteLocalCommand(BotControlCommand command)
    {
        _mainForm!.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
        {
            var sendAllMethod = _mainForm.GetType().GetMethod("SendAll",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            sendAllMethod?.Invoke(_mainForm, [command]);
        }));
    }

    private static async Task<bool> WaitForBotsIdleAsync(List<InstanceInfo> instances)
    {
        var timeout = DateTime.Now.AddMinutes(3);
        var lastLogTime = DateTime.Now;

        while (DateTime.Now < timeout)
        {
            var allIdle = await CheckAllBotsIdleAsync(instances);
            if (allIdle)
            {
                LogUtil.LogInfo("RestartManager", "All bots are now idle");
                return true;
            }

            // Log progress every 10 seconds
            if ((DateTime.Now - lastLogTime).TotalSeconds >= 10)
            {
                var remaining = (int)(timeout - DateTime.Now).TotalSeconds;
                LogUtil.LogInfo("RestartManager", $"Still waiting for bots to idle... {remaining}s remaining");
                lastLogTime = DateTime.Now;
            }

            await Task.Delay(2000);
        }

        return false;
    }

    private static async Task<bool> CheckAllBotsIdleAsync(List<InstanceInfo> instances)
    {
        try
        {
            var tasks = instances.Select(instance => CheckInstanceBotsIdleAsync(instance));
            var results = await Task.WhenAll(tasks);
            return results.All(idle => idle);
        }
        catch
        {
            return false;
        }
    }
    
    private static async Task<bool> CheckInstanceBotsIdleAsync(InstanceInfo instance)
    {
        try
        {
            if (instance.IsMaster)
            {
                return CheckLocalBotsIdle();
            }
            else
            {
                return await CheckRemoteBotsIdleAsync(instance.Port);
            }
        }
        catch
        {
            return false;
        }
    }
    
    private static bool CheckLocalBotsIdle()
    {
        var flpBotsField = _mainForm!.GetType().GetField("FLP_Bots",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (flpBotsField?.GetValue(_mainForm) is FlowLayoutPanel flpBots)
        {
            var controllers = flpBots.Controls.OfType<BotController>().ToList();
            return controllers.All(c =>
            {
                var state = c.ReadBotState();
                return state == "IDLE" || state == "STOPPED";
            });
        }
        return true;
    }
    
    private static async Task<bool> CheckRemoteBotsIdleAsync(int port)
    {
        return await Task.Run(() =>
        {
            var botsResponse = BotServer.QueryRemote(port, "LISTBOTS");
            if (!botsResponse.StartsWith("{") || !botsResponse.Contains("Bots"))
                return true;
                
            try
            {
                var botsData = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(botsResponse);
                if (botsData?.ContainsKey("Bots") == true)
                {
                    return botsData["Bots"].All(b =>
                    {
                        if (b.TryGetValue("Status", out var status))
                        {
                            var statusStr = status?.ToString()?.ToUpperInvariant() ?? "";
                            return statusStr == "IDLE" || statusStr == "STOPPED";
                        }
                        return true;
                    });
                }
            }
            catch
            {
                // If we can't parse the response, assume not idle
                return false;
            }
            return true;
        });
    }

    private static async Task ForceStopAllBotsAsync(List<InstanceInfo> instances)
    {
        LogUtil.LogInfo("RestartManager", "Force stopping all bots due to idle timeout");
        await ExecuteCommandOnAllInstancesAsync(instances, BotControlCommand.Stop, "stop");
    }

    private static async Task RestartSlaveInstancesAsync(List<InstanceInfo> slaves, RestartResult result)
    {
        foreach (var slave in slaves)
        {
            var instanceResult = new InstanceRestartResult
            {
                Port = slave.Port,
                ProcessId = slave.ProcessId
            };

            try
            {
                LogUtil.LogInfo("RestartManager", $"Restarting slave instance on port {slave.Port}...");
                
                // First ensure all bots are stopped on this instance
                var stopResponse = BotServer.QueryRemote(slave.Port, "STOPALL");
                LogUtil.LogInfo("RestartManager", $"Stop command sent to port {slave.Port}: {stopResponse}");
                await Task.Delay(1000);

                var response = BotServer.QueryRemote(slave.Port, "SELFRESTARTALL");
                if (!response.StartsWith("ERROR"))
                {
                    instanceResult.Success = true;
                    LogUtil.LogInfo("RestartManager", $"Restart command sent to port {slave.Port}");

                    // Wait for process termination
                    var terminated = await WaitForProcessTerminationAsync(slave.ProcessId, 30);
                    if (terminated)
                    {
                        LogUtil.LogInfo("RestartManager", $"Process {slave.ProcessId} terminated successfully");
                        
                        // Wait for instance to come back online
                        var backOnline = await WaitForInstanceOnlineAsync(slave.Port, 60);
                        if (backOnline)
                        {
                            LogUtil.LogInfo("RestartManager", $"Instance on port {slave.Port} is back online");
                        }
                        else
                        {
                            LogUtil.LogError("RestartManager", $"Instance on port {slave.Port} did not come back online");
                        }
                    }
                    else
                    {
                        LogUtil.LogError("RestartManager", $"Process {slave.ProcessId} did not terminate in time");
                    }
                }
                else
                {
                    instanceResult.Error = response;
                    LogUtil.LogError("RestartManager", $"Failed to restart port {slave.Port}: {response}");
                }
            }
            catch (Exception ex)
            {
                instanceResult.Error = ex.Message;
                LogUtil.LogError("RestartManager", $"Error restarting port {slave.Port}: {ex.Message}");
            }

            result.InstanceResults.Add(instanceResult);
        }
    }

    private static async Task RestartMasterInstanceAsync(RestartResult result)
    {
        LogUtil.LogInfo("RestartManager", "Preparing to restart master instance");
        
        // Save current process IDs before restart
        SavePreRestartProcessIds();
        
        // Create restart flag for post-restart detection
        File.WriteAllText(RestartFlagPath, DateTime.Now.ToString());
        
        result.MasterRestarting = true;
        
        // Give a moment for any pending operations
        await Task.Delay(2000);
        
        // Restart the application
        _mainForm!.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
        {
            Application.Restart();
        }));
    }
    
    private static void SavePreRestartProcessIds()
    {
        try
        {
            var pids = new List<int>();
            
            // Add current process ID
            pids.Add(Environment.ProcessId);
            
            // Add all PokeBot process IDs
            var processes = Process.GetProcessesByName("PokeBot");
            foreach (var process in processes)
            {
                try
                {
                    pids.Add(process.Id);
                }
                catch { }
            }
            
            var json = JsonSerializer.Serialize(pids);
            File.WriteAllText(PreRestartPidsPath, json);
            LogUtil.LogInfo("RestartManager", $"Saved {pids.Count} process IDs before restart");
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Failed to save pre-restart process IDs: {ex.Message}");
        }
    }

    private static async Task<bool> WaitForProcessTerminationAsync(int processId, int timeoutSeconds)
    {
        var endTime = DateTime.Now.AddSeconds(timeoutSeconds);

        while (DateTime.Now < endTime)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return true;
            }
            catch (ArgumentException)
            {
                // Process not found = terminated
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<bool> WaitForInstanceOnlineAsync(int port, int timeoutSeconds)
    {
        var endTime = DateTime.Now.AddSeconds(timeoutSeconds);

        while (DateTime.Now < endTime)
        {
            if (IsPortOpen(port))
            {
                await Task.Delay(1000); // Give it a moment to fully initialize
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (success)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region Post-Restart Startup
    private static void CheckPostRestartStartup()
    {
        try
        {
            if (!File.Exists(RestartFlagPath))
                return;

            LogUtil.LogInfo("RestartManager", "Post-restart startup detected. Cleaning up the previous process...");
            File.Delete(RestartFlagPath);
            
            // Kill any lingering old processes
            KillOldProcesses();
            LogUtil.LogInfo(
                "RestartManager",
                _mainForm?.Config.Hub.AutoStartBots == true
                    ? "Configured bots are started by the main startup sequence."
                    : "Automatic bot startup is disabled; bots will remain stopped.");
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Error in post-restart startup: {ex.Message}");
        }
    }
    
    private static void KillOldProcesses()
    {
        try
        {
            if (!File.Exists(PreRestartPidsPath))
                return;
                
            var json = File.ReadAllText(PreRestartPidsPath);
            var oldPids = JsonSerializer.Deserialize<List<int>>(json);
            File.Delete(PreRestartPidsPath); // Clean up the file
            
            if (oldPids == null || oldPids.Count == 0)
                return;
                
            LogUtil.LogInfo("RestartManager", $"Checking for {oldPids.Count} old process IDs to clean up");
            
            var currentPid = Environment.ProcessId;
            var killedCount = 0;
            
            foreach (var pid in oldPids)
            {
                // Don't kill the current process
                if (pid == currentPid)
                    continue;
                    
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (process != null && !process.HasExited)
                    {
                        LogUtil.LogInfo("RestartManager", $"Killing lingering old process {pid} ({process.ProcessName})");
                        process.Kill();
                        process.WaitForExit(5000); // Wait up to 5 seconds for it to exit
                        killedCount++;
                    }
                }
                catch (ArgumentException)
                {
                    // Process doesn't exist, that's fine
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("RestartManager", $"Failed to kill old process {pid}: {ex.Message}");
                }
            }
            
            if (killedCount > 0)
            {
                LogUtil.LogInfo("RestartManager", $"Killed {killedCount} lingering old processes");
                // Give a moment for processes to fully terminate
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError("RestartManager", $"Error killing old processes: {ex.Message}");
        }
    }
    #endregion
}

#region Data Classes
public enum RestartState
{
    Idle,
    Preparing,
    DiscoveringInstances,
    IdlingBots,
    WaitingForIdle,
    RestartingSlaves,
    RestartingMaster
}

public enum RestartReason
{
    Manual,
    Scheduled
}

public class RestartScheduleConfig
{
    public bool Enabled { get; set; }
    public string Cron { get; set; } = string.Empty;

    // Kept for compatibility with existing web clients and restart_schedule.json.
    public string? Time { get; set; }

    internal static RestartScheduleConfig FromHub(PokeTradeHubConfig hub)
    {
        var cron = hub.RestartCronSchedule;
        return new RestartScheduleConfig
        {
            Enabled = hub.ScheduledRestartEnabled,
            Cron = cron,
            Time = CronSchedule.TryGetDailyTime(cron, out var time)
                ? $"{time.Hours:00}:{time.Minutes:00}"
                : null,
        };
    }

    internal string ResolveCronExpression()
    {
        if (!string.IsNullOrWhiteSpace(Cron))
        {
            return string.Join(
                ' ',
                Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (TimeSpan.TryParse(Time, out var legacyTime) &&
            legacyTime >= TimeSpan.Zero &&
            legacyTime < TimeSpan.FromDays(1))
        {
            return CronSchedule.FromDailyTime(legacyTime);
        }

        return BaseConfig.DefaultRestartCronSchedule;
    }
}

public class RestartResult
{
    public bool Success { get; set; }
    public RestartReason Reason { get; set; }
    public int TotalInstances { get; set; }
    public bool MasterRestarting { get; set; }
    public string? Error { get; set; }
    public List<InstanceRestartResult> InstanceResults { get; set; } = new();
}

public class InstanceRestartResult
{
    public int Port { get; set; }
    public int ProcessId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class InstanceInfo
{
    public int ProcessId { get; set; }
    public int Port { get; set; }
    public bool IsMaster { get; set; }
}
#endregion
