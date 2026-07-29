using SysBot.Pokemon.WinForms.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using SysBot.Base;

#pragma warning disable CS8618
#pragma warning disable CS8625
#pragma warning disable CS8669

namespace SysBot.Pokemon.WinForms
{
    partial class Main
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            if (disposing && trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));

            trayIcon = new NotifyIcon(this.components);
            trayContextMenu = new ContextMenuStrip(this.components);
            trayMenuShow = new ToolStripMenuItem();
            trayMenuExit = new ToolStripMenuItem();

            mainLayoutPanel = new TableLayoutPanel();
            sidebarPanel = new Panel();
            contentPanel = new Panel();
            headerPanel = new Panel();

            logoPanel = new Panel();
            navButtonsPanel = new FlowLayoutPanel();
            btnNavBots = new Button();
            btnNavHub = new Button();
            btnNavMcp = new Button();
            btnNavTesting = new Button();
            btnNavLogs = new Button();
            sidebarBottomPanel = new Panel();
            btnUpdate = new Button();
            statusIndicator = new Panel();

            titleLabel = new Label();
            controlButtonsPanel = new FlowLayoutPanel();
            btnStart = new Button();
            btnStop = new Button();
            btnReboot = new Button();

            botsPanel = new Panel();
            hubPanel = new Panel();
            mcpPanel = new McpControlPlanePanel();
            testingPanel = new ControlPlaneTestingPanel();
            logsPanel = new Panel();

            botHeaderPanel = new Panel();
            addBotPanel = new Panel();
            TB_IP = new TextBox();
            NUD_Port = new NumericUpDown();
            CB_Protocol = new ComboBox();
            CB_Routine = new ComboBox();
            B_New = new Button();
            FLP_Bots = new FlowLayoutPanel();

            PG_Hub = new PropertyGrid();
            configToolbarPanel = new Panel();
            configThemeMark = new Panel();
            configToolbarTitle = new Label();
            configToolbarSubtitle = new Label();
            configTextSizeControlsPanel = new FlowLayoutPanel();
            configTextSizeCaption = new Label();
            btnConfigFontDecrease = new Button();
            lblConfigFontScale = new Label();
            btnConfigFontIncrease = new Button();
            btnConfigFontReset = new Button();
            configContentSplit = new SplitContainer();
            configSettingsTree = new ConfigurationSettingsTree();
            pgContainer = new Panel();
            configCategoryTitle = new Label();
            configCategorySubtitle = new Label();
            configCategoryList = new ListBox();
            configSectionHeaderPanel = new Panel();
            configSectionTitle = new Label();
            configSectionDescription = new Label();

            RTB_Logs = new RichTextBox();
            logsHeaderPanel = new Panel();
            searchPanel = new Panel();
            logSearchBox = new TextBox();
            searchOptionsPanel = new FlowLayoutPanel();
            btnCaseSensitive = new CheckBox();
            btnRegex = new CheckBox();
            btnWholeWord = new CheckBox();
            btnClearLogs = new Button();
            searchStatusLabel = new Label();

            comboBox1 = new ComboBox();

            mainLayoutPanel.SuspendLayout();
            sidebarPanel.SuspendLayout();
            navButtonsPanel.SuspendLayout();
            sidebarBottomPanel.SuspendLayout();
            headerPanel.SuspendLayout();
            controlButtonsPanel.SuspendLayout();
            contentPanel.SuspendLayout();
            botsPanel.SuspendLayout();
            botHeaderPanel.SuspendLayout();
            addBotPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)NUD_Port).BeginInit();
            hubPanel.SuspendLayout();
            configToolbarPanel.SuspendLayout();
            configTextSizeControlsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)configContentSplit).BeginInit();
            configContentSplit.Panel1.SuspendLayout();
            configContentSplit.Panel2.SuspendLayout();
            configContentSplit.SuspendLayout();
            configSectionHeaderPanel.SuspendLayout();
            logsPanel.SuspendLayout();
            logsHeaderPanel.SuspendLayout();
            searchPanel.SuspendLayout();
            searchOptionsPanel.SuspendLayout();
            SuspendLayout();

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.DoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            // Main Form
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1280, 720);
            MinimumSize = new Size(1100, 650);
            BackColor = Color.FromArgb(27, 40, 56);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.Sizable;
            Icon = Resources.icon;
            Name = "Main";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PokéBot Control Center";
            FormClosing += Main_FormClosing;
            DoubleBuffered = true;
            Resize += Main_Resize;

            // Main Layout Panel
            mainLayoutPanel.ColumnCount = 2;
            mainLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240F));
            mainLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayoutPanel.Controls.Add(sidebarPanel, 0, 0);
            mainLayoutPanel.Controls.Add(contentPanel, 1, 0);
            mainLayoutPanel.Dock = DockStyle.Fill;
            mainLayoutPanel.Location = new Point(0, 0);
            mainLayoutPanel.Margin = new Padding(0);
            mainLayoutPanel.Name = "mainLayoutPanel";
            mainLayoutPanel.RowCount = 1;
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayoutPanel.TabIndex = 0;
            mainLayoutPanel.BackColor = Color.Transparent;
            EnableDoubleBuffering(mainLayoutPanel);

            // Sidebar Panel - Cuztom style
            sidebarPanel.BackColor = Color.FromArgb(23, 29, 37);
            sidebarPanel.Controls.Add(navButtonsPanel);
            sidebarPanel.Controls.Add(sidebarBottomPanel);
            sidebarPanel.Controls.Add(logoPanel);
            sidebarPanel.Dock = DockStyle.Fill;
            sidebarPanel.Location = new Point(0, 0);
            sidebarPanel.Margin = new Padding(0);
            sidebarPanel.Name = "sidebarPanel";
            sidebarPanel.Size = new Size(240, 600);
            sidebarPanel.TabIndex = 0;
            EnableDoubleBuffering(sidebarPanel);

            // Logo Panel - Cuztom gradient
            logoPanel.BackColor = Color.FromArgb(23, 29, 37);
            logoPanel.Dock = DockStyle.Top;
            logoPanel.Height = 60;
            logoPanel.Location = new Point(0, 0);
            logoPanel.Name = "logoPanel";
            logoPanel.Size = new Size(240, 60);
            logoPanel.TabIndex = 2;
            logoPanel.Paint += PokeBallLogoPanel_Paint;
            EnableDoubleBuffering(logoPanel);

            // Navigation Buttons Panel
            navButtonsPanel.AutoSize = false;
            navButtonsPanel.Controls.Add(btnNavBots);
            navButtonsPanel.Controls.Add(btnNavHub);
            navButtonsPanel.Controls.Add(btnNavMcp);
            navButtonsPanel.Controls.Add(btnNavTesting);
            navButtonsPanel.Controls.Add(btnNavLogs);
            navButtonsPanel.Dock = DockStyle.Fill;
            navButtonsPanel.FlowDirection = FlowDirection.TopDown;
            navButtonsPanel.Location = new Point(0, 60);
            navButtonsPanel.Margin = new Padding(0);
            navButtonsPanel.Name = "navButtonsPanel";
            navButtonsPanel.Padding = new Padding(0, 10, 0, 0);
            navButtonsPanel.Size = new Size(240, 460);
            navButtonsPanel.TabIndex = 1;
            navButtonsPanel.BackColor = Color.Transparent;
            EnableDoubleBuffering(navButtonsPanel);

            // One restrained Poké Ball-red accent keeps navigation consistent.
            var pokeBallAccent = Color.FromArgb(230, 77, 77);
            ConfigureNavButton(
                btnNavBots,
                "BOTS",
                NavigationBotsIndex,
                "Manage bot connections",
                pokeBallAccent);
            ConfigureNavButton(
                btnNavHub,
                "CONFIGURATION",
                NavigationConfigurationIndex,
                "System settings",
                pokeBallAccent);
            ConfigureNavButton(
                btnNavMcp,
                "MCP",
                NavigationMcpIndex,
                "View MCP control-plane status",
                pokeBallAccent);
            ConfigureNavButton(
                btnNavTesting,
                "TESTING",
                NavigationTestingIndex,
                "Run read-only MCP and runtime tests",
                pokeBallAccent);
            ConfigureNavButton(
                btnNavLogs,
                "SYSTEM LOGS",
                NavigationLogsIndex,
                "View activity logs",
                pokeBallAccent);

            var separator = new Panel();
            separator.BackColor = Color.FromArgb(32, 38, 48);
            separator.Size = new Size(200, 1);
            separator.Margin = new Padding(20, 20, 20, 20);
            navButtonsPanel.Controls.Add(separator);

            var btnTray = new Button();
            ConfigureNavButton(
                btnTray,
                "SEND TO TRAY",
                NavigationPanelCount,
                "Minimize to system tray",
                Color.FromArgb(102, 192, 244));
            btnTray.Click += BtnTray_Click;
            navButtonsPanel.Controls.Add(btnTray);

            // Sidebar Bottom Panel
            var spacerPanel = new Panel();
            spacerPanel.Dock = DockStyle.Top;
            spacerPanel.Height = 8;  // Gap between combo and button
            sidebarBottomPanel.Controls.Add(btnUpdate);
            sidebarBottomPanel.Controls.Add(spacerPanel);
            sidebarBottomPanel.Controls.Add(comboBox1);
            sidebarBottomPanel.Dock = DockStyle.Bottom;
            sidebarBottomPanel.Height = 90;  // Increased height for better spacing
            sidebarBottomPanel.Location = new Point(0, 510);
            sidebarBottomPanel.Name = "sidebarBottomPanel";
            sidebarBottomPanel.Padding = new Padding(10, 5, 10, 10);
            sidebarBottomPanel.TabIndex = 0;
            sidebarBottomPanel.BackColor = Color.FromArgb(19, 23, 30);
            sidebarBottomPanel.MaximumSize = new Size(240, 90);
            EnableDoubleBuffering(sidebarBottomPanel);

            // Mode Selector ComboBox - Enhanced style
            comboBox1.Dock = DockStyle.Top;
            comboBox1.BackColor = Color.FromArgb(32, 38, 48);
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.FlatStyle = FlatStyle.Flat;
            comboBox1.Font = new Font("Segoe UI", 9F);
            comboBox1.ForeColor = Color.FromArgb(239, 239, 239);
            comboBox1.Name = "comboBox1";
            comboBox1.TabIndex = 10;
            comboBox1.Cursor = Cursors.Hand;
            comboBox1.SelectedIndexChanged += ComboBox1_SelectedIndexChanged;

            // Update Button - Modern style with proper spacing
            var dpiScale = this.DeviceDpi / 96f;
            btnUpdate.Dock = DockStyle.Bottom;
            btnUpdate.BackColor = Color.FromArgb(45, 125, 200);
            btnUpdate.FlatAppearance.BorderSize = 0;
            btnUpdate.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 145, 220);
            btnUpdate.FlatAppearance.MouseDownBackColor = Color.FromArgb(35, 105, 180);
            btnUpdate.FlatStyle = FlatStyle.Flat;
            btnUpdate.Font = ScaleFont(new Font("Segoe UI", 9F, FontStyle.Bold));
            btnUpdate.ForeColor = Color.White;
            btnUpdate.Height = 32;
            btnUpdate.Margin = new Padding(0, 8, 0, 0);  // Add gap between combo and button
            btnUpdate.Name = "btnUpdate";
            btnUpdate.TabIndex = 1;
            btnUpdate.Text = "";  // Text will be set by ConfigureUpdateButton
            btnUpdate.UseVisualStyleBackColor = false;
            btnUpdate.Click += Updater_Click;
            btnUpdate.Cursor = Cursors.Hand;
            btnUpdate.Tag = new ButtonAnimationState();
            ConfigureHoverAnimation(btnUpdate);
            ConfigureUpdateButton();

            // Content Panel
            contentPanel.BackColor = Color.FromArgb(30, 35, 44);
            contentPanel.Controls.Add(botsPanel);
            contentPanel.Controls.Add(hubPanel);
            contentPanel.Controls.Add(mcpPanel);
            contentPanel.Controls.Add(testingPanel);
            contentPanel.Controls.Add(logsPanel);
            contentPanel.Controls.Add(headerPanel);
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Location = new Point(240, 0);
            contentPanel.Margin = new Padding(0);
            contentPanel.Name = "contentPanel";
            contentPanel.Size = new Size(860, 600);
            contentPanel.TabIndex = 1;
            EnableDoubleBuffering(contentPanel);

            // Header Panel - Cuztom style
            headerPanel.BackColor = Color.FromArgb(30, 35, 44);
            headerPanel.Controls.Add(controlButtonsPanel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 60;
            headerPanel.Location = new Point(0, 0);
            headerPanel.Name = "headerPanel";
            headerPanel.Size = new Size(860, 60);
            headerPanel.TabIndex = 3;
            headerPanel.Paint += HeaderPanel_Paint;
            headerPanel.Resize += HeaderPanel_Resize;
            EnableDoubleBuffering(headerPanel);

            // Title Label
            titleLabel.AutoSize = true;
            titleLabel.Font = ScaleFont(new Font("Segoe UI", 16F, FontStyle.Bold));
            titleLabel.ForeColor = Color.FromArgb(239, 239, 239);
            titleLabel.Location = new Point(20, 18);
            titleLabel.Name = "titleLabel";
            titleLabel.TabIndex = 0;
            titleLabel.Text = "Bot Management";
            titleLabel.MaximumSize = new Size(350, 35);
            titleLabel.AutoEllipsis = true;

            // Control Buttons Panel
            controlButtonsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            controlButtonsPanel.AutoSize = true;
            controlButtonsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            controlButtonsPanel.Controls.Add(btnStart);
            controlButtonsPanel.Controls.Add(btnStop);
            controlButtonsPanel.Controls.Add(btnReboot);
            controlButtonsPanel.FlowDirection = FlowDirection.LeftToRight;
            controlButtonsPanel.Location = new Point(contentPanel.Width - 300, 18);
            controlButtonsPanel.Name = "controlButtonsPanel";
            controlButtonsPanel.TabIndex = 1;
            controlButtonsPanel.BackColor = Color.Transparent;
            controlButtonsPanel.WrapContents = false;

            // Modern control buttons with clean design
            ConfigureEnhancedControlButton(btnStart, "START", Color.FromArgb(90, 186, 71), "▶");
            ConfigureEnhancedControlButton(btnStop, "STOP", Color.FromArgb(236, 98, 95), "■");
            ConfigureEnhancedControlButton(btnReboot, "RESTART", Color.FromArgb(102, 192, 244), "↻");

            btnStart.Click += B_Start_Click;
            btnStop.Click += B_Stop_Click;
            btnReboot.Click += B_RebootStop_Click;

            // Bots Panel
            botsPanel.BackColor = Color.Transparent;
            botsPanel.Controls.Add(FLP_Bots);
            botsPanel.Controls.Add(botHeaderPanel);
            botsPanel.Dock = DockStyle.Fill;
            botsPanel.Location = new Point(0, 60);
            botsPanel.Name = "botsPanel";
            botsPanel.Padding = new Padding(10);
            botsPanel.Size = new Size(860, 540);
            botsPanel.TabIndex = 0;
            botsPanel.Visible = true;
            EnableDoubleBuffering(botsPanel);

            // Bot Header Panel - Cuztom style
            botHeaderPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            botHeaderPanel.BackColor = Color.FromArgb(22, 32, 45);
            botHeaderPanel.Controls.Add(addBotPanel);
            botHeaderPanel.Height = 60;
            botHeaderPanel.Location = new Point(10, 10);
            botHeaderPanel.Name = "botHeaderPanel";
            botHeaderPanel.Size = new Size(840, 60);
            botHeaderPanel.TabIndex = 1;
            CreateRoundedPanel(botHeaderPanel);
            EnableDoubleBuffering(botHeaderPanel);

            // Add Bot Panel
            addBotPanel.Controls.Add(B_New);
            addBotPanel.Controls.Add(CB_Routine);
            addBotPanel.Controls.Add(CB_Protocol);
            addBotPanel.Controls.Add(NUD_Port);
            addBotPanel.Controls.Add(TB_IP);
            addBotPanel.Dock = DockStyle.Fill;
            addBotPanel.Location = new Point(0, 0);
            addBotPanel.Name = "addBotPanel";
            addBotPanel.Size = new Size(840, 60);
            addBotPanel.TabIndex = 0;
            addBotPanel.BackColor = Color.Transparent;
            addBotPanel.Layout += AddBotPanel_Layout;

            // Cuztom-style input controls
            TB_IP.BackColor = Color.FromArgb(32, 38, 48);
            TB_IP.BorderStyle = BorderStyle.FixedSingle;
            TB_IP.Font = ScaleFont(new Font("Segoe UI", 9F));
            TB_IP.ForeColor = Color.FromArgb(239, 239, 239);
            TB_IP.Location = new Point(15, 18);
            TB_IP.Name = "TB_IP";
            TB_IP.PlaceholderText = "IP Address";
            TB_IP.Size = new Size(110, 23);
            TB_IP.TabIndex = 0;
            TB_IP.Text = "192.168.0.1";

            ConfigureNumericUpDown(NUD_Port, 135, 18, 60);
            NUD_Port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            NUD_Port.Value = new decimal(new int[] { 6000, 0, 0, 0 });

            CB_Protocol.SuspendLayout();
            ConfigureComboBox(CB_Protocol, 205, 18, 70);
            CB_Protocol.SelectedIndexChanged += CB_Protocol_SelectedIndexChanged;
            CB_Protocol.ResumeLayout();

            ConfigureComboBox(CB_Routine, 285, 18, 130);

            CB_Routine.SizeChanged += CB_Routine_SizeChanged;
            CB_Routine.LocationChanged += CB_Routine_LocationChanged;

            // Add Bot Button - Cuztom accent - Responsive positioning
            B_New.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            B_New.BackColor = Color.FromArgb(102, 192, 244);
            B_New.FlatAppearance.BorderSize = 0;
            B_New.FlatStyle = FlatStyle.Flat;
            B_New.Font = ScaleFont(new Font("Segoe UI", 8.5F, FontStyle.Bold));
            B_New.ForeColor = Color.FromArgb(22, 32, 45);
            B_New.Location = new Point(430, 16);
            B_New.Name = "B_New";
            B_New.Size = new Size(85, 28);
            B_New.TabIndex = 4;
            B_New.Text = "ADD BOT";
            B_New.UseVisualStyleBackColor = false;
            B_New.Click += B_New_Click;
            B_New.Cursor = Cursors.Hand;
            ConfigureGlowButton(B_New);
            CreateRoundedButton(B_New);

            // Bots Flow Layout Panel
            FLP_Bots.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            FLP_Bots.AutoScroll = true;
            FLP_Bots.BackColor = Color.Transparent;
            FLP_Bots.FlowDirection = FlowDirection.TopDown;
            FLP_Bots.Location = new Point(10, 75);
            FLP_Bots.Margin = new Padding(0, 5, 0, 0);
            FLP_Bots.Name = "FLP_Bots";
            FLP_Bots.Padding = new Padding(0);
            FLP_Bots.Size = new Size(840, 455);
            FLP_Bots.TabIndex = 0;
            FLP_Bots.WrapContents = false;
            FLP_Bots.Resize += FLP_Bots_Resize;
            FLP_Bots.Paint += FLP_Bots_Paint;
            FLP_Bots.Scroll += FLP_Bots_Scroll;
            FLP_Bots.ControlAdded += FLP_Bots_ControlAdded;
            FLP_Bots.ControlRemoved += FLP_Bots_ControlRemoved;
            EnableDoubleBuffering(FLP_Bots);

            // Hub Panel
            hubPanel.BackColor = Color.Transparent;
            hubPanel.Controls.Add(configContentSplit);
            hubPanel.Controls.Add(configToolbarPanel);
            hubPanel.Dock = DockStyle.Fill;
            hubPanel.Location = new Point(0, 60);
            hubPanel.Name = "hubPanel";
            hubPanel.Padding = new Padding(10);
            hubPanel.Size = new Size(860, 540);
            hubPanel.TabIndex = 1;
            hubPanel.Visible = false;
            EnableDoubleBuffering(hubPanel);

            // Configuration toolbar - dark Poké Ball theme
            configToolbarPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            configToolbarPanel.BackColor = ConfigurationTheme.Surface;
            configToolbarPanel.Controls.Add(configTextSizeControlsPanel);
            configToolbarPanel.Controls.Add(configToolbarSubtitle);
            configToolbarPanel.Controls.Add(configToolbarTitle);
            configToolbarPanel.Controls.Add(configThemeMark);
            configToolbarPanel.Location = new Point(10, 10);
            configToolbarPanel.Name = "configToolbarPanel";
            configToolbarPanel.Size = new Size(840, 58);
            configToolbarPanel.TabIndex = 0;
            configToolbarPanel.Paint += ConfigurationToolbarPanel_Paint;
            CreateRoundedPanel(configToolbarPanel);
            EnableDoubleBuffering(configToolbarPanel);

            configThemeMark.BackColor = Color.Transparent;
            configThemeMark.Location = new Point(14, 14);
            configThemeMark.Name = "configThemeMark";
            configThemeMark.Size = new Size(30, 30);
            configThemeMark.TabStop = false;
            configThemeMark.Paint += ConfigThemeMark_Paint;

            configToolbarTitle.AutoSize = true;
            configToolbarTitle.Font = new Font("Segoe UI Semibold", 10.5F);
            configToolbarTitle.ForeColor = ConfigurationTheme.TextPrimary;
            configToolbarTitle.Location = new Point(54, 9);
            configToolbarTitle.Name = "configToolbarTitle";
            configToolbarTitle.Text = "Configuration text";

            configToolbarSubtitle.AutoSize = true;
            configToolbarSubtitle.Font = new Font("Segoe UI", 8.5F);
            configToolbarSubtitle.ForeColor = ConfigurationTheme.TextMuted;
            configToolbarSubtitle.Location = new Point(54, 31);
            configToolbarSubtitle.Name = "configToolbarSubtitle";
            configToolbarSubtitle.Text = "Readable settings, saved to config.json";

            configTextSizeControlsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            configTextSizeControlsPanel.BackColor = Color.Transparent;
            configTextSizeControlsPanel.Controls.Add(configTextSizeCaption);
            configTextSizeControlsPanel.Controls.Add(btnConfigFontDecrease);
            configTextSizeControlsPanel.Controls.Add(lblConfigFontScale);
            configTextSizeControlsPanel.Controls.Add(btnConfigFontIncrease);
            configTextSizeControlsPanel.Controls.Add(btnConfigFontReset);
            configTextSizeControlsPanel.FlowDirection = FlowDirection.LeftToRight;
            configTextSizeControlsPanel.Location = new Point(551, 12);
            configTextSizeControlsPanel.Name = "configTextSizeControlsPanel";
            configTextSizeControlsPanel.Size = new Size(275, 34);
            configTextSizeControlsPanel.TabIndex = 1;
            configTextSizeControlsPanel.WrapContents = false;

            configTextSizeCaption.AutoSize = false;
            configTextSizeCaption.Font = new Font("Segoe UI Semibold", 9F);
            configTextSizeCaption.ForeColor = ConfigurationTheme.TextSecondary;
            configTextSizeCaption.Margin = new Padding(0, 0, 8, 0);
            configTextSizeCaption.Name = "configTextSizeCaption";
            configTextSizeCaption.Size = new Size(65, 30);
            configTextSizeCaption.Text = "Text size";
            configTextSizeCaption.TextAlign = ContentAlignment.MiddleRight;

            ConfigureConfigurationScaleButton(btnConfigFontDecrease, "−", 32);
            btnConfigFontDecrease.AccessibleName = "Decrease configuration text size";
            btnConfigFontDecrease.Click += ConfigFontDecrease_Click;

            lblConfigFontScale.Font = new Font("Segoe UI Semibold", 9.5F);
            lblConfigFontScale.ForeColor = ConfigurationTheme.TextPrimary;
            lblConfigFontScale.Margin = new Padding(4, 0, 4, 0);
            lblConfigFontScale.Name = "lblConfigFontScale";
            lblConfigFontScale.Size = new Size(52, 30);
            lblConfigFontScale.Text = "125%";
            lblConfigFontScale.TextAlign = ContentAlignment.MiddleCenter;

            ConfigureConfigurationScaleButton(btnConfigFontIncrease, "+", 32);
            btnConfigFontIncrease.AccessibleName = "Increase configuration text size";
            btnConfigFontIncrease.Click += ConfigFontIncrease_Click;

            ConfigureConfigurationScaleButton(btnConfigFontReset, "Reset", 58);
            btnConfigFontReset.AccessibleName = "Reset configuration text size";
            btnConfigFontReset.Margin = new Padding(8, 0, 0, 0);
            btnConfigFontReset.Click += ConfigFontReset_Click;

            var configToolTip = new ToolTip(this.components);
            configToolTip.SetToolTip(btnConfigFontDecrease, "Decrease configuration text size");
            configToolTip.SetToolTip(btnConfigFontIncrease, "Increase configuration text size");
            configToolTip.SetToolTip(btnConfigFontReset, "Reset configuration text size to 125%");

            // Configuration content - category navigation with a descriptor-backed
            // modern surface and the live PropertyGrid retained as a complete fallback.
            configContentSplit.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            configContentSplit.BackColor = ConfigurationTheme.Border;
            configContentSplit.FixedPanel = FixedPanel.Panel1;
            configContentSplit.Location = new Point(10, 78);
            configContentSplit.Name = "configContentSplit";
            configContentSplit.Panel1.BackColor = ConfigurationTheme.Surface;
            configContentSplit.Panel1.Controls.Add(configCategoryList);
            configContentSplit.Panel1.Controls.Add(configCategorySubtitle);
            configContentSplit.Panel1.Controls.Add(configCategoryTitle);
            configContentSplit.Panel1MinSize = 190;
            configContentSplit.Panel2.BackColor = ConfigurationTheme.Canvas;
            configContentSplit.Panel2.Controls.Add(configSectionHeaderPanel);
            configContentSplit.Panel2.Controls.Add(configSettingsTree);
            configContentSplit.Panel2.Controls.Add(pgContainer);
            configContentSplit.Panel2.Padding = new Padding(0, 72, 0, 0);
            configContentSplit.Panel2MinSize = 320;
            configContentSplit.Size = new Size(840, 452);
            configContentSplit.SplitterDistance = 220;
            configContentSplit.SplitterWidth = 6;
            configContentSplit.TabIndex = 1;

            configCategoryTitle.AutoSize = true;
            configCategoryTitle.Font = new Font("Segoe UI Semibold", 12F);
            configCategoryTitle.ForeColor = ConfigurationTheme.TextPrimary;
            configCategoryTitle.Location = new Point(16, 14);
            configCategoryTitle.Name = "configCategoryTitle";
            configCategoryTitle.Text = "Settings";

            configCategorySubtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            configCategorySubtitle.Font = new Font("Segoe UI", 9F);
            configCategorySubtitle.ForeColor = ConfigurationTheme.TextMuted;
            configCategorySubtitle.Location = new Point(16, 39);
            configCategorySubtitle.Name = "configCategorySubtitle";
            configCategorySubtitle.Size = new Size(188, 34);
            configCategorySubtitle.Text = "Choose an area to configure";

            configCategoryList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            configCategoryList.BackColor = ConfigurationTheme.Surface;
            configCategoryList.BorderStyle = BorderStyle.None;
            configCategoryList.DrawMode = DrawMode.OwnerDrawFixed;
            configCategoryList.Font = new Font("Segoe UI Semibold", 11F);
            configCategoryList.ForeColor = ConfigurationTheme.TextSecondary;
            configCategoryList.IntegralHeight = false;
            configCategoryList.ItemHeight = 44;
            configCategoryList.Location = new Point(8, 78);
            configCategoryList.Name = "configCategoryList";
            configCategoryList.Size = new Size(204, 366);
            configCategoryList.TabIndex = 0;
            configCategoryList.Cursor = Cursors.Hand;
            configCategoryList.DrawItem += ConfigCategoryList_DrawItem;
            configCategoryList.MouseLeave += ConfigCategoryList_MouseLeave;
            configCategoryList.MouseMove += ConfigCategoryList_MouseMove;
            configCategoryList.SelectedIndexChanged += ConfigCategoryList_SelectedIndexChanged;

            configSectionHeaderPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            configSectionHeaderPanel.BackColor = ConfigurationTheme.Surface;
            configSectionHeaderPanel.Controls.Add(configSectionDescription);
            configSectionHeaderPanel.Controls.Add(configSectionTitle);
            configSectionHeaderPanel.Location = new Point(0, 0);
            configSectionHeaderPanel.Name = "configSectionHeaderPanel";
            configSectionHeaderPanel.Size = new Size(614, 64);
            configSectionHeaderPanel.TabIndex = 0;

            configSectionTitle.AutoSize = true;
            configSectionTitle.Font = new Font("Segoe UI Semibold", 14F);
            configSectionTitle.ForeColor = ConfigurationTheme.TextPrimary;
            configSectionTitle.Location = new Point(16, 9);
            configSectionTitle.Name = "configSectionTitle";
            configSectionTitle.Text = "General";

            configSectionDescription.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            configSectionDescription.AutoEllipsis = true;
            configSectionDescription.Font = new Font("Segoe UI", 9.5F);
            configSectionDescription.ForeColor = ConfigurationTheme.TextMuted;
            configSectionDescription.Location = new Point(17, 35);
            configSectionDescription.Name = "configSectionDescription";
            configSectionDescription.Size = new Size(580, 22);
            configSectionDescription.Text = "Core bot identity and application behavior";

            // Descriptor-backed settings surface for the focused categories.
            configSettingsTree.BackColor = ConfigurationTheme.Canvas;
            configSettingsTree.Dock = DockStyle.Fill;
            configSettingsTree.Location = new Point(0, 72);
            configSettingsTree.Name = "configSettingsTree";
            configSettingsTree.Size = new Size(614, 380);
            configSettingsTree.TabIndex = 0;

            // Property Grid Container - complete fallback for All settings.
            pgContainer.BackColor = ConfigurationTheme.Border;
            pgContainer.Dock = DockStyle.Fill;
            pgContainer.Location = new Point(0, 72);
            pgContainer.Name = "pgContainer";
            pgContainer.Padding = new Padding(1);
            pgContainer.Size = new Size(614, 380);
            EnableDoubleBuffering(pgContainer);

            // Property Grid - dark Poké Ball palette
            PG_Hub.BackColor = ConfigurationTheme.Surface;
            PG_Hub.CategoryForeColor = ConfigurationTheme.TextPrimary;
            PG_Hub.CategorySplitterColor = ConfigurationTheme.Border;
            PG_Hub.CommandsBackColor = ConfigurationTheme.Surface;
            PG_Hub.CommandsForeColor = ConfigurationTheme.TextPrimary;
            PG_Hub.Dock = DockStyle.Fill;
            PG_Hub.Font = new Font("Segoe UI", 15F);
            // Keep the settings viewport available for properties. The reskin's
            // section header already provides context, while PropertyGrid's
            // built-in help pane consumes roughly half the available height.
            PG_Hub.HelpVisible = false;
            PG_Hub.HelpBackColor = ConfigurationTheme.Surface;
            PG_Hub.HelpForeColor = ConfigurationTheme.TextSecondary;
            PG_Hub.LineColor = ConfigurationTheme.Border;
            PG_Hub.Location = new Point(1, 1);
            PG_Hub.Name = "PG_Hub";
            PG_Hub.PropertySort = PropertySort.Categorized;
            PG_Hub.Size = new Size(612, 378);
            PG_Hub.TabIndex = 0;
            PG_Hub.ToolbarVisible = false;
            PG_Hub.ViewBackColor = ConfigurationTheme.Canvas;
            PG_Hub.ViewForeColor = ConfigurationTheme.TextPrimary;
            pgContainer.Controls.Add(PG_Hub);
            PG_Hub.CreateControl();

            // MCP Panel
            mcpPanel.Dock = DockStyle.Fill;
            mcpPanel.Location = new Point(0, 60);
            mcpPanel.Name = "mcpPanel";
            mcpPanel.Size = new Size(860, 540);
            mcpPanel.TabIndex = NavigationMcpIndex;
            mcpPanel.Visible = false;

            // Testing Panel
            testingPanel.Dock = DockStyle.Fill;
            testingPanel.Location = new Point(0, 60);
            testingPanel.Name = "testingPanel";
            testingPanel.Size = new Size(860, 540);
            testingPanel.TabIndex = NavigationTestingIndex;
            testingPanel.Visible = false;

            // Logs Panel
            logsPanel.BackColor = Color.Transparent;
            logsPanel.Dock = DockStyle.Fill;
            logsPanel.Location = new Point(0, 60);
            logsPanel.Name = "logsPanel";
            logsPanel.Padding = new Padding(10);
            logsPanel.Size = new Size(860, 540);
            logsPanel.TabIndex = NavigationLogsIndex;
            logsPanel.Visible = false;
            EnableDoubleBuffering(logsPanel);

            // Logs Container - Cuztom style
            var logsContainer = new Panel();
            logsContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logsContainer.BackColor = Color.FromArgb(22, 32, 45);
            logsContainer.Location = new Point(10, 60);
            logsContainer.Margin = new Padding(0, 5, 0, 0);
            logsContainer.Name = "logsContainer";
            logsContainer.Padding = new Padding(2);
            logsContainer.Size = new Size(840, 470);
            CreateRoundedPanel(logsContainer);
            EnableDoubleBuffering(logsContainer);
            logsPanel.Controls.Add(logsContainer);
            logsPanel.Controls.Add(logsHeaderPanel);

            // Logs Header Panel - Cuztom style
            logsHeaderPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            logsHeaderPanel.BackColor = Color.FromArgb(22, 32, 45);
            logsHeaderPanel.Height = 45;
            logsHeaderPanel.Location = new Point(10, 10);
            logsHeaderPanel.Name = "logsHeaderPanel";
            logsHeaderPanel.Padding = new Padding(15, 8, 15, 8);
            logsHeaderPanel.Size = new Size(840, 45);
            logsHeaderPanel.TabIndex = 1;
            CreateRoundedPanel(logsHeaderPanel);
            EnableDoubleBuffering(logsHeaderPanel);

            // Search Panel
            searchPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            searchPanel.Controls.Add(logSearchBox);
            searchPanel.Height = 23;
            searchPanel.Location = new Point(15, 11);
            searchPanel.Name = "searchPanel";
            searchPanel.Size = new Size(380, 23);
            searchPanel.TabIndex = 0;
            searchPanel.BackColor = Color.FromArgb(22, 32, 45);

            // Log Search Box - Cuztom style
            logSearchBox.BackColor = Color.FromArgb(32, 38, 48);
            logSearchBox.BorderStyle = BorderStyle.FixedSingle;
            logSearchBox.Dock = DockStyle.Fill;
            logSearchBox.Font = ScaleFont(new Font("Segoe UI", 8.5F));
            logSearchBox.ForeColor = Color.FromArgb(239, 239, 239);
            logSearchBox.Location = new Point(0, 0);
            logSearchBox.Name = "logSearchBox";
            logSearchBox.PlaceholderText = "Search logs (Enter = next, Shift+Enter = previous, Esc = clear)...";
            logSearchBox.Size = new Size(380, 23);
            logSearchBox.TabIndex = 0;
            logSearchBox.TextChanged += LogSearchBox_TextChanged;
            logSearchBox.KeyDown += LogSearchBox_KeyDown;

            // Search Options Panel
            searchOptionsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            searchOptionsPanel.AutoSize = true;
            searchOptionsPanel.Controls.Add(btnCaseSensitive);
            searchOptionsPanel.Controls.Add(btnRegex);
            searchOptionsPanel.Controls.Add(btnWholeWord);
            searchOptionsPanel.FlowDirection = FlowDirection.LeftToRight;
            searchOptionsPanel.Height = 18;
            searchOptionsPanel.Location = new Point(400, 8);
            searchOptionsPanel.Name = "searchOptionsPanel";
            searchOptionsPanel.Size = new Size(100, 28);
            searchOptionsPanel.TabIndex = 1;
            searchOptionsPanel.BackColor = Color.FromArgb(22, 32, 45);
            searchOptionsPanel.WrapContents = false;

            ConfigureSearchOption(btnCaseSensitive, "Aa", "Case sensitive search");
            ConfigureSearchOption(btnRegex, ".*", "Regular expression search");
            ConfigureSearchOption(btnWholeWord, "W", "Whole word search");

            // Search Status Label
            searchStatusLabel.AutoSize = true;
            searchStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            searchStatusLabel.Font = ScaleFont(new Font("Segoe UI", 7.5F));
            searchStatusLabel.ForeColor = Color.FromArgb(139, 179, 217);
            searchStatusLabel.Location = new Point(660, 14);
            searchStatusLabel.Name = "searchStatusLabel";
            searchStatusLabel.Size = new Size(80, 12);
            searchStatusLabel.TabIndex = 2;
            searchStatusLabel.Text = "";
            searchStatusLabel.TextAlign = ContentAlignment.MiddleRight;

            // Clear Logs Button - Cuztom style
            btnClearLogs.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClearLogs.BackColor = Color.FromArgb(236, 98, 95);
            btnClearLogs.FlatAppearance.BorderSize = 0;
            btnClearLogs.FlatStyle = FlatStyle.Flat;
            btnClearLogs.Font = ScaleFont(new Font("Segoe UI", 7.5F, FontStyle.Bold));
            btnClearLogs.ForeColor = Color.White;
            btnClearLogs.Location = new Point(750, 10);
            btnClearLogs.Name = "btnClearLogs";
            btnClearLogs.Size = new Size(75, 23);
            btnClearLogs.TabIndex = 3;
            btnClearLogs.Text = "CLEAR";
            btnClearLogs.UseVisualStyleBackColor = false;
            btnClearLogs.Cursor = Cursors.Hand;
            btnClearLogs.Click += BtnClearLogs_Click;
            ConfigureGlowButton(btnClearLogs);
            CreateRoundedButton(btnClearLogs);


            // Rich Text Box - Cuztom style
            RTB_Logs.BackColor = Color.FromArgb(32, 38, 48);
            RTB_Logs.BorderStyle = BorderStyle.None;
            RTB_Logs.Dock = DockStyle.Fill;
            RTB_Logs.Font = ScaleFont(new Font("Consolas", 9F));
            RTB_Logs.ForeColor = Color.FromArgb(239, 239, 239);
            RTB_Logs.Location = new Point(2, 2);
            RTB_Logs.Name = "RTB_Logs";
            RTB_Logs.ReadOnly = true;
            RTB_Logs.Size = new Size(836, 466);
            RTB_Logs.TabIndex = 0;
            RTB_Logs.Text = "";
            RTB_Logs.HideSelection = false;
            RTB_Logs.KeyDown += RTB_Logs_KeyDown;
            logsContainer.Controls.Add(RTB_Logs);

            // Add controls to logsHeaderPanel
            logsHeaderPanel.Controls.Add(searchPanel);
            logsHeaderPanel.Controls.Add(searchOptionsPanel);
            logsHeaderPanel.Controls.Add(searchStatusLabel);
            logsHeaderPanel.Controls.Add(btnClearLogs);

            // Hidden tab control for compatibility
            TC_Main = new TabControl { Visible = false };
            Tab_Bots = new TabPage();
            Tab_Hub = new TabPage();
            Tab_Mcp = new TabPage();
            Tab_Testing = new TabPage();
            Tab_Logs = new TabPage();
            TC_Main.TabPages.Add(Tab_Bots);
            TC_Main.TabPages.Add(Tab_Hub);
            TC_Main.TabPages.Add(Tab_Mcp);
            TC_Main.TabPages.Add(Tab_Testing);
            TC_Main.TabPages.Add(Tab_Logs);
            TC_Main.SendToBack();

            Controls.Add(mainLayoutPanel);

            // Resume layouts
            mainLayoutPanel.ResumeLayout(false);
            sidebarPanel.ResumeLayout(false);
            navButtonsPanel.ResumeLayout(false);
            sidebarBottomPanel.ResumeLayout(false);
            headerPanel.ResumeLayout(false);
            headerPanel.PerformLayout();
            controlButtonsPanel.ResumeLayout(false);
            contentPanel.ResumeLayout(false);
            botsPanel.ResumeLayout(false);
            botHeaderPanel.ResumeLayout(false);
            addBotPanel.ResumeLayout(false);
            addBotPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)NUD_Port).EndInit();
            configTextSizeControlsPanel.ResumeLayout(false);
            configToolbarPanel.ResumeLayout(false);
            configToolbarPanel.PerformLayout();
            configSectionHeaderPanel.ResumeLayout(false);
            configSectionHeaderPanel.PerformLayout();
            configContentSplit.Panel1.ResumeLayout(false);
            configContentSplit.Panel1.PerformLayout();
            configContentSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)configContentSplit).EndInit();
            configContentSplit.ResumeLayout(false);
            hubPanel.ResumeLayout(false);
            logsPanel.ResumeLayout(false);
            logsHeaderPanel.ResumeLayout(false);
            logsHeaderPanel.PerformLayout();
            searchPanel.ResumeLayout(false);
            searchOptionsPanel.ResumeLayout(false);
            ResumeLayout(false);

            ConfigureSystemTray();
        }

        #endregion

        #region Font Scaling

        private Font ScaleFont(Font baseFont)
        {
            using (Graphics g = CreateGraphics())
            {
                float dpiScale = g.DpiX / 96f;
                float scaledSize = baseFont.Size * dpiScale;

                if (ClientSize.Width < 900)
                {
                    scaledSize *= 0.85f;
                }
                else if (ClientSize.Width < 1100)
                {
                    scaledSize *= 0.92f;
                }

                scaledSize = Math.Max(7f, scaledSize);

                if (ClientSize.Width < 800)
                {
                    if (baseFont.Size >= 24)
                        scaledSize = Math.Min(scaledSize, 16f);
                    else if (baseFont.Size >= 11)
                        scaledSize = Math.Min(scaledSize, 9f);
                    else
                        scaledSize = Math.Min(scaledSize, 8f);
                }

                return new Font(baseFont.FontFamily, scaledSize, baseFont.Style);
            }
        }

        #endregion

        #region UI Helper Methods

        private void EnableDoubleBuffering(Control control)
        {
            if (control == null) return;

            typeof(Control).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, control, new object[] { true });
        }

        private void HeaderPanel_Resize(object sender, EventArgs e)
        {
            if (controlButtonsPanel != null && headerPanel != null)
            {
                int rightMargin = 20;
                int minLeftPosition = titleLabel.Right + 20;

                int availableWidth = headerPanel.Width - minLeftPosition - rightMargin;

                controlButtonsPanel.MaximumSize = new Size(400, 32);
                controlButtonsPanel.WrapContents = false;

                int desiredX = headerPanel.Width - controlButtonsPanel.Width - rightMargin;
                controlButtonsPanel.Location = new Point(Math.Max(minLeftPosition, desiredX), 16);
            }
        }

        private void ConfigureSearchOption(CheckBox checkBox, string text, string tooltip)
        {
            checkBox.Appearance = Appearance.Button;
            checkBox.BackColor = Color.FromArgb(45, 51, 61);
            checkBox.FlatAppearance.BorderSize = 1;
            checkBox.FlatAppearance.BorderColor = Color.FromArgb(32, 38, 48);
            checkBox.FlatAppearance.CheckedBackColor = Color.FromArgb(102, 192, 244);
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.Font = ScaleFont(new Font("Segoe UI", 6.5F, FontStyle.Bold));
            checkBox.ForeColor = Color.FromArgb(200, 200, 200);
            checkBox.Margin = new Padding(0, 0, 3, 0);
            checkBox.Size = new Size(26, 26);
            checkBox.Text = text;
            checkBox.TextAlign = ContentAlignment.MiddleCenter;
            checkBox.UseVisualStyleBackColor = false;
            checkBox.Cursor = Cursors.Hand;
            
            // Ensure checked state changes text color for better visibility
            checkBox.CheckedChanged += (s, e) => 
            {
                if (checkBox.Checked)
                {
                    checkBox.ForeColor = Color.FromArgb(22, 32, 45); // Dark text on light background
                }
                else
                {
                    checkBox.ForeColor = Color.FromArgb(200, 200, 200); // Light text on dark background
                }
            };

            var toolTip = new ToolTip();
            toolTip.SetToolTip(checkBox, tooltip);
        }

        private static void ConfigureConfigurationScaleButton(Button button, string text, int width)
        {
            button.BackColor = ConfigurationTheme.Canvas;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = ConfigurationTheme.BorderStrong;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseDownBackColor = ConfigurationTheme.AccentPressed;
            button.FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceSelected;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Segoe UI Semibold", text == "Reset" ? 8.5F : 11F);
            button.ForeColor = ConfigurationTheme.TextPrimary;
            button.Margin = new Padding(0);
            button.Size = new Size(width, 30);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
        }

        private void ConfigureNavButton(Button btn, string text, int index, string tooltip, Color neonColor)
        {
            btn.BackColor = Color.FromArgb(23, 29, 37);
            btn.Cursor = Cursors.Hand;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 38, 48);
            btn.FlatStyle = FlatStyle.Flat;
            btn.Font = ScaleFont(new Font("Segoe UI", 10F, FontStyle.Regular));
            btn.ForeColor = Color.FromArgb(139, 179, 217);
            btn.Location = new Point(0, 10 + (index * 45));
            btn.Margin = new Padding(0, 0, 0, 5);
            btn.Name = $"btnNav{text.Replace(" ", "")}";
            btn.Padding = new Padding(50, 0, 0, 0);
            btn.Size = new Size(240, 40);
            btn.TabIndex = index;
            btn.Text = text;
            btn.TextAlign = ContentAlignment.MiddleLeft;
            btn.UseVisualStyleBackColor = false;
            btn.Tag = new NavButtonState { NeonColor = neonColor, Index = index };

            btn.Paint += (s, e) => {
                var navState = btn.Tag as NavButtonState;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw background
                using (var bgBrush = new SolidBrush(btn.BackColor))
                {
                    g.FillRectangle(bgBrush, btn.ClientRectangle);
                }

                // Draw left accent bar when selected
                if (navState.IsSelected)
                {
                    using (var accentBrush = new SolidBrush(navState.NeonColor))
                    {
                        g.FillRectangle(accentBrush, 0, 0, 3, btn.Height);
                    }

                    // Draw neon glow effect
                    for (int i = 1; i <= 2; i++)
                    {
                        using (var glowBrush = new SolidBrush(Color.FromArgb(20 / i, navState.NeonColor)))
                        {
                            g.FillRectangle(glowBrush, 0, 0, 3 + i * 2, btn.Height);
                        }
                    }

                    // Update text color to match neon
                    btn.ForeColor = navState.NeonColor;
                }
                else
                {
                    btn.ForeColor = Color.FromArgb(139, 179, 217);
                }

                // Draw icon
                int iconSize = 18;
                var iconRect = new Rectangle(15, (btn.Height - iconSize) / 2, iconSize, iconSize);
                using var iconFont = new Font("Segoe MDL2 Assets", 13F);
                string iconText = index switch
                {
                    NavigationBotsIndex => "\uE77B", // Bots icon
                    NavigationConfigurationIndex => "\uE713", // Settings icon
                    NavigationMcpIndex => "\uE968", // Connected service icon
                    NavigationTestingIndex => "\uE9D9", // Developer tools icon
                    NavigationLogsIndex => "\uE7C3", // Logs icon
                    NavigationPanelCount => "\uE74A", // Minimize to tray
                    _ => "\uE700"
                };

                var iconColor = navState.IsSelected ? navState.NeonColor : Color.FromArgb(139, 179, 217);
                using var iconBrush = new SolidBrush(iconColor);
                var textSize = g.MeasureString(iconText, iconFont);
                var textX = iconRect.X + (iconRect.Width - textSize.Width) / 2;
                var textY = iconRect.Y + (iconRect.Height - textSize.Height) / 2;
                g.DrawString(iconText, iconFont, iconBrush, textX, textY);

                // Draw text with proper font
                var textRect = new Rectangle(50, 0, btn.Width - 50, btn.Height);
                TextRenderer.DrawText(g, btn.Text, btn.Font, textRect, btn.ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };

            btn.Click += (s, e) => {
                if (index >= NavigationPanelCount)
                    return; // Don't select the tray button.

                // Update all nav buttons
                foreach (Button navBtn in navButtonsPanel.Controls.OfType<Button>())
                {
                    if (navBtn.Tag is NavButtonState state)
                    {
                        state.IsSelected = false;
                        navBtn.Invalidate();
                    }
                }

                // Select this button
                var navState = btn.Tag as NavButtonState;
                navState.IsSelected = true;
                btn.Invalidate();

                TransitionPanels(index);

                titleLabel.Text = index switch
                {
                    NavigationBotsIndex => "Bot Management",
                    NavigationConfigurationIndex => "Configuration",
                    NavigationMcpIndex => "MCP Control Plane",
                    NavigationTestingIndex => "Testing",
                    NavigationLogsIndex => "System Logs",
                    _ => "PokéBot"
                };
            };

            ConfigureHoverAnimation(btn);

            // Select first button by default
            if (index == NavigationBotsIndex)
            {
                var navState = btn.Tag as NavButtonState;
                navState.IsSelected = true;
            }
        }

        private void ConfigureEnhancedControlButton(Button btn, string text, Color baseColor, string iconText)
        {
            var dpiScale = this.DeviceDpi / 96f;
            
            // Modern glass-morphism design with responsive sizing
            btn.BackColor = Color.FromArgb(25, 30, 40);
            btn.Cursor = Cursors.Hand;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btn.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btn.FlatStyle = FlatStyle.Flat;
            btn.Font = ScaleFont(new Font("Segoe UI Semibold", 9F));
            btn.ForeColor = baseColor;
            btn.Margin = new Padding(5, 0, 5, 0);
            btn.Name = $"btn{text.Replace(" ", "")}";
            btn.Padding = new Padding((int)(12 * dpiScale), (int)(6 * dpiScale), (int)(12 * dpiScale), (int)(6 * dpiScale));
            btn.TabIndex = 0;
            btn.Text = $"{iconText}  {text}";
            btn.UseVisualStyleBackColor = false;
            btn.AutoSize = true;
            btn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btn.MinimumSize = new Size((int)(85 * dpiScale), (int)(32 * dpiScale));
            btn.MaximumSize = new Size((int)(120 * dpiScale), (int)(36 * dpiScale));

            var animState = new EnhancedButtonAnimationState
            {
                BaseColor = baseColor,
                IconText = iconText,
                IsActive = false
            };
            btn.Tag = animState;

            // Create rounded corners with custom region
            CreateRoundedButton(btn);
            ConfigureEnhancedHoverAnimation(btn);

            // Add custom paint for modern glass effect
            btn.Paint += EnhancedControlButton_Paint;
        }

        private void ConfigureEnhancedHoverAnimation(Button btn)
        {
            var animState = btn.Tag as EnhancedButtonAnimationState;

            btn.MouseEnter += (s, e) => {
                animState.IsHovering = true;
                animState.AnimationStart = DateTime.Now;
                btn.Invalidate();
            };

            btn.MouseLeave += (s, e) => {
                animState.IsHovering = false;
                animState.AnimationStart = DateTime.Now;
                btn.Invalidate();
            };

            btn.MouseDown += (s, e) => {
                animState.IsPressed = true;
                btn.Invalidate();
            };

            btn.MouseUp += (s, e) => {
                animState.IsPressed = false;
                btn.Invalidate();
            };
        }

        private void ConfigureNumericUpDown(NumericUpDown nud, int x, int y, int width)
        {
            nud.BackColor = Color.FromArgb(32, 38, 48);
            nud.BorderStyle = BorderStyle.None;
            nud.Font = ScaleFont(new Font("Segoe UI", 9F));
            nud.ForeColor = Color.FromArgb(239, 239, 239);
            nud.Location = new Point(x, y);
            nud.Name = nud.Name;
            nud.Size = new Size(width, 23);
            nud.TabIndex = 1;
        }

        private void ConfigureComboBox(ComboBox cb, int x, int y, int width)
        {
            cb.BackColor = Color.FromArgb(32, 38, 48);
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.Font = ScaleFont(new Font("Segoe UI", 9F));
            cb.ForeColor = Color.FromArgb(239, 239, 239);
            cb.Location = new Point(x, y);
            cb.Name = cb.Name;
            cb.Size = new Size(width, 23);
            cb.TabIndex = 2;
        }

        private void ConfigureHoverAnimation(Control control)
        {
            var animState = control.Tag as ButtonAnimationState ?? new ButtonAnimationState();
            control.Tag = animState;

            control.MouseEnter += (s, e) => {
                animState.IsHovering = true;
                animState.AnimationStart = DateTime.Now;
            };

            control.MouseLeave += (s, e) => {
                animState.IsHovering = false;
                animState.AnimationStart = DateTime.Now;
            };
        }

        private void ConfigureGlowButton(Button btn)
        {
            ConfigureHoverAnimation(btn);

            btn.Paint += (s, e) => {
                var animState = btn.Tag as ButtonAnimationState;
                if (animState != null && animState.HoverProgress > 0)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                    var glowAlpha = (int)(40 * animState.HoverProgress);
                    using (var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, btn.BackColor)))
                    {
                        for (int i = 1; i <= 2; i++)
                        {
                            var rect = new Rectangle(-i * 2, -i * 2, btn.Width + i * 4, btn.Height + i * 4);
                            e.Graphics.FillRectangle(glowBrush, rect);
                        }
                    }
                }
            };
        }

        private void CreateRoundedPanel(Panel panel)
        {
            panel.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                panel.Region?.Dispose();

                using var path = new GraphicsPath();
                var rect = panel.ClientRectangle;
                rect.Inflate(-1, -1);
                GraphicsExtensions.AddRoundedRectangle(path, rect, 4);
                panel.Region = new Region(path);
            };
        }

        private void CreateRoundedButton(Button btn)
        {
            btn.Paint += (s, e) => {
                if (btn.Region != null) btn.Region.Dispose();

                using var path = new GraphicsPath();
                var rect = btn.ClientRectangle;
                int radius = Math.Min(3, Math.Min(rect.Width, rect.Height) / 4);
                GraphicsExtensions.AddRoundedRectangle(path, rect, radius);
                btn.Region = new Region(path);
            };

            btn.Invalidate();
        }

        private void CreateCircularRegion(Control control)
        {
            control.Region?.Dispose();

            using var path = new GraphicsPath();
            path.AddEllipse(0, 0, control.Width, control.Height);
            control.Region = new Region(path);
        }

        private void ConfigureUpdateButton()
        {
            // Scale-aware indicator sizing
            var dpiScale = this.DeviceDpi / 96f;
            var indicatorSize = (int)(8 * dpiScale);
            var indicatorMargin = (int)(18 * dpiScale);
            var indicatorTop = (int)(13 * dpiScale);
            
            statusIndicator.BackColor = Color.FromArgb(100, 100, 100);
            statusIndicator.Size = new Size(indicatorSize, indicatorSize);
            statusIndicator.Location = new Point(btnUpdate.ClientSize.Width - indicatorMargin, indicatorTop);
            statusIndicator.Name = "statusIndicator";
            statusIndicator.Enabled = false;
            statusIndicator.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            CreateCircularRegion(statusIndicator);
            btnUpdate.Controls.Add(statusIndicator);
            statusIndicator.BringToFront();

            statusIndicator.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = statusIndicator.ClientRectangle;
                rect.Inflate(-1, -1);

                using var brush = new SolidBrush(statusIndicator.BackColor);
                e.Graphics.FillEllipse(brush, rect);

                var mainForm = (Main)statusIndicator.FindForm();
                if (mainForm != null && mainForm.hasUpdate)
                {
                    var highlightRect = new Rectangle(1, 1, 3, 3);
                    using var highlightBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                    e.Graphics.FillEllipse(highlightBrush, highlightRect);
                }
            };

            var updateTooltip = new ToolTip();
            updateTooltip.SetToolTip(btnUpdate, "Check for updates");
            btnUpdate.MouseEnter += (s, e) => {
                var mainForm = (Main)btnUpdate.FindForm();
                if (mainForm != null && mainForm.hasUpdate)
                {
                    updateTooltip.SetToolTip(btnUpdate, "Update available! Click to download.");
                }
                else
                {
                    updateTooltip.SetToolTip(btnUpdate, "No updates available");
                }
            };

            btnUpdate.Resize += (s, e) => {
                if (statusIndicator != null && btnUpdate.Controls.Contains(statusIndicator))
                {
                    var dpiScale = this.DeviceDpi / 96f;
                    var indicatorMargin = (int)(18 * dpiScale);
                    var indicatorTop = (int)(13 * dpiScale);
                    statusIndicator.Location = new Point(btnUpdate.ClientSize.Width - indicatorMargin, indicatorTop);
                }
            };

            btnUpdate.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                var animState = btnUpdate.Tag as ButtonAnimationState;

                if (animState != null && animState.HoverProgress > 0 && animState.IsHovering)
                {
                    using var glowBrush = new SolidBrush(Color.FromArgb((int)(20 * animState.HoverProgress), 102, 192, 244));
                    e.Graphics.FillRectangle(glowBrush, btnUpdate.ClientRectangle);
                }

                var iconColor = btnUpdate.ForeColor;
                if (animState != null && animState.HoverProgress > 0)
                {
                    iconColor = Color.FromArgb(
                        (int)(139 + (239 - 139) * animState.HoverProgress),
                        (int)(179 + (239 - 179) * animState.HoverProgress),
                        (int)(217 + (239 - 217) * animState.HoverProgress)
                    );
                }

                using var iconFont = new Font("Segoe MDL2 Assets", 11F);
                var iconText = "\uE895";

                using var iconBrush = new SolidBrush(iconColor);
                var iconSize = e.Graphics.MeasureString(iconText, iconFont);

                var iconX = 10;
                var iconY = (btnUpdate.Height - iconSize.Height) / 2;
                e.Graphics.DrawString(iconText, iconFont, iconBrush, iconX, iconY);

                using var textFont = ScaleFont(new Font("Segoe UI", 7.5F, FontStyle.Regular));
                var text = "CHECK FOR UPDATES";

                var textSize = e.Graphics.MeasureString(text, textFont);
                var textX = iconX + iconSize.Width + 5;
                var textY = (btnUpdate.Height - textSize.Height) / 2;
                e.Graphics.DrawString(text, textFont, iconBrush, textX, textY);

                var mainForm = (Main)btnUpdate.FindForm();
                if (mainForm != null && mainForm.hasUpdate && statusIndicator != null)
                {
                    var indicatorBounds = new Rectangle(
                        statusIndicator.Left - 2,
                        statusIndicator.Top - 2,
                        statusIndicator.Width + 4,
                        statusIndicator.Height + 4
                    );

                    for (int i = 2; i > 0; i--)
                    {
                        var glowAlpha = 15 / i; // Animation removed
                        using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, 102, 192, 244));
                        var glowRect = new Rectangle(
                            indicatorBounds.X - i * 2,
                            indicatorBounds.Y - i * 2,
                            indicatorBounds.Width + i * 4,
                            indicatorBounds.Height + i * 4
                        );
                        e.Graphics.FillEllipse(glowBrush, glowRect);
                    }
                }
            };
        }

        #endregion

        #region Paint Event Handlers

        private void LogoPanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var rect = logoPanel.ClientRectangle;

            // Draw metallic gradient background
            using (var bgPath = new GraphicsPath())
            {
                bgPath.AddRectangle(rect);
                using (var pgBrush = new PathGradientBrush(bgPath))
                {
                    pgBrush.CenterColor = Color.FromArgb(35, 42, 54);
                    pgBrush.SurroundColors = new[] { Color.FromArgb(23, 29, 37) };
                    pgBrush.FocusScales = new PointF(0.8f, 0.5f);
                    e.Graphics.FillRectangle(pgBrush, rect);
                }
            }

            // Draw brass/copper accent lines
            using (var pen = new Pen(Color.FromArgb(40, 184, 115, 51), 1))
            {
                e.Graphics.DrawLine(pen, 0, 0, rect.Width, 0);
                e.Graphics.DrawLine(pen, 0, rect.Height - 1, rect.Width, rect.Height - 1);
            }

            // Draw main logo text with metallic effect (static, no animation)
            DrawStaticMetallicText(e.Graphics, rect);
        }

        private void DrawStaticMetallicText(Graphics g, Rectangle rect)
        {
            using var font = ScaleFont(new Font("Segoe UI", 14F, FontStyle.Bold));
            var text = "POKÉBOT";
            var textSize = g.MeasureString(text, font);
            var x = (rect.Width - textSize.Width) / 2;
            var y = (rect.Height - textSize.Height) / 2;

            // Create metallic gradient
            var textRect = new RectangleF(x, y, textSize.Width, textSize.Height);
            using (var metalBrush = new LinearGradientBrush(
                textRect,
                Color.FromArgb(255, 220, 180, 140), // Light brass
                Color.FromArgb(255, 139, 69, 19),   // Dark brass
                LinearGradientMode.Vertical))
            {
                metalBrush.SetBlendTriangularShape(0.5f);

                // Shadow
                using (var shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    g.DrawString(text, font, shadowBrush, x + 2, y + 2);
                }

                // Main text (static, no glow effects)
                g.DrawString(text, font, metalBrush, x, y);
            }
        }


        private void PokeBallLogoPanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var rect = logoPanel.ClientRectangle;

            using (var background = new SolidBrush(Color.FromArgb(23, 29, 37)))
                e.Graphics.FillRectangle(background, rect);
            using (var border = new Pen(Color.FromArgb(48, 60, 73), 1))
                e.Graphics.DrawLine(border, 0, rect.Height - 1, rect.Width, rect.Height - 1);

            DrawPokeBall(e.Graphics, new Rectangle(20, (rect.Height - 28) / 2, 28, 28));

            using var font = new Font("Segoe UI Semibold", 14F);
            using var textBrush = new SolidBrush(Color.FromArgb(244, 246, 248));
            const string text = "POKÉBOT";
            var textSize = e.Graphics.MeasureString(text, font);
            e.Graphics.DrawString(text, font, textBrush, 58, (rect.Height - textSize.Height) / 2);
        }

        private void ConfigurationToolbarPanel_Paint(object sender, PaintEventArgs e)
        {
            using var accent = new Pen(ConfigurationTheme.Accent, 2);
            e.Graphics.DrawLine(accent, 0, 8, 0, configToolbarPanel.Height - 8);
        }

        private void ConfigThemeMark_Paint(object sender, PaintEventArgs e)
        {
            DrawPokeBall(e.Graphics, new Rectangle(1, 1, configThemeMark.Width - 2, configThemeMark.Height - 2));
        }

        private static void DrawPokeBall(Graphics graphics, Rectangle bounds)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            path.AddEllipse(bounds);
            var state = graphics.Save();
            graphics.SetClip(path);

            using (var red = new SolidBrush(ConfigurationTheme.Accent))
                graphics.FillRectangle(red, bounds.X, bounds.Y, bounds.Width, bounds.Height / 2);
            using (var white = new SolidBrush(ConfigurationTheme.TextPrimary))
                graphics.FillRectangle(white, bounds.X, bounds.Y + bounds.Height / 2, bounds.Width, bounds.Height / 2 + 1);
            using (var divider = new Pen(ConfigurationTheme.Ink, Math.Max(3, bounds.Height / 8f)))
                graphics.DrawLine(divider, bounds.Left, bounds.Top + bounds.Height / 2, bounds.Right, bounds.Top + bounds.Height / 2);

            graphics.Restore(state);
            using (var outline = new Pen(ConfigurationTheme.Ink, 2))
                graphics.DrawEllipse(outline, bounds);

            var centerSize = Math.Max(7, bounds.Width / 3);
            var center = new Rectangle(
                bounds.Left + (bounds.Width - centerSize) / 2,
                bounds.Top + (bounds.Height - centerSize) / 2,
                centerSize,
                centerSize);
            using (var centerFill = new SolidBrush(ConfigurationTheme.TextPrimary))
                graphics.FillEllipse(centerFill, center);
            using (var centerOutline = new Pen(ConfigurationTheme.Ink, 2))
                graphics.DrawEllipse(centerOutline, center);
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            // Cuztom-style bottom border
            using var pen = new Pen(Color.FromArgb(22, 26, 32), 1);
            e.Graphics.DrawLine(pen, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
        }

        private void EnhancedControlButton_Paint(object sender, PaintEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;
            
            var animState = btn.Tag as EnhancedButtonAnimationState;
            if (animState == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = btn.ClientRectangle;
            
            // Create rounded rectangle path
            using var path = new GraphicsPath();
            int cornerRadius = 8;
            path.AddArc(rect.X, rect.Y, cornerRadius, cornerRadius, 180, 90);
            path.AddArc(rect.Right - cornerRadius - 1, rect.Y, cornerRadius, cornerRadius, 270, 90);
            path.AddArc(rect.Right - cornerRadius - 1, rect.Bottom - cornerRadius - 1, cornerRadius, cornerRadius, 0, 90);
            path.AddArc(rect.X, rect.Bottom - cornerRadius - 1, cornerRadius, cornerRadius, 90, 90);
            path.CloseFigure();

            // Draw gradient background with glass effect
            var baseAlpha = animState.IsPressed ? 60 : (animState.IsHovering ? 45 : 30);
            var glowAlpha = (int)(baseAlpha + (animState.HoverProgress * 25));
            
            using (var bgBrush = new LinearGradientBrush(rect, 
                Color.FromArgb(glowAlpha, animState.BaseColor),
                Color.FromArgb(glowAlpha / 2, animState.BaseColor),
                LinearGradientMode.Vertical))
            {
                g.FillPath(bgBrush, path);
            }

            // Draw glow effect on hover
            if (animState.HoverProgress > 0)
            {
                var glowSize = (int)(3 + animState.HoverProgress * 5);
                using var glowPath = new GraphicsPath();
                var glowRect = Rectangle.Inflate(rect, -1, -1);
                glowPath.AddArc(glowRect.X, glowRect.Y, cornerRadius, cornerRadius, 180, 90);
                glowPath.AddArc(glowRect.Right - cornerRadius - 1, glowRect.Y, cornerRadius, cornerRadius, 270, 90);
                glowPath.AddArc(glowRect.Right - cornerRadius - 1, glowRect.Bottom - cornerRadius - 1, cornerRadius, cornerRadius, 0, 90);
                glowPath.AddArc(glowRect.X, glowRect.Bottom - cornerRadius - 1, cornerRadius, cornerRadius, 90, 90);
                glowPath.CloseFigure();

                using var glowBrush = new SolidBrush(Color.FromArgb((int)(20 * animState.HoverProgress), animState.BaseColor));
                for (int i = 0; i < glowSize; i++)
                {
                    g.FillPath(glowBrush, glowPath);
                }
            }

            // Draw border with gradient
            var borderAlpha = animState.IsHovering ? 200 : 120;
            using (var borderPen = new Pen(Color.FromArgb(borderAlpha, animState.BaseColor), animState.IsPressed ? 2f : 1.5f))
            {
                g.DrawPath(borderPen, path);
            }

            // Draw inner highlight for glass effect
            if (!animState.IsPressed)
            {
                var highlightRect = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height / 2);
                using var highlightPath = new GraphicsPath();
                highlightPath.AddArc(highlightRect.X, highlightRect.Y, cornerRadius - 2, cornerRadius - 2, 180, 90);
                highlightPath.AddArc(highlightRect.Right - cornerRadius + 1, highlightRect.Y, cornerRadius - 2, cornerRadius - 2, 270, 90);
                highlightPath.AddLine(highlightRect.Right - 1, highlightRect.Bottom, highlightRect.X, highlightRect.Bottom);
                highlightPath.CloseFigure();

                using var highlightBrush = new LinearGradientBrush(highlightRect,
                    Color.FromArgb(30, 255, 255, 255),
                    Color.FromArgb(5, 255, 255, 255),
                    LinearGradientMode.Vertical);
                g.FillPath(highlightBrush, highlightPath);
            }

            // Draw text with shadow for depth
            var textColor = animState.IsHovering ? Color.White : animState.BaseColor;
            var textRect = rect;
            textRect.Offset(0, animState.IsPressed ? 1 : 0);

            // Draw text shadow
            using (var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            {
                textRect.Offset(1, 1);
                TextRenderer.DrawText(g, btn.Text, btn.Font, textRect, Color.FromArgb(50, 0, 0, 0),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                textRect.Offset(-1, -1);
            }

            // Draw main text
            TextRenderer.DrawText(g, btn.Text, btn.Font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void FLP_Bots_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            if (_currentModeImage != null && FLP_Bots.Controls.Count == 0)
            {
                var image = _currentModeImage;
                var panelWidth = FLP_Bots.ClientSize.Width;
                var panelHeight = FLP_Bots.ClientSize.Height;

                float scale = 0.35f;
                int imageWidth = (int)(image.Width * scale);
                int imageHeight = (int)(image.Height * scale);

                int x = (panelWidth - imageWidth) / 2;
                int y = 30;

                using (var attributes = new ImageAttributes())
                {
                    float[][] matrixItems = {
                        new float[] {1, 0, 0, 0, 0},
                        new float[] {0, 1, 0, 0, 0},
                        new float[] {0, 0, 1, 0, 0},
                        new float[] {0, 0, 0, 0.1f, 0},
                        new float[] {0, 0, 0, 0, 1}
                    };
                    var colorMatrix = new ColorMatrix(matrixItems);
                    attributes.SetColorMatrix(colorMatrix);

                    g.DrawImage(image,
                        new Rectangle(x, y, imageWidth, imageHeight),
                        0, 0, image.Width, image.Height,
                        GraphicsUnit.Pixel, attributes);
                }

                using var font = ScaleFont(new Font("Segoe UI", 11F, FontStyle.Regular));
                using var brush = new SolidBrush(Color.FromArgb(139, 179, 217));
                var text = "No bots configured. Add a bot using the form above.";
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, brush,
                    (panelWidth - size.Width) / 2,
                    y + imageHeight + 10);
            }
        }

        private void TransitionPanels(int index)
        {
            // Ensure proper panel layout before transitioning
            // This fixes the issue where panels are cut off when first visited after tray restore
            contentPanel.SuspendLayout();
            
            // Hide all panels
            botsPanel.Visible = false;
            hubPanel.Visible = false;
            mcpPanel.Visible = false;
            testingPanel.Visible = false;
            logsPanel.Visible = false;
            
            // Fix z-order to ensure headerPanel is on top
            contentPanel.Controls.SetChildIndex(headerPanel, contentPanel.Controls.Count - 1);
            
            // Reset and reapply header docking
            headerPanel.Dock = DockStyle.None;
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 60;
            
            // Show the selected panel
            switch (index)
            {
                case NavigationBotsIndex:
                    botsPanel.Dock = DockStyle.None;
                    botsPanel.Dock = DockStyle.Fill;
                    botsPanel.Visible = true;
                    break;
                case NavigationConfigurationIndex:
                    hubPanel.Dock = DockStyle.None;
                    hubPanel.Dock = DockStyle.Fill;
                    hubPanel.Visible = true;
                    break;
                case NavigationMcpIndex:
                    mcpPanel.Dock = DockStyle.None;
                    mcpPanel.Dock = DockStyle.Fill;
                    mcpPanel.Visible = true;
                    break;
                case NavigationTestingIndex:
                    testingPanel.Dock = DockStyle.None;
                    testingPanel.Dock = DockStyle.Fill;
                    testingPanel.Visible = true;
                    break;
                case NavigationLogsIndex:
                    logsPanel.Dock = DockStyle.None;
                    logsPanel.Dock = DockStyle.Fill;
                    logsPanel.Visible = true;
                    break;
            }
            
            contentPanel.ResumeLayout(true);
            contentPanel.PerformLayout();
            contentPanel.Refresh();
        }

        #endregion

        #region System Tray

        private void ConfigureSystemTray()
        {
            trayIcon.Icon = Icon;
            trayIcon.Text = "PokéBot Control Center";
            trayIcon.Visible = false;
            trayIcon.DoubleClick += TrayIcon_DoubleClick;

            trayContextMenu.BackColor = Color.FromArgb(27, 40, 56);
            trayContextMenu.Font = ScaleFont(new Font("Segoe UI", 9F));
            trayContextMenu.Renderer = new CuztomMenuRenderer();

            trayMenuShow.Text = "Show Window";
            trayMenuShow.ForeColor = Color.FromArgb(239, 239, 239);
            trayMenuShow.Click += TrayMenuShow_Click;

            var separator = new ToolStripSeparator();

            var trayMenuStart = new ToolStripMenuItem("Start All Bots");
            trayMenuStart.ForeColor = Color.FromArgb(90, 186, 71);
            trayMenuStart.Click += (s, e) => {
                RunningEnvironment.InitializeStart();
                foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    c.SendCommand(BotControlCommand.Start, false);
                LogUtil.LogInfo("All bots started from tray", "Tray");
            };

            var trayMenuStop = new ToolStripMenuItem("Stop All Bots");
            trayMenuStop.ForeColor = Color.FromArgb(236, 98, 95);
            trayMenuStop.Click += (s, e) => {
                RunningEnvironment.StopAll();
                LogUtil.LogInfo("All bots stopped from tray", "Tray");
            };

            var separator2 = new ToolStripSeparator();

            trayMenuExit.Text = "Exit";
            trayMenuExit.ForeColor = Color.FromArgb(236, 98, 95);
            trayMenuExit.Click += TrayMenuExit_Click;

            trayContextMenu.Items.AddRange(new ToolStripItem[] {
                trayMenuShow,
                separator,
                trayMenuStart,
                trayMenuStop,
                separator2,
                trayMenuExit
            });
            trayIcon.ContextMenuStrip = trayContextMenu;
        }

        #endregion

        #region Custom Classes

        private class CuztomMenuRenderer : ToolStripProfessionalRenderer
        {
            public CuztomMenuRenderer() : base(new CuztomColorTable()) { }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var rc = new Rectangle(Point.Empty, e.Item.Size);
                var c = e.Item.Selected ? Color.FromArgb(45, 51, 61) : Color.FromArgb(27, 40, 56);
                using (var brush = new SolidBrush(c))
                    e.Graphics.FillRectangle(brush, rc);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? e.Item.ForeColor : Color.FromArgb(100, 100, 100);
                base.OnRenderItemText(e);
            }
        }

        private class CuztomColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(45, 51, 61);
            public override Color MenuItemBorder => Color.FromArgb(102, 192, 244);
            public override Color MenuBorder => Color.FromArgb(32, 38, 48);
            public override Color ToolStripDropDownBackground => Color.FromArgb(27, 40, 56);
            public override Color ImageMarginGradientBegin => Color.FromArgb(27, 40, 56);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(27, 40, 56);
            public override Color ImageMarginGradientEnd => Color.FromArgb(27, 40, 56);
            public override Color SeparatorDark => Color.FromArgb(32, 38, 48);
            public override Color SeparatorLight => Color.FromArgb(45, 51, 61);
        }

        private class ButtonAnimationState
        {
            public bool IsHovering { get; set; }
            public DateTime AnimationStart { get; set; }
            public double HoverProgress { get; set; }
            public Color BaseColor { get; set; }
        }

        private class EnhancedButtonAnimationState
        {
            public bool IsHovering { get; set; }
            public bool IsPressed { get; set; }
            public bool IsActive { get; set; }
            public DateTime AnimationStart { get; set; }
            public float HoverProgress { get; set; }
            public float PulsePhase { get; set; }
            public float PulseIntensity { get; set; }
            public Color BaseColor { get; set; }
            public string IconText { get; set; } = "";
        }

        private class NavButtonState : ButtonAnimationState
        {
            public Color NeonColor { get; set; }
            public bool IsSelected { get; set; }
            public int Index { get; set; }
        }

        #endregion

        #region Controls Declaration

        private TableLayoutPanel mainLayoutPanel;
        private Panel sidebarPanel;
        private Panel contentPanel;
        private Panel headerPanel;
        private Panel logoPanel;
        private FlowLayoutPanel navButtonsPanel;
        private Button btnNavBots;
        private Button btnNavHub;
        private Button btnNavMcp;
        private Button btnNavTesting;
        private Button btnNavLogs;
        private Panel sidebarBottomPanel;
        private Button btnUpdate;
        private Label titleLabel;
        private FlowLayoutPanel controlButtonsPanel;
        private Button btnStart;
        private Button btnStop;
        private Button btnReboot;
        private Panel botsPanel;
        private Panel hubPanel;
        private McpControlPlanePanel mcpPanel;
        private ControlPlaneTestingPanel testingPanel;
        private Panel logsPanel;
        private Panel botHeaderPanel;
        private Panel addBotPanel;
        private TextBox TB_IP;
        private NumericUpDown NUD_Port;
        private ComboBox CB_Protocol;
        private ComboBox CB_Routine;
        private Button B_New;
        private FlowLayoutPanel FLP_Bots;
        private PropertyGrid PG_Hub;
        private ConfigurationSettingsTree configSettingsTree;
        private Panel pgContainer;
        private Panel configToolbarPanel;
        private Panel configThemeMark;
        private Label configToolbarTitle;
        private Label configToolbarSubtitle;
        private FlowLayoutPanel configTextSizeControlsPanel;
        private Label configTextSizeCaption;
        private Button btnConfigFontDecrease;
        private Label lblConfigFontScale;
        private Button btnConfigFontIncrease;
        private Button btnConfigFontReset;
        private SplitContainer configContentSplit;
        private Label configCategoryTitle;
        private Label configCategorySubtitle;
        private ListBox configCategoryList;
        private Panel configSectionHeaderPanel;
        private Label configSectionTitle;
        private Label configSectionDescription;
        private RichTextBox RTB_Logs;
        private Panel logsHeaderPanel;
        private Panel searchPanel;
        private TextBox logSearchBox;
        private FlowLayoutPanel searchOptionsPanel;
        private CheckBox btnCaseSensitive;
        private CheckBox btnRegex;
        private CheckBox btnWholeWord;
        private Button btnClearLogs;
        private Label searchStatusLabel;
        private Panel statusIndicator;
        // Animation timer removed
        private ComboBox comboBox1;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayContextMenu;
        private ToolStripMenuItem trayMenuShow;
        private ToolStripMenuItem trayMenuExit;

        private Button updater => btnUpdate;
        private Button B_Start => btnStart;
        private Button B_Stop => btnStop;
        private Button B_RebootStop => btnReboot;
        private TabControl TC_Main;
        private TabPage Tab_Bots;
        private TabPage Tab_Hub;
        private TabPage Tab_Mcp;
        private TabPage Tab_Testing;
        private TabPage Tab_Logs;
        private Panel ButtonPanel => controlButtonsPanel;

        #endregion
    }

    public static class GraphicsExtensions
    {
        public static void AddRoundedRectangle(this GraphicsPath path, Rectangle rect, int radius)
        {
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
        }
    }
}

#pragma warning restore CS8618
#pragma warning restore CS8625
#pragma warning restore CS8669
