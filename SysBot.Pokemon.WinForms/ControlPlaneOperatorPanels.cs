using System;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

internal sealed class McpControlPlanePanel : UserControl
{
    private readonly Label _orchestrationValue;
    private readonly Label _transportValue;
    private readonly Label _securityValue;
    private readonly Label _endpointValue;
    private readonly Label _errorValue;

    public McpControlPlanePanel()
    {
        AccessibleName = "MCP control plane";
        AutoScroll = true;
        BackColor = Color.Transparent;
        Padding = new Padding(18);

        var content = new FlowLayoutPanel
        {
            AutoScroll = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        Controls.Add(content);

        content.Controls.Add(CreateHeading(
            "MCP CONTROL PLANE",
            "Local status for the durable LLM trade endpoint. The transport remains loopback-only."));

        var statusCard = CreateCard();
        _orchestrationValue = AddStatusRow(
            statusCard,
            "Durable orchestration",
            "Starting...");
        _transportValue = AddStatusRow(
            statusCard,
            "MCP transport",
            "Starting...");
        _securityValue = AddStatusRow(
            statusCard,
            "Bearer token",
            "Checking...");
        content.Controls.Add(statusCard);

        var connectionCard = CreateCard();
        connectionCard.Controls.Add(CreateSectionLabel("CLIENT CONNECTION"));
        _endpointValue = new Label
        {
            AutoEllipsis = true,
            Font = new Font("Consolas", 9F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Height = 28,
            Margin = new Padding(12, 2, 12, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 730,
        };
        connectionCard.Controls.Add(_endpointValue);

        var connectionActions = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(8, 2, 8, 8),
            WrapContents = false,
        };
        var copyButton = CreateButton("COPY ENDPOINT");
        copyButton.Click += (_, _) => CopyEndpoint();
        var refreshButton = CreateButton("REFRESH");
        refreshButton.Click += (_, _) => RefreshStatus();
        connectionActions.Controls.Add(copyButton);
        connectionActions.Controls.Add(refreshButton);
        connectionCard.Controls.Add(connectionActions);
        content.Controls.Add(connectionCard);

        var noticeCard = CreateCard();
        noticeCard.Controls.Add(CreateSectionLabel("STARTUP STATUS"));
        _errorValue = new Label
        {
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = ConfigurationTheme.TextMuted,
            Height = 42,
            Margin = new Padding(12, 2, 12, 10),
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 730,
        };
        noticeCard.Controls.Add(_errorValue);
        content.Controls.Add(noticeCard);

        Resize += (_, _) => ResizeCards(content);
        VisibleChanged += (_, _) =>
        {
            if (Visible)
                RefreshStatus();
        };
        McpControlPlaneService.StatusChanged += McpControlPlaneService_StatusChanged;
        RefreshStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            McpControlPlaneService.StatusChanged -= McpControlPlaneService_StatusChanged;
        base.Dispose(disposing);
    }

    private void McpControlPlaneService_StatusChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)RefreshStatus);
            }
            catch (InvalidOperationException)
            {
                // The panel handle was destroyed while the service was stopping.
            }
            return;
        }
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (IsDisposed)
            return;

        var status = McpControlPlaneService.GetStatus();
        SetStatus(
            _orchestrationValue,
            status.OrchestrationActive ? "READY" : "NOT STARTED",
            status.OrchestrationActive);
        SetStatus(
            _transportValue,
            status.TransportRunning ? $"LISTENING ON {status.Port}" : "DISABLED",
            status.TransportRunning);
        SetStatus(
            _securityValue,
            status.TokenConfigured ? "CONFIGURED" : "MISSING OR INVALID",
            status.TokenConfigured);
        _endpointValue.Text = status.Endpoint;
        _errorValue.Text = status.LastError is { Length: > 0 }
            ? status.LastError
            : status.TransportRunning
                ? "The MCP endpoint is healthy enough to accept authenticated client requests."
                : $"Set {SysBot.Pokemon.Mcp.PokeBotMcpHost.TokenEnvironmentVariable} to a unique 32+ character token, then restart PokeBot.";
        _errorValue.ForeColor = status.LastError is null
            ? ConfigurationTheme.TextMuted
            : ConfigurationTheme.Accent;
    }

    private void CopyEndpoint()
    {
        if (string.IsNullOrWhiteSpace(_endpointValue.Text))
            return;
        try
        {
            Clipboard.SetText(_endpointValue.Text);
            _errorValue.Text = "MCP endpoint copied to the clipboard.";
            _errorValue.ForeColor = ConfigurationTheme.TextMuted;
        }
        catch (Exception ex)
        {
            _errorValue.Text = $"Could not copy the endpoint: {ex.Message}";
            _errorValue.ForeColor = ConfigurationTheme.Accent;
        }
    }

    private static void SetStatus(Label label, string text, bool healthy)
    {
        label.Text = text;
        label.ForeColor = healthy
            ? Color.FromArgb(90, 186, 71)
            : ConfigurationTheme.Accent;
    }

    private static Control CreateHeading(string title, string description)
    {
        var panel = new Panel
        {
            BackColor = Color.Transparent,
            Height = 76,
            Margin = new Padding(0, 0, 0, 10),
            Width = 760,
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Location = new Point(0, 2),
            Text = title,
        });
        panel.Controls.Add(new Label
        {
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = ConfigurationTheme.TextMuted,
            Location = new Point(0, 34),
            Size = new Size(750, 34),
            Text = description,
        });
        return panel;
    }

    private static FlowLayoutPanel CreateCard() =>
        new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = ConfigurationTheme.SurfaceRaised,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(6),
            Width = 760,
            WrapContents = false,
        };

    private static Label CreateSectionLabel(string text) =>
        new()
        {
            Font = new Font("Segoe UI Semibold", 8.5F),
            ForeColor = ConfigurationTheme.TextSecondary,
            Height = 24,
            Margin = new Padding(12, 8, 12, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 730,
        };

    private static Label AddStatusRow(
        FlowLayoutPanel parent,
        string name,
        string initialValue)
    {
        var row = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Height = 38,
            Margin = new Padding(6, 2, 6, 2),
            RowCount = 1,
            Width = 730,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        row.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            ForeColor = ConfigurationTheme.TextSecondary,
            Text = name,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        var value = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Text = initialValue,
            TextAlign = ContentAlignment.MiddleRight,
        };
        row.Controls.Add(value, 1, 0);
        parent.Controls.Add(row);
        return value;
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            AutoSize = true,
            BackColor = ConfigurationTheme.Surface,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Height = 31,
            Margin = new Padding(4),
            Padding = new Padding(12, 0, 12, 0),
            Text = text,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = ConfigurationTheme.BorderStrong;
        button.FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceHover;
        return button;
    }

    private static void ResizeCards(FlowLayoutPanel content)
    {
        var width = Math.Max(420, content.Parent?.ClientSize.Width - 36 ?? 760);
        content.Width = width;
        foreach (Control control in content.Controls)
            control.Width = width;
    }
}

