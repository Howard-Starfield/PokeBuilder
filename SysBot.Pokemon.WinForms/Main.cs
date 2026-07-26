using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Helpers;
using SysBot.Pokemon.WinForms.Properties;
using SysBot.Pokemon.WinForms.Helpers;
using SysBot.Pokemon.Z3;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public sealed partial class Main : Form
    {
        // Windows API for forcing window frame update
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, 
            int X, int Y, int cx, int cy, uint uFlags);
        
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        
        // Performance optimization flags
        private bool _suspendLayout = false;
        private bool _deferredInvalidate = false;
        private DateTime _lastInvalidate = DateTime.MinValue;
        private const int INVALIDATE_THROTTLE_MS = 16; // 60 FPS max
        private readonly List<PokeBotState> Bots = [];

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ProgramConfig Config { get; set; } = null!;

        private IPokeBotRunner RunningEnvironment { get; set; } = null!;
        internal IPokeBotRunner? Runner => RunningEnvironment;

        public readonly ISwitchConnectionAsync? SwitchConnection;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static volatile bool IsUpdating = false;
        private System.Windows.Forms.Timer? _autoSaveTimer;
        private System.Windows.Forms.Timer? _logCleanupTimer;
        private readonly object _configSaveLock = new();
        private bool _isFormLoading = true;
        private int _configCategoryHotIndex = ListBox.NoMatches;

        private SearchManager _searchManager = null!;
        private TextBoxForwarder _textBoxForwarder = null!;

        internal bool hasUpdate = false;
        private bool _isRestoringFromTray = false;
        private LinearGradientBrush? _logoBrush;
        private Image? _currentModeImage = null;

        private readonly Color CuztomBackground = Color.FromArgb(27, 40, 56);
        private readonly Color CuztomDarkBackground = Color.FromArgb(22, 32, 45);
        private readonly Color CuztomAccent = Color.FromArgb(102, 192, 244);
        private readonly Color CuztomText = Color.FromArgb(239, 239, 239);
        private readonly Color CuztomSubText = Color.FromArgb(139, 179, 217);

        public Main()
        {
            // Enable DPI awareness
            this.AutoScaleMode = AutoScaleMode.Dpi;
            
            InitializeComponent();
            
            // Performance optimizations
            SetStyle(ControlStyles.AllPaintingInWmPaint | 
                    ControlStyles.UserPaint | 
                    ControlStyles.DoubleBuffer | 
                    ControlStyles.ResizeRedraw |
                    ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
            
            // Apply dark mode to the main window
            DarkModeHelper.SetDarkMode(this.Handle);
            
            Load += async (sender, e) => await InitializeAsync();

            TC_Main = new TabControl { Visible = false };
            Tab_Bots = new TabPage();
            Tab_Hub = new TabPage();
            Tab_Logs = new TabPage();
            TC_Main.TabPages.AddRange([Tab_Bots, Tab_Hub, Tab_Logs]);
            TC_Main.SendToBack();

            _searchManager = new SearchManager(RTB_Logs, searchStatusLabel);
            ConfigureSearchEventHandlers();
        }

        private void ConfigureSearchEventHandlers()
        {
            btnCaseSensitive.CheckedChanged += (s, e) => _searchManager.ToggleCaseSensitive();
            btnRegex.CheckedChanged += (s, e) => _searchManager.ToggleRegex();
            btnWholeWord.CheckedChanged += (s, e) => _searchManager.ToggleWholeWord();
        }

        private void CreateNewConfig()
        {
            Config = new ProgramConfig();
            RunningEnvironment = GetRunner(Config);
            Config.Hub.Folder.CreateDefaults(ConfigLoader.WorkingDirectory);
        }

        private async Task InitializeAsync()
        {
            if (IsUpdating)
                return;
            string discordName = string.Empty;

            PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();
            UpdateChecker updateChecker = new();

            try
            {
                var (updateAvailable, _, _) = await UpdateChecker.CheckForUpdatesAsync();
                hasUpdate = updateAvailable;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Update check failed: {ex.Message}", "Update");
            }

            var loadResult = ConfigLoader.LoadConfigWithResult(null);
            Config = loadResult.Config;
            foreach (var message in loadResult.Messages)
                LogUtil.LogInfo(message, "Config");

            LogConfig.MaxArchiveFiles = Config.Hub.MaxArchiveFiles;
            LogConfig.LoggingEnabled = Config.Hub.LoggingEnabled;
            Config.Hub.Distribution.CurrentMode = Config.Mode;
            comboBox1.SelectedValue = (int)Config.Mode;

            RunningEnvironment = GetRunner(Config);
            foreach (var bot in Config.Bots)
            {
                bot.Initialize();
                AddBot(bot);
            }

            RTB_Logs.MaxLength = 2_000_000; // Limit to 2MB of text to prevent memory issues
            LoadControls();
            Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "PokeBuilder" : Config.Hub.BotName)} {PokeBot.Version} ({Config.Mode})";
            trayIcon.Text = Text;
            _ = Task.Run(BotMonitor);
            InitUtil.InitializeStubs(Config.Mode);
            _isFormLoading = false;
            UpdateBackgroundImage(Config.Mode);
            UpdateStatusIndicatorColor();
            
            this.ActiveControl = null;
            LogUtil.LogInfo("System", $"Bot initialization complete");
            _ = Task.Run(() =>
            {
                try
                {
                    this.InitWebServer();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to initialize web server: {ex.Message}", "System");
                }
            });
        }

        #region Enhanced Search Implementation

        private void LogSearchBox_TextChanged(object sender, EventArgs e)
        {
            _searchManager.UpdateSearch(logSearchBox.Text);
        }

        private void LogSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    e.SuppressKeyPress = true;
                    if (e.Shift)
                        _searchManager.FindPrevious();
                    else
                        _searchManager.FindNext();
                    break;

                case Keys.Escape:
                    e.SuppressKeyPress = true;
                    _searchManager.ClearSearch();
                    logSearchBox.Clear();
                    break;

                case Keys.F when e.Control:
                    e.SuppressKeyPress = true;
                    logSearchBox.Focus();
                    logSearchBox.SelectAll();
                    break;
            }
        }

        private void RTB_Logs_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.F when e.Control:
                    e.SuppressKeyPress = true;
                    logSearchBox.Focus();
                    logSearchBox.SelectAll();
                    break;

                case Keys.F3:
                    e.SuppressKeyPress = true;
                    if (e.Shift)
                        _searchManager.FindPrevious();
                    else
                        _searchManager.FindNext();
                    break;
            }
        }

        #endregion

        private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Mode switch
        {
            ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(cfg.Hub, new BotFactory8SWSH(), cfg),
            ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(cfg.Hub, new BotFactory8BS(), cfg),
            ProgramMode.LA => new PokeBotRunnerImpl<PA8>(cfg.Hub, new BotFactory8LA(), cfg),
            ProgramMode.SV => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV(), cfg),
            ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(cfg.Hub, new BotFactory7LGPE(), cfg),
            ProgramMode.LZA => new PokeBotRunnerImpl<PA9>(cfg.Hub, new BotFactory9LZA(), cfg),
            _ => throw new IndexOutOfRangeException("Unsupported mode."),
        };

        private async Task BotMonitor()
        {
            while (!Disposing)
            {
                try
                {
                    // Only update UI if form is visible and not suspended
                    if (WindowState != FormWindowState.Minimized && !_suspendLayout)
                    {
                        // Batch updates to reduce UI thread blocking
                        var controllers = FLP_Bots.Controls.OfType<BotController>().ToList();
                        if (controllers.Count > 0)
                        {
                            BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                            {
                                SuspendLayout();
                                foreach (var c in controllers)
                                    c.ReadState();
                                ResumeLayout(false);
                            }));
                        }

                        UpdateControlButtonStates();
                    }

                    if (trayIcon != null && trayIcon.Visible && Config != null)
                    {
                        // Get bot counts in a thread-safe manner
                        int runningBots = 0;
                        int totalBots = 0;

                        if (InvokeRequired)
                        {
                            // Use BeginInvoke to avoid blocking the monitoring thread
                            BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                            {
                                runningBots = FLP_Bots.Controls.OfType<BotController>().Count(c => c.GetBot()?.IsRunning ?? false);
                                totalBots = FLP_Bots.Controls.OfType<BotController>().Count();
                                
                                // Update tray icon text from UI thread
                                string botTitle = string.IsNullOrWhiteSpace(Config.Hub.BotName) ? "PokéBot" : Config.Hub.BotName;
                                trayIcon.Text = totalBots == 0
                                    ? $"{botTitle} - No bots configured"
                                    : $"{botTitle} - {runningBots}/{totalBots} bots running";
                            }));
                        }
                        else
                        {
                            runningBots = FLP_Bots.Controls.OfType<BotController>().Count(c => c.GetBot()?.IsRunning ?? false);
                            totalBots = FLP_Bots.Controls.OfType<BotController>().Count();
                            
                            string botTitle = string.IsNullOrWhiteSpace(Config.Hub.BotName) ? "PokéBot" : Config.Hub.BotName;
                            trayIcon.Text = totalBots == 0
                                ? $"{botTitle} - No bots configured"
                                : $"{botTitle} - {runningBots}/{totalBots} bots running";
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"BotMonitor error: {ex.Message}", "Monitor");
                }
                await Task.Delay(3_000).ConfigureAwait(false); // Reduced frequency for better performance
            }
        }

        private void UpdateControlButtonStates()
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateControlButtonStates());
                return;
            }

            var botControllers = FLP_Bots.Controls.OfType<BotController>().ToList(); // Cache the collection
            var runningBots = botControllers.Count(c => c.GetBot()?.IsRunning ?? false);
            var totalBots = botControllers.Count;
            var anyRunning = runningBots > 0;

            if (btnStart?.Tag is EnhancedButtonAnimationState startState)
            {
                startState.IsActive = !anyRunning && totalBots > 0;
            }

            if (btnStop?.Tag is EnhancedButtonAnimationState stopState)
            {
                stopState.IsActive = anyRunning;
            }

            if (btnReboot?.Tag is EnhancedButtonAnimationState rebootState)
            {
                rebootState.IsActive = anyRunning;
            }
        }

        private void LoadControls()
        {
            ConfigurationDropDownTheme.Register(typeof(ProgramConfig).Assembly);
            PG_Hub.SelectedObject = RunningEnvironment.Config;
            PG_Hub.PropertyValueChanged += (_, _) =>
            {
                if (!_isFormLoading)
                    SaveCurrentConfig();
            };
            InitializeConfigurationCategories();
            ApplyConfigurationFontScale(Config.ConfigurationFontScalePercent);
            DarkModeHelper.ApplyDarkModeToControlTree(PG_Hub);
            ConfigurationPropertyGridTheme.Apply(PG_Hub);
            DarkModeHelper.ApplyDarkModeToControlTree(configCategoryList);
            DarkModeHelper.ApplyDarkModeToControlTree(FLP_Bots);
            DarkModeHelper.ApplyDarkModeToControlTree(RTB_Logs);
            _autoSaveTimer = new System.Windows.Forms.Timer
            {
                Interval = 10_000,
                Enabled = true
            };
            _autoSaveTimer.Tick += (_, _) => SaveCurrentConfig();
            var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType))).Where(z => RunningEnvironment.SupportsRoutine(z));
            var list = routines.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
            CB_Routine.DisplayMember = nameof(ComboItem.Text);
            CB_Routine.ValueMember = nameof(ComboItem.Value);
            CB_Routine.DataSource = list;
            CB_Routine.SelectedValue = (int)PokeRoutineType.FlexTrade;

            var protocols = (SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol));
            var listP = protocols.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
            CB_Protocol.DisplayMember = nameof(ComboItem.Text);
            CB_Protocol.ValueMember = nameof(ComboItem.Value);
            CB_Protocol.DataSource = listP;
            CB_Protocol.SelectedIndex = (int)SwitchProtocol.WiFi;

            var gameModes = Enum.GetValues(typeof(ProgramMode))
                .Cast<ProgramMode>()
                .Where(m => m != ProgramMode.None)
                .Select(mode => new { Text = mode.ToString(), Value = (int)mode })
                .ToList();
            comboBox1.DisplayMember = "Text";
            comboBox1.ValueMember = "Value";
            comboBox1.DataSource = gameModes;
            comboBox1.SelectedValue = (int)Config.Mode;
            
            // Apply enhanced styling to the game selector
            ConfigureGameSelector();

            _textBoxForwarder = new TextBoxForwarder(RTB_Logs);
            LogUtil.Forwarders.Add(_textBoxForwarder);

            // Initialize log cleanup timer - runs every 30 minutes
            _logCleanupTimer = new System.Windows.Forms.Timer
            {
                Interval = 30 * 60 * 1000, // 30 minutes
                Enabled = true
            };
            _logCleanupTimer.Tick += (s, e) =>
            {
                try
                {
                    // Clean up logs if they're getting too large
                    if (RTB_Logs.TextLength > RTB_Logs.MaxLength * 0.8)
                    {
                        LogUtil.LogInfo("Performing automatic log cleanup to maintain performance", "System");

                        // Keep only the last 25% of logs
                        BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                        {
                            var lines = RTB_Logs.Lines;
                            var linesToKeep = lines.Length / 4;
                            RTB_Logs.Lines = lines[^linesToKeep..];
                            _searchManager.ClearSearch(); // Clear search after cleanup
                        }));
                    }

                    // Also clean up old log files on disk
                    CleanupOldLogFiles();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Log cleanup failed: {ex.Message}", "System");
                }
            };
        }

        private void ConfigFontDecrease_Click(object? sender, EventArgs e) => AdjustConfigurationFontScale(-10);

        private void ConfigFontIncrease_Click(object? sender, EventArgs e) => AdjustConfigurationFontScale(10);

        private void ConfigFontReset_Click(object? sender, EventArgs e) =>
            ApplyConfigurationFontScale(ProgramConfig.DefaultConfigurationFontScalePercent, persist: true);

        private void AdjustConfigurationFontScale(int delta) =>
            ApplyConfigurationFontScale(Config.ConfigurationFontScalePercent + delta, persist: true);

        private void InitializeConfigurationCategories(string? selectedName = null)
        {
            var hub = RunningEnvironment.Config;
            var categories = new ConfigurationCategoryItem[]
            {
                new("General", "Core bot identity, logging, and idle behavior.",
                    new PropertySubsetView(hub,
                    [
                        nameof(PokeTradeHubConfig.BotName),
                        nameof(BaseConfig.AntiIdle),
                        nameof(BaseConfig.LoggingEnabled),
                        nameof(BaseConfig.MaxArchiveFiles),
                    ])),
                new("Discord", "Discord connection, commands, roles, channels, and status settings.", hub.Discord),
                new("Trade", "Trade behavior, request handling, embeds, limits, and statistics.", hub.Trade),
                new("Trade Queue", "Queue timing, user priority, and favored-user behavior.",
                    new PropertySubsetView(hub,
                    [
                        nameof(PokeTradeHubConfig.Queues),
                        nameof(PokeTradeHubConfig.Favoritism),
                    ])),
                new("Legality", "Pokémon generation and legality-checking behavior.", hub.Legality),
                new("Distribution", "Idle distribution pools and automatic distribution behavior.", hub.Distribution),
                new("Integrations", "Streaming, Twitch, and YouTube integration settings.",
                    new PropertySubsetView(hub,
                    [
                        nameof(PokeTradeHubConfig.Stream),
                        nameof(PokeTradeHubConfig.Twitch),
                        nameof(PokeTradeHubConfig.YouTube),
                    ])),
                new("Web API", "Web control panel and website trade integration settings.",
                    new PropertySubsetView(hub,
                    [
                        nameof(PokeTradeHubConfig.WebServer),
                        nameof(PokeTradeHubConfig.WebTrade),
                    ])),
                new("Recovery", "Connection timing and automatic recovery behavior.",
                    new PropertySubsetView(hub,
                    [
                        nameof(PokeTradeHubConfig.Timings),
                        nameof(PokeTradeHubConfig.Recovery),
                    ])),
                new("Advanced", "Folders, abuse monitoring, and diagnostic startup options.",
                    new PropertySubsetView(hub,
                    [
                        nameof(PokeTradeHubConfig.Folder),
                        nameof(PokeTradeHubConfig.TradeAbuse),
                        nameof(BaseConfig.SkipConsoleBotCreation),
                    ])),
                new(
                    "All settings",
                    "The complete configuration tree, grouped by its original categories.",
                    hub,
                    UsePropertyGrid: true),
            };

            configCategoryList.BeginUpdate();
            try
            {
                configCategoryList.Items.Clear();
                configCategoryList.Items.AddRange(categories);
                var selectedIndex = string.IsNullOrWhiteSpace(selectedName)
                    ? 0
                    : Array.FindIndex(categories, item => item.Name == selectedName);
                configCategoryList.SelectedIndex = Math.Max(0, selectedIndex);
            }
            finally
            {
                configCategoryList.EndUpdate();
            }
        }

        private void ConfigCategoryList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (configCategoryList.SelectedItem is not ConfigurationCategoryItem selected)
                return;

            configSectionTitle.Text = selected.Name;
            configSectionDescription.Text = selected.Description;
            if (selected.UsePropertyGrid)
            {
                configSettingsTree.Visible = false;
                pgContainer.Visible = true;
                pgContainer.BringToFront();
                PG_Hub.SelectedObject = selected.View;
                PG_Hub.Refresh();
            }
            else
            {
                pgContainer.Visible = false;
                configSettingsTree.Visible = true;
                configSettingsTree.BringToFront();
                configSettingsTree.Bind(
                    selected.View,
                    Config.ConfigurationFontScalePercent,
                    SaveCurrentConfig,
                    OpenAllSettings);
            }
            configCategoryList.Invalidate();
        }

        private void OpenAllSettings()
        {
            for (var index = 0; index < configCategoryList.Items.Count; index++)
            {
                if (configCategoryList.Items[index] is ConfigurationCategoryItem { UsePropertyGrid: true })
                {
                    configCategoryList.SelectedIndex = index;
                    return;
                }
            }
        }

        private void ConfigCategoryList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= configCategoryList.Items.Count)
                return;

            var selected = (e.State & DrawItemState.Selected) != 0;
            var hot = e.Index == _configCategoryHotIndex;
            using (var backgroundBrush = new SolidBrush(ConfigurationTheme.Surface))
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

            var rowBounds = Rectangle.Inflate(e.Bounds, -5, -3);
            var rowBackground = selected
                ? ConfigurationTheme.SurfaceSelected
                : hot
                    ? ConfigurationTheme.SurfaceHover
                    : ConfigurationTheme.Surface;
            using (var rowPath = new GraphicsPath())
            using (var rowBrush = new SolidBrush(rowBackground))
            {
                rowPath.AddRoundedRectangle(rowBounds, 7);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(rowBrush, rowPath);
            }

            if (selected)
            {
                using var accentBrush = new SolidBrush(ConfigurationTheme.Accent);
                e.Graphics.FillRectangle(
                    accentBrush,
                    rowBounds.Left,
                    rowBounds.Top + 5,
                    3,
                    rowBounds.Height - 10);
                var markerSize = Math.Clamp((int)Math.Round(configCategoryList.Font.GetHeight() * 1.05F), 16, 24);
                DrawPokeBall(
                    e.Graphics,
                    new Rectangle(
                        rowBounds.Left + 14,
                        e.Bounds.Top + (e.Bounds.Height - markerSize) / 2,
                        markerSize,
                        markerSize));
            }
            else
            {
                var markerSize = hot ? 6 : 5;
                var center = new Point(rowBounds.Left + 23, e.Bounds.Top + e.Bounds.Height / 2);
                using var marker = new SolidBrush(
                    hot ? ConfigurationTheme.Accent : ConfigurationTheme.TextMuted);
                e.Graphics.FillEllipse(
                    marker,
                    center.X - markerSize / 2,
                    center.Y - markerSize / 2,
                    markerSize,
                    markerSize);
            }

            var item = (ConfigurationCategoryItem)configCategoryList.Items[e.Index];
            var textBounds = new Rectangle(
                e.Bounds.Left + 44,
                e.Bounds.Top,
                e.Bounds.Width - 52,
                e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                item.Name,
                configCategoryList.Font,
                textBounds,
                selected || hot ? ConfigurationTheme.TextPrimary : ConfigurationTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if ((e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    Rectangle.Inflate(rowBounds, -3, -3),
                    ConfigurationTheme.TextPrimary,
                    rowBackground);
        }

        private void ConfigCategoryList_MouseMove(object? sender, MouseEventArgs e)
        {
            var nextHotIndex = configCategoryList.IndexFromPoint(e.Location);
            if (nextHotIndex == _configCategoryHotIndex)
                return;

            var previousHotIndex = _configCategoryHotIndex;
            _configCategoryHotIndex = nextHotIndex;
            if (previousHotIndex != ListBox.NoMatches)
                configCategoryList.Invalidate(configCategoryList.GetItemRectangle(previousHotIndex));
            if (_configCategoryHotIndex != ListBox.NoMatches)
                configCategoryList.Invalidate(configCategoryList.GetItemRectangle(_configCategoryHotIndex));
        }

        private void ConfigCategoryList_MouseLeave(object? sender, EventArgs e)
        {
            if (_configCategoryHotIndex == ListBox.NoMatches)
                return;

            var previousHotIndex = _configCategoryHotIndex;
            _configCategoryHotIndex = ListBox.NoMatches;
            configCategoryList.Invalidate(configCategoryList.GetItemRectangle(previousHotIndex));
        }

        private void ApplyConfigurationFontScale(int requestedPercent, bool persist = false)
        {
            var percent = Math.Clamp(
                requestedPercent,
                ProgramConfig.MinConfigurationFontScalePercent,
                ProgramConfig.MaxConfigurationFontScalePercent);

            Config.ConfigurationFontScalePercent = percent;
            ConfigurationDropDownTheme.SetScale(percent);
            lblConfigFontScale.Text = $"{percent}%";
            btnConfigFontDecrease.Enabled = percent > ProgramConfig.MinConfigurationFontScalePercent;
            btnConfigFontIncrease.Enabled = percent < ProgramConfig.MaxConfigurationFontScalePercent;

            ReplaceControlFont(PG_Hub, new Font("Segoe UI", 12F * percent / 100F, FontStyle.Regular, GraphicsUnit.Point));
            ReplaceControlFont(configCategoryList, new Font("Segoe UI Semibold", 11F * percent / 100F, FontStyle.Regular, GraphicsUnit.Point));
            configSettingsTree.SetScale(percent);
            configCategoryList.ItemHeight = Math.Clamp(
                (int)Math.Ceiling(configCategoryList.Font.GetHeight() * 2.1F),
                44,
                82);
            configCategoryList.Invalidate();
            PG_Hub.Refresh();

            if (persist && !_isFormLoading)
                SaveCurrentConfig();
        }

        private static void ReplaceControlFont(Control control, Font replacement)
        {
            var previous = control.Font;

            // WinForms ignores an assignment when the new Font is value-equal
            // to the current one. Disposing the current instance afterward
            // leaves the control holding an invalid GDI resource.
            if (previous.Equals(replacement))
            {
                replacement.Dispose();
                return;
            }

            control.Font = replacement;
            previous?.Dispose();
        }

        private ProgramConfig GetCurrentConfiguration()
        {
            if (Config == null)
            {
                throw new InvalidOperationException("Config has not been initialized because a valid license was not entered.");
            }
            Config.Bots = [.. Bots];
            return Config;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsUpdating) return;

            // Remove log forwarders to prevent memory leaks
            LogUtil.Forwarders.Remove(_textBoxForwarder);

            // Let the form close normally when X button is clicked
            // No longer minimizing to tray on close
            this.StopWebServer();

            try
            {
                string? exePath = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string? dirPath = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(dirPath))
                    {
                        string portInfoPath = Path.Combine(dirPath, $"MergeBot_{Environment.ProcessId}.port");
                        if (File.Exists(portInfoPath))
                            File.Delete(portInfoPath);
                    }
                }
            }
            catch { }

            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Dispose();
            }

            if (_logCleanupTimer != null)
            {
                _logCleanupTimer.Stop();
                _logCleanupTimer.Dispose();
            }

            // Animation timer removed

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            if (_logoBrush != null)
            {
                _logoBrush.Dispose();
                _logoBrush = null;
            }

            SaveCurrentConfig();
            var bots = RunningEnvironment;
            if (!bots.IsRunning)
            {
                bots.Shutdown();
                return;
            }

            async Task WaitUntilNotRunning()
            {
                while (bots.IsRunning)
                    await Task.Delay(10).ConfigureAwait(false);
            }

            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            bots.Shutdown();
            Task.WhenAny(WaitUntilNotRunning(), Task.Delay(5_000)).ConfigureAwait(true).GetAwaiter().GetResult();
        }

        private void SaveCurrentConfig()
        {
            try
            {
                lock (_configSaveLock)
                {
                    var cfg = GetCurrentConfiguration();
                    ProgramConfigPersistence.SaveAtomic(cfg, ConfigLoader.ConfigPath, out _);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to save config: {ex.Message}", "Config");
            }
        }


        private void B_Start_Click(object sender, EventArgs e)
        {
            SaveCurrentConfig();

            LogUtil.LogInfo("Form", "Starting all bots...");
            RunningEnvironment.InitializeStart();
            WebApiExtensions.NotifyBotsStarted(RunningEnvironment);
            SendAll(BotControlCommand.Start);

            SetButtonActiveState(btnStart, true);
            SetButtonActiveState(btnStop, false);
            SetButtonActiveState(btnReboot, false);

            // Keep the Bots tab selected when starting
            foreach (Button navBtn in navButtonsPanel.Controls.OfType<Button>())
            {
                if (navBtn.Tag is NavButtonState state)
                {
                    // Keep Bots tab (index 0) selected, deselect others
                    state.IsSelected = (state.Index == 0);
                    navBtn.Invalidate();
                }
            }

            // Stay on Bots tab instead of switching to Logs
            TransitionPanels(0);
            titleLabel.Text = "Bot Management";

            if (Bots.Count == 0)
                WinFormsUtil.Alert("No bots configured, but all supporting services have been started.");
        }

        private void B_RebootStop_Click(object sender, EventArgs e)
        {
            SetButtonActiveState(btnReboot, true);
            SetButtonActiveState(btnStart, false);
            SetButtonActiveState(btnStop, false);

            Task.Run(async () =>
            {
                try
                {
                    LogUtil.LogInfo("Form", "Starting reset process...");
                    SaveCurrentConfig();

                    // Phase 1: Stop all bots gracefully
                    LogUtil.LogInfo("Form", "Phase 1: Stopping all bots...");
                    SendAll(BotControlCommand.Stop);

                    // Phase 2: Wait for all bots to fully stop
                    var stopTimeout = DateTime.Now.AddSeconds(30);
                    while (DateTime.Now < stopTimeout)
                    {
                        if (AreAllBotsStopped())
                        {
                            LogUtil.LogInfo("Form", "All bots stopped successfully");
                            break;
                        }
                        await Task.Delay(500).ConfigureAwait(false);
                    }

                    if (!AreAllBotsStopped())
                    {
                        LogUtil.LogInfo("Form", "Some bots did not stop in time, forcing stop...");
                        SendAll(BotControlCommand.Stop);
                        await Task.Delay(2000).ConfigureAwait(false);
                    }

                    // Phase 3: Stop all services
                    LogUtil.LogInfo("Form", "Phase 3: Stopping all services...");
                    await Task.Delay(2000).ConfigureAwait(false); // Give services time to fully stop

                    // Phase 4: Reinitialize environment
                    LogUtil.LogInfo("Form", "Phase 4: Reinitializing environment...");
                    RunningEnvironment.InitializeStart();
                    await Task.Delay(1000).ConfigureAwait(false);

                    // Phase 5: Reboot consoles
                    LogUtil.LogInfo("Form", "Phase 5: Rebooting all consoles...");
                    SendAll(BotControlCommand.RebootAndStop);
                    await Task.Delay(8000).ConfigureAwait(false); // Give consoles time to reboot

                    // Phase 6: Restart all bots with staggered timing
                    LogUtil.LogInfo("Form", "Phase 6: Starting all bots...");
                    await StartBotsStaggeredAsync();

                    BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                    {
                        SetButtonActiveState(btnReboot, false);
                        SetButtonActiveState(btnStop, true);
                        SetButtonActiveState(btnStart, false);

                        foreach (Button navBtn in navButtonsPanel.Controls.OfType<Button>())
                        {
                            if (navBtn.Tag is NavButtonState state)
                            {
                                state.IsSelected = false;
                                navBtn.Invalidate();
                            }
                        }

                        if (btnNavLogs.Tag is NavButtonState logsState)
                        {
                            logsState.IsSelected = true;
                            btnNavLogs.Invalidate();
                        }

                        TransitionPanels(2);
                        titleLabel.Text = "System Logs";
                    }));

                    LogUtil.LogInfo("Reset process completed successfully", "Form");

                    if (Bots.Count == 0)
                        WinFormsUtil.Alert("No bots configured, but all supporting services have been issued the reboot command.");
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Reset process failed: {ex.Message}", "Form");
                    BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                    {
                        SetButtonActiveState(btnReboot, false);
                        SetButtonActiveState(btnStop, false);
                        SetButtonActiveState(btnStart, false);
                        WinFormsUtil.Error($"Reset failed: {ex.Message}");
                    }));
                }
            });
        }

        private async void Updater_Click(object sender, EventArgs e)
        {
            var (updateAvailable, _, _) = await UpdateChecker.CheckForUpdatesAsync(forceShow: true);
            hasUpdate = updateAvailable;
        }

        private void SetButtonActiveState(Button button, bool isActive)
        {
            if (button?.Tag is EnhancedButtonAnimationState state)
            {
                state.IsActive = isActive;
                button.Invalidate();
            }
        }

        private void SendAll(BotControlCommand cmd)
        {
            foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                c.SendCommand(cmd, false);

            LogUtil.LogText($"All bots have been issued a command to {cmd}.");
        }

        private void BtnTray_Click(object sender, EventArgs e)
        {
            // Send to Tray - minimizes to system tray
            MinimizeToTray();
        }

        private void UpdateAddButtonPosition()
        {
            if (B_New != null && CB_Routine != null)
            {
                B_New.Location = new Point(CB_Routine.Right + 10, 16);
            }
        }

        private void AddBotPanel_Layout(object sender, EventArgs e)
        {
            UpdateAddButtonPosition();
        }

        private void CB_Routine_SizeChanged(object sender, EventArgs e)
        {
            UpdateAddButtonPosition();
        }

        private void CB_Routine_LocationChanged(object sender, EventArgs e)
        {
            UpdateAddButtonPosition();
        }

        private void FLP_Bots_Scroll(object sender, ScrollEventArgs e)
        {
            FLP_Bots.Invalidate();
        }

        private void FLP_Bots_ControlAdded(object sender, ControlEventArgs e)
        {
            FLP_Bots.Invalidate();
        }

        private void FLP_Bots_ControlRemoved(object sender, ControlEventArgs e)
        {
            FLP_Bots.Invalidate();
        }

        private void BtnClearLogs_Click(object sender, EventArgs e)
        {
            RTB_Logs.Clear();
            _searchManager.ClearSearch();
        }


        private void B_Stop_Click(object sender, EventArgs e)
        {
            var env = RunningEnvironment;
            if (!env.IsRunning && (ModifierKeys & Keys.Alt) == 0)
            {
                WinFormsUtil.Alert("Nothing is currently running.");
                return;
            }

            var cmd = BotControlCommand.Stop;

            if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0)
            {
                if (env.IsRunning)
                {
                    WinFormsUtil.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                    cmd = BotControlCommand.Idle;
                    SetButtonActiveState(btnStop, true);
                }
                else
                {
                    WinFormsUtil.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                    cmd = BotControlCommand.Resume;
                    SetButtonActiveState(btnStop, false);
                }
            }
            else
            {
                env.StopAll();
                WebApiExtensions.NotifyBotsStopped();
                SetButtonActiveState(btnStart, false);
                SetButtonActiveState(btnStop, false);
                SetButtonActiveState(btnReboot, false);
            }
            SendAll(cmd);
        }

        private void B_New_Click(object sender, EventArgs e)
        {
            var cfg = CreateNewBotConfig();
            if (!AddBot(cfg))
            {
                WinFormsUtil.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
                return;
            }
            System.Media.SystemSounds.Asterisk.Play();
        }

        private bool AddBot(PokeBotState cfg)
        {
            if (!cfg.IsValid())
                return false;

            if (Bots.Any(z => z.Connection.Equals(cfg.Connection)))
                return false;

            PokeRoutineExecutorBase newBot;
            try
            {
                LogUtil.LogError("Bot", $"Current Mode ({Config.Mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
                newBot = RunningEnvironment.CreateBotFromConfig(cfg);
            }
            catch
            {
                return false;
            }

            try
            {
                RunningEnvironment.Add(newBot);
            }
            catch (ArgumentException ex)
            {
                WinFormsUtil.Error(ex.Message);
                return false;
            }

            AddBotControl(cfg);
            Bots.Add(cfg);
            return true;
        }

        private void AddBotControl(PokeBotState cfg)
        {
            int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
            int availableWidth = FLP_Bots.ClientSize.Width;

            if (FLP_Bots.VerticalScroll.Visible)
            {
                availableWidth -= scrollBarWidth;
            }

            int botWidth = Math.Max(400, availableWidth - 10);

            var row = new BotController { Width = botWidth };
            row.Initialize(RunningEnvironment, cfg);
            FLP_Bots.Controls.Add(row);
            FLP_Bots.SetFlowBreak(row, true);

            row.Click += (s, e) =>
            {
                var details = cfg.Connection;
                TB_IP.Text = details.IP;
                NUD_Port.Value = details.Port;
                CB_Protocol.SelectedIndex = (int)details.Protocol;
                CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
            };

            EventHandler removeHandler = null!;
            removeHandler = (s, e) =>
            {
                row.Remove -= removeHandler; // Unsubscribe to prevent memory leak
                Bots.Remove(row.State);
                RunningEnvironment.Remove(row.State, !RunningEnvironment.Config.SkipConsoleBotCreation);
                FLP_Bots.Controls.Remove(row);
                row.Dispose(); // Ensure proper disposal
            };
            row.Remove += removeHandler;
        }

        private PokeBotState CreateNewBotConfig()
        {
            var ip = TB_IP.Text;
            var port = (int)NUD_Port.Value;
            var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port);
            cfg.Protocol = (SwitchProtocol)WinFormsUtil.GetIndex(CB_Protocol);

            var pk = new PokeBotState { Connection = cfg };
            var type = (PokeRoutineType)WinFormsUtil.GetIndex(CB_Routine);
            pk.Initialize(type);
            return pk;
        }

        private void FLP_Bots_Resize(object sender, EventArgs e)
        {
            int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
            int availableWidth = FLP_Bots.ClientSize.Width;

            if (FLP_Bots.VerticalScroll.Visible)
            {
                availableWidth -= scrollBarWidth;
            }

            int botWidth = Math.Max(400, availableWidth - 10);

            foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            {
                c.Width = botWidth;
            }
        }

        private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
        {
            TB_IP.Visible = CB_Protocol.SelectedIndex == 0;
        }

        private void ComboBox1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_isFormLoading) return;
            if (comboBox1.SelectedValue is int selectedValue)
            {
                ProgramMode newMode = (ProgramMode)selectedValue;
                Config.Mode = newMode;
                Config.Hub.Distribution.CurrentMode = newMode;
                SaveCurrentConfig();
                UpdateRunnerAndUI();
                UpdateBackgroundImage(newMode);

                var selectedCategory = (configCategoryList.SelectedItem as ConfigurationCategoryItem)?.Name;
                InitializeConfigurationCategories(selectedCategory);
            }
        }

        private void ConfigureGameSelector()
        {
            // Enhanced styling for the game selector
            comboBox1.DrawMode = DrawMode.OwnerDrawFixed;
            comboBox1.ItemHeight = 24;
            comboBox1.DrawItem += (sender, e) =>
            {
                if (e.Index < 0) return;

                // Custom background colors
                var backgroundColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                    ? Color.FromArgb(45, 125, 200)
                    : Color.FromArgb(32, 38, 48);
                    
                using (var bgBrush = new SolidBrush(backgroundColor))
                {
                    e.Graphics.FillRectangle(bgBrush, e.Bounds);
                }

                // Get the item text properly
                string text = "";
                var item = comboBox1.Items[e.Index];
                
                if (item != null)
                {
                    // Handle anonymous type from DataSource
                    var textProp = item.GetType().GetProperty("Text");
                    if (textProp != null)
                    {
                        text = textProp.GetValue(item)?.ToString() ?? "";
                    }
                    else
                    {
                        text = item.ToString() ?? "";
                    }
                }

                // Draw text with proper colors
                var textColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                    ? Color.White
                    : Color.FromArgb(239, 239, 239);
                    
                using (var textBrush = new SolidBrush(textColor))
                {
                    var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                    var format = new StringFormat
                    {
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap
                    };
                    e.Graphics.DrawString(text, e.Font ?? comboBox1.Font, textBrush, textRect, format);
                }
                
                if ((e.State & DrawItemState.Focus) == DrawItemState.Focus)
                {
                    e.DrawFocusRectangle();
                }
            };
        }

        private void UpdateRunnerAndUI()
        {
            if (RunningEnvironment.RunOnce)
            {
                LogUtil.LogInfo("Form", "Shutting down the previous integration runner before switching game mode.");
                RunningEnvironment.Shutdown();
            }

            RunningEnvironment = GetRunner(Config);
            Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "PokeBuilder" : Config.Hub.BotName)} {PokeBot.Version} ({Config.Mode})";
        }

        private void UpdateStatusIndicatorPulse()
        {
            // Animation removed - just update color
            UpdateStatusIndicatorColor();
        }

        private void UpdateStatusIndicatorColor()
        {
            if (statusIndicator == null) return;

            // Simple static color - no animation
            Color newColor = hasUpdate ? Color.FromArgb(255, 102, 192, 244) : Color.FromArgb(100, 100, 100);
            statusIndicator.BackColor = newColor;
        }

        private void UpdateBackgroundImage(ProgramMode mode)
        {
            try
            {
                _currentModeImage = mode switch
                {
                    ProgramMode.SV => Resources.sv_mode_image,
                    ProgramMode.SWSH => Resources.swsh_mode_image,
                    ProgramMode.BDSP => Resources.bdsp_mode_image,
                    ProgramMode.LA => Resources.pla_mode_image,
                    ProgramMode.LGPE => Resources.lgpe_mode_image,
                    //Todo: Add Resources.plza_mode_image when asset is available
                    ProgramMode.LZA => null,
                    _ => null,
                };
                FLP_Bots.Invalidate();
            }
            catch
            {
                _currentModeImage = null;
            }
        }

        #region Tray Icon Methods

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowFromTray();
        }

        private void TrayMenuShow_Click(object sender, EventArgs e)
        {
            ShowFromTray();
        }

        private void TrayMenuExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ShowFromTray()
        {
            // Set flag to prevent re-minimizing
            _isRestoringFromTray = true;
            
            // Make visible in taskbar first
            ShowInTaskbar = true;
            trayIcon.Visible = false;
            
            // Show the form without suspending layout
            Show();
            
            // Force normal window state
            WindowState = FormWindowState.Normal;

            // Ensure window is properly restored and focused
            BringToFront();
            Activate();
            Focus();

            // Apply dark mode after the window is fully shown
            // Use BeginInvoke to ensure it happens after the UI thread processes the show event
            BeginInvoke((MethodInvoker)(() =>
            {
                DarkModeHelper.SetDarkMode(this.Handle);

                // Force a repaint of the non-client area
                SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

                // Force proper panel layout after tray restore
                EnsurePanelLayout();
            }));

            // Clear the flag after a delay
            Task.Run(async () =>
            {
                await Task.Delay(500);
                _isRestoringFromTray = false;
                _suspendLayout = false;
            });

            // Update bots asynchronously without blocking UI
            if (TC_Main.SelectedTab == Tab_Bots && FLP_Bots.Controls.Count > 0)
            {
                // Use BeginInvoke to update bots after UI has settled
                BeginInvoke((MethodInvoker)(() =>
                {
                    // Resume animations for all bots
                    foreach (var bot in FLP_Bots.Controls.OfType<BotController>())
                    {
                        bot.ResumeAnimations();
                    }
                    
                    // Schedule bot state updates asynchronously
                    Task.Run(async () =>
                    {
                        // Small delay to let UI fully restore
                        await Task.Delay(200);
                        
                        BeginInvoke((MethodInvoker)(() =>
                        {
                            // Only update visible bots in viewport
                            var scrollPos = FLP_Bots.VerticalScroll.Value;
                            var viewportHeight = FLP_Bots.ClientSize.Height;
                            
                            foreach (var bot in FLP_Bots.Controls.OfType<BotController>())
                            {
                                // Check if bot is in visible viewport
                                if (bot.Top >= scrollPos - bot.Height && 
                                    bot.Top <= scrollPos + viewportHeight)
                                {
                                    bot.ReadState();
                                }
                            }
                        }));
                    });
                }));
            }
        }

        private void MinimizeToTray()
        {
            _suspendLayout = true;

            // Pause animations on all bot controllers before hiding
            foreach (var bot in FLP_Bots.Controls.OfType<BotController>())
            {
                bot.PauseAnimations();
            }

            Hide();
            ShowInTaskbar = false;
            trayIcon.Visible = true;

            var runningBots = FLP_Bots.Controls.OfType<BotController>().Count(c => c.GetBot()?.IsRunning ?? false);
            var totalBots = FLP_Bots.Controls.OfType<BotController>().Count();

            string message = totalBots == 0
                ? "No bots configured"
                : $"{runningBots} of {totalBots} bots running";

            trayIcon.ShowBalloonTip(2000, "PokéBot Minimized", message, ToolTipIcon.Info);
        }

        private void Main_Resize(object sender, EventArgs e)
        {
            // Don't minimize to tray on minimize button - only on close (X) button
            // The minimize button should just minimize normally to taskbar

            // Handle window state changes to manage animations
            if (WindowState == FormWindowState.Minimized)
            {
                // Pause animations when minimized
                foreach (var bot in FLP_Bots.Controls.OfType<BotController>())
                {
                    bot.PauseAnimations();
                }
            }
            else if (WindowState == FormWindowState.Normal || WindowState == FormWindowState.Maximized)
            {
                // Resume animations when restored
                foreach (var bot in FLP_Bots.Controls.OfType<BotController>())
                {
                    bot.ResumeAnimations();
                }
            }
        }

        #endregion

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Reapply dark mode when form is shown (helps with tray restore)
            DarkModeHelper.SetDarkMode(this.Handle);

            // Ensure panels are properly positioned
            EnsurePanelLayout();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // Apply dark mode when window is activated
            if (_isRestoringFromTray)
            {
                DarkModeHelper.SetDarkMode(this.Handle);
                // Also ensure proper panel layout when restoring from tray
                EnsurePanelLayout();
            }
        }
        
        private void EnsurePanelLayout()
        {
            // Skip if controls aren't ready
            if (contentPanel == null || headerPanel == null)
                return;
                
            // Force proper layout recalculation
            contentPanel.SuspendLayout();
            
            // Fix z-order: headerPanel must be last (on top) for DockStyle.Top to work correctly
            // The order matters: panels docked with Fill should be added first, then Top-docked panels
            contentPanel.Controls.SetChildIndex(botsPanel, 0);
            contentPanel.Controls.SetChildIndex(hubPanel, 0);
            contentPanel.Controls.SetChildIndex(logsPanel, 0);
            contentPanel.Controls.SetChildIndex(headerPanel, contentPanel.Controls.Count - 1);
            
            // Reset docking to force recalculation
            headerPanel.Dock = DockStyle.None;
            botsPanel.Dock = DockStyle.None;
            hubPanel.Dock = DockStyle.None;
            logsPanel.Dock = DockStyle.None;
            
            // Force layout update
            contentPanel.PerformLayout();
            
            // Reapply docking in correct order
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 60;
            
            botsPanel.Dock = DockStyle.Fill;
            hubPanel.Dock = DockStyle.Fill;
            logsPanel.Dock = DockStyle.Fill;
            
            contentPanel.ResumeLayout(true);
            contentPanel.PerformLayout();
        }
        
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            
            // Update all controls for new DPI
            DpiHelper.UpdateDpiForControl(this);
            if (Config != null)
                ApplyConfigurationFontScale(Config.ConfigurationFontScalePercent);
            
            // Update specific UI elements
            if (statusIndicator != null)
            {
                var scaledSize = DpiHelper.Scale(20);
                statusIndicator.Size = new Size(scaledSize, scaledSize);
                statusIndicator.Location = DpiHelper.Scale(new Point(10, 6));
            }
            
            // Update bot controllers
            foreach (var controller in FLP_Bots.Controls.OfType<BotController>())
            {
                controller.PerformLayout();
            }
            
            // Force layout update
            PerformLayout();
        }

        #region Performance Optimization Methods

        private void InvalidateThrottled(Control control, Rectangle? rect = null)
        {
            if (_suspendLayout) return;
            
            var now = DateTime.Now;
            if ((now - _lastInvalidate).TotalMilliseconds < INVALIDATE_THROTTLE_MS)
            {
                _deferredInvalidate = true;
                return;
            }

            _lastInvalidate = now;
            if (rect.HasValue)
                control.Invalidate(rect.Value);
            else
                control.Invalidate();
                
            if (_deferredInvalidate)
            {
                _deferredInvalidate = false;
                // Schedule deferred invalidation
                BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                {
                    if (!_suspendLayout)
                        control.Invalidate();
                }));
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCPAINT = 0x0085;
            
            // Skip non-client area painting for performance
            if (m.Msg == WM_NCPAINT && WindowState != FormWindowState.Normal)
            {
                return;
            }
            
            base.WndProc(ref m);
        }

        #endregion

        #region Log Management

        private void CleanupOldLogFiles()
        {
            // Skip cleanup if disabled - rely on NLog's built-in MaxArchiveFiles setting
            if (!LogConfig.EnableLogFileCleanup)
                return;

            try
            {
                var workingDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
                var logDirectory = Path.Combine(workingDirectory, "logs");

                if (!Directory.Exists(logDirectory))
                    return;

                var logFiles = Directory.GetFiles(logDirectory, "SysBotLog.*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                // Use configured retention days
                var cutoffDate = DateTime.Now.AddDays(-LogConfig.LogFileRetentionDays);
                foreach (var file in logFiles.Where(f => f.LastWriteTime < cutoffDate))
                {
                    try
                    {
                        file.Delete();
                        LogUtil.LogInfo($"Deleted old log file: {file.Name}", "System");
                    }
                    catch
                    {
                        // File might be in use, ignore
                    }
                }

                // Also check current log file size
                var currentLogFile = Path.Combine(logDirectory, "SysBotLog.txt");
                if (File.Exists(currentLogFile))
                {
                    var fileInfo = new FileInfo(currentLogFile);
                    // If current log file exceeds configured max size, log a notice (NLog handles rotation)
                    if (fileInfo.Length > LogConfig.MaxLogFileSize)
                    {
                        LogUtil.LogInfo("Current log file exceeds max size, NLog will rotate on next write", "System");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Failed to cleanup old log files: {ex.Message}", "System");
            }
        }

        #endregion

        #region Reset Helper Methods

        private bool AreAllBotsStopped()
        {
            foreach (var controller in FLP_Bots.Controls.OfType<BotController>())
            {
                var state = controller.ReadBotState();
                if (state != "STOPPED" && state != "IDLE")
                    return false;
            }
            return true;
        }

        private async Task StartBotsStaggeredAsync()
        {
            var controllers = FLP_Bots.Controls.OfType<BotController>().ToList();

            if (controllers.Count == 0)
            {
                SendAll(BotControlCommand.Start);
                return;
            }

            // Start bots in groups with delays to prevent overwhelming the system
            const int batchSize = 3;
            const int delayBetweenBatches = 2000; // 2 seconds between batches

            for (int i = 0; i < controllers.Count; i += batchSize)
            {
                var batch = controllers.Skip(i).Take(batchSize);
                foreach (var controller in batch)
                {
                    controller.SendCommand(BotControlCommand.Start, false);
                }

                if (i + batchSize < controllers.Count)
                {
                    await Task.Delay(delayBetweenBatches).ConfigureAwait(false);
                }
            }

            LogUtil.LogText($"Started {controllers.Count} bots in batches");
        }

        #endregion
    }

    public sealed class SearchManager
    {
        private readonly RichTextBox _textBox;
        private readonly Label _statusLabel;
        private readonly List<SearchMatch> _matches = [];
        private int _currentIndex = -1;
        private string _lastSearchText = string.Empty;
        private bool _caseSensitive = false;
        private bool _useRegex = false;
        private bool _wholeWord = false;

        private readonly Color HighlightColor = Color.FromArgb(102, 192, 244);
        private readonly Color CurrentHighlightColor = Color.FromArgb(57, 255, 221);

        public SearchManager(RichTextBox textBox, Label statusLabel)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
            _statusLabel = statusLabel ?? throw new ArgumentNullException(nameof(statusLabel));
        }

        public void UpdateSearch(string searchText)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                ClearSearch();
                return;
            }

            if (searchText == _lastSearchText)
                return;

            _lastSearchText = searchText;
            PerformSearch(searchText);
        }

        public void FindNext()
        {
            if (_matches.Count == 0)
                return;

            _currentIndex = (_currentIndex + 1) % _matches.Count;
            HighlightCurrentMatch();
        }

        public void FindPrevious()
        {
            if (_matches.Count == 0)
                return;

            _currentIndex = _currentIndex == 0 ? _matches.Count - 1 : _currentIndex - 1;
            HighlightCurrentMatch();
        }

        public void ClearSearch()
        {
            ClearHighlights();
            _matches.Clear();
            _matches.TrimExcess(); // Free up memory
            _currentIndex = -1;
            _lastSearchText = string.Empty;
            _statusLabel.Text = string.Empty;
        }

        public void ToggleCaseSensitive()
        {
            _caseSensitive = !_caseSensitive;
            if (!string.IsNullOrEmpty(_lastSearchText))
                PerformSearch(_lastSearchText);
        }

        public void ToggleRegex()
        {
            _useRegex = !_useRegex;
            if (!string.IsNullOrEmpty(_lastSearchText))
                PerformSearch(_lastSearchText);
        }

        public void ToggleWholeWord()
        {
            _wholeWord = !_wholeWord;
            if (!string.IsNullOrEmpty(_lastSearchText))
                PerformSearch(_lastSearchText);
        }

        private void PerformSearch(string searchText)
        {
            ClearHighlights();
            _matches.Clear();
            _currentIndex = -1;

            if (string.IsNullOrEmpty(searchText))
            {
                _statusLabel.Text = string.Empty;
                return;
            }

            try
            {
                var text = _textBox.Text;
                var matches = _useRegex ? FindRegexMatches(text, searchText) : FindTextMatches(text, searchText);

                _matches.AddRange(matches);

                if (_matches.Count > 0)
                {
                    HighlightAllMatches();
                    _currentIndex = 0;
                    HighlightCurrentMatch();
                    _statusLabel.Text = $"1 of {_matches.Count}";
                }
                else
                {
                    _statusLabel.Text = "No matches found";
                }
            }
            catch (ArgumentException)
            {
                _statusLabel.Text = "Invalid regex pattern";
            }
        }

        private IEnumerable<SearchMatch> FindTextMatches(string text, string searchText)
        {
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var searchPattern = _wholeWord ? $@"\b{Regex.Escape(searchText)}\b" : searchText;

            if (_wholeWord)
            {
                var regex = new Regex(searchPattern, _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                return regex.Matches(text).Cast<Match>()
                    .Select(m => new SearchMatch(m.Index, m.Length));
            }

            var matches = new List<SearchMatch>();
            int index = 0;
            while ((index = text.IndexOf(searchText, index, comparison)) != -1)
            {
                matches.Add(new SearchMatch(index, searchText.Length));
                index += searchText.Length;
            }
            return matches;
        }

        private IEnumerable<SearchMatch> FindRegexMatches(string text, string pattern)
        {
            var options = _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(pattern, options);
            return regex.Matches(text).Cast<Match>()
                .Select(m => new SearchMatch(m.Index, m.Length));
        }

        private void HighlightAllMatches()
        {
            foreach (var match in _matches)
            {
                _textBox.Select(match.Start, match.Length);
                _textBox.SelectionBackColor = HighlightColor;
            }
        }

        private void HighlightCurrentMatch()
        {
            if (_currentIndex < 0 || _currentIndex >= _matches.Count)
                return;

            ClearCurrentHighlight();

            var currentMatch = _matches[_currentIndex];
            _textBox.Select(currentMatch.Start, currentMatch.Length);
            _textBox.SelectionBackColor = CurrentHighlightColor;
            _textBox.ScrollToCaret();

            _statusLabel.Text = $"{_currentIndex + 1} of {_matches.Count}";
        }

        private void ClearCurrentHighlight()
        {
            if (_currentIndex >= 0 && _currentIndex < _matches.Count)
            {
                var match = _matches[_currentIndex];
                _textBox.Select(match.Start, match.Length);
                _textBox.SelectionBackColor = HighlightColor;
            }
        }

        private void ClearHighlights()
        {
            _textBox.SelectAll();
            _textBox.SelectionBackColor = _textBox.BackColor;
            _textBox.DeselectAll();
        }
    }

    public readonly record struct SearchMatch(int Start, int Length);
}