internal sealed class ControlPlaneTestingPanel : UserControl
{
    private static readonly HttpClient HealthClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    private readonly Button _healthButton;
    private readonly Button _runtimeButton;
    private readonly RichTextBox _results;

    public ControlPlaneTestingPanel()
    {
        AccessibleName = "MCP and runtime testing";
        BackColor = Color.Transparent;
        Padding = new Padding(18);

        var heading = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Location = new Point(18, 18),
            Text = "CONTROL-PLANE TESTING",
        };
        var description = new Label
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = ConfigurationTheme.TextMuted,
            Location = new Point(18, 50),
            Size = new Size(790, 38),
            Text = "Read-only checks for the local MCP listener and the current SysBot runtime. These tests do not enqueue or trade Pokémon.",
        };

        var actions = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = ConfigurationTheme.SurfaceRaised,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(18, 98),
            Padding = new Padding(10),
            Size = new Size(800, 58),
            WrapContents = false,
        };
        _healthButton = CreateButton("TEST MCP HEALTH");
        _healthButton.Click += async (_, _) => await RunHealthCheckAsync();
        _runtimeButton = CreateButton("INSPECT BOT RUNTIME");
        _runtimeButton.Click += (_, _) => InspectRuntime();
        var clearButton = CreateButton("CLEAR");
        actions.Controls.Add(_healthButton);
        actions.Controls.Add(_runtimeButton);
        actions.Controls.Add(clearButton);

        _results = new RichTextBox
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = ConfigurationTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Location = new Point(18, 170),
            ReadOnly = true,
            Size = new Size(800, 340),
            Text = "Select a read-only test above.\n",
        };
        clearButton.Click += (_, _) => _results.Clear();

        Controls.Add(heading);
        Controls.Add(description);
        Controls.Add(actions);
        Controls.Add(_results);
    }

    private async Task RunHealthCheckAsync()
    {
        _healthButton.Enabled = false;
        try
        {
            var status = McpControlPlaneService.GetStatus();
            if (!status.TransportRunning)
            {
                AppendResult(
                    "MCP HEALTH",
                    status.LastError is { Length: > 0 }
                        ? $"FAILED - {status.LastError}"
                        : "NOT RUNNING - configure a valid token and restart PokeBot.");
                return;
            }

            using var response = await HealthClient
                .GetAsync(status.HealthEndpoint)
                .ConfigureAwait(true);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            AppendResult(
                "MCP HEALTH",
                response.IsSuccessStatusCode
                    ? $"PASS - HTTP {(int)response.StatusCode}; {body}"
                    : $"FAILED - HTTP {(int)response.StatusCode}; {body}");
        }
        catch (Exception ex)
        {
            AppendResult("MCP HEALTH", $"FAILED - {ex.Message}");
        }
        finally
        {
            if (!IsDisposed)
                _healthButton.Enabled = true;
        }
    }

    private void InspectRuntime()
    {
        try
        {
            var api = McpControlPlaneService.CurrentApi;
            if (api is null)
            {
                AppendResult(
                    "BOT RUNTIME",
                    "NOT READY - durable orchestration has not started.");
                return;
            }

            var response = api.ListBotInstances(
                "winforms:testing",
                includeOffline: true);
            if (!response.Success || response.Data is null)
            {
                AppendResult(
                    "BOT RUNTIME",
                    $"FAILED - {response.Error?.Code}: {response.Error?.Message}");
                return;
            }

            var runtime = response.Data;
            var connected = runtime.Bots.Count(bot => bot.IsConnected);
            AppendResult(
                "BOT RUNTIME",
                $"PASS - mode={runtime.GameMode}; generation={runtime.Generation}; " +
                $"available={runtime.IsAvailable}; running={runtime.IsRunning}; " +
                $"queue_open={runtime.IsQueueOpen}; queue_count={runtime.QueueCount}; " +
                $"connected_bots={connected}/{runtime.Bots.Count}");
        }
        catch (Exception ex)
        {
            AppendResult("BOT RUNTIME", $"FAILED - {ex.Message}");
        }
    }

    private void AppendResult(string test, string result)
    {
        _results.AppendText(
            $"[{DateTime.Now:T}] {test}{Environment.NewLine}{result}{Environment.NewLine}{Environment.NewLine}");
        _results.SelectionStart = _results.TextLength;
        _results.ScrollToCaret();
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            BackColor = ConfigurationTheme.Surface,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 8.5F),
            ForeColor = ConfigurationTheme.TextPrimary,
            Height = 34,
            Margin = new Padding(4),
            Padding = new Padding(12, 0, 12, 0),
            Text = text,
            UseVisualStyleBackColor = false,
            Width = text == "CLEAR" ? 90 : 180,
        };
        button.FlatAppearance.BorderColor = ConfigurationTheme.BorderStrong;
        button.FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceHover;
        return button;
    }
}
