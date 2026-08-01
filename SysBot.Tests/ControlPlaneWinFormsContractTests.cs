using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace SysBot.Tests;

public sealed class ControlPlaneWinFormsContractTests
{
    [Fact]
    public void ConfigurationChoiceMenu_DefersDisposalUntilClosedEventUnwinds()
    {
        var source = ReadSource(
            "SysBot.Pokemon.WinForms",
            "ConfigurationSettingsTree.cs");

        source.Should().Contain("private ContextMenuStrip? _activeMenu;");
        source.Should().Contain("private bool _disposing;");
        source.Should().Contain("BeginInvoke((Action)(() =>");
        source.Should().Contain("if (!menu.IsDisposed)");
        source.Should().NotContain(
            "menu.Closed += (_, _) => menu.Dispose();",
            "disposing a ToolStrip dropdown inside its Closed callback can re-enter native teardown");
    }

    [Fact]
    public void LeftNavigation_WiresMcpAutomationAndTestingButtonsToRealPanels()
    {
        var main = ReadSource("SysBot.Pokemon.WinForms", "Main.cs");
        var designer = ReadSource(
            "SysBot.Pokemon.WinForms",
            "Main.Designer.cs");

        main.Should().Contain("NavigationMcpIndex = 2");
        main.Should().Contain("NavigationAutomationIndex = 3");
        main.Should().Contain("NavigationTestingIndex = 4");
        main.Should().Contain("NavigationLogsIndex = 5");
        designer.Should().Contain("navButtonsPanel.Controls.Add(btnNavMcp);");
        designer.Should().Contain("navButtonsPanel.Controls.Add(btnNavAutomation);");
        designer.Should().Contain("navButtonsPanel.Controls.Add(btnNavTesting);");
        designer.Should().Contain("case NavigationMcpIndex:");
        designer.Should().Contain("mcpPanel.Visible = true;");
        designer.Should().Contain("case NavigationAutomationIndex:");
        designer.Should().Contain("automationPanel.Visible = true;");
        designer.Should().Contain("case NavigationTestingIndex:");
        designer.Should().Contain("testingPanel.Visible = true;");
        designer.Should().Contain(
            "private McpControlPlanePanel mcpPanel;");
        designer.Should().Contain(
            "private AutomationControlPanel automationPanel;");
        designer.Should().Contain(
            "private ControlPlaneTestingPanel testingPanel;");
    }

    [Fact]
    public void TestingPanel_CoversDiagnosticsAndConfirmedRestartActions()
    {
        var source = ReadSource(
            "SysBot.Pokemon.WinForms",
            "ControlPlaneOperatorPanels.cs");

        source.Should().Contain("GetAsync(status.HealthEndpoint)");
        source.Should().Contain("api.ListBotInstances(");
        source.Should().Contain("TEST WINDOWS STARTUP");
        source.Should().Contain("TEST BOT AUTO-START");
        source.Should().Contain("TEST RESTART SCHEDULE");
        source.Should().Contain("TEST POKEBOT RESTART");
        source.Should().Contain("TEST GAME RESTART");
        source.Should().Contain("main.TestRestartNowAsync()");
        source.Should().Contain("main.TestConnectedGameRestartAsync()");
        source.Should().Contain("always require confirmation");
        source.Should().NotContain("CreateTradePlan(");
        source.Should().NotContain("EnqueueTradePlan(");
        source.Should().NotContain("CancelTradeOperation(");
    }

    [Fact]
    public void AutomationAndTestingPanels_UseDockedLayouts()
    {
        var source = ReadSource(
            "SysBot.Pokemon.WinForms",
            "ControlPlaneOperatorPanels.cs");

        source.Should().Contain("var layout = new TableLayoutPanel", Exactly.Twice());
        source.Should().Contain("_settings = new ConfigurationSettingsTree");
        source.Should().Contain("Dock = DockStyle.Fill");
        source.Should().NotContain("Location = new Point(18, 92)");
        source.Should().NotContain("Location = new Point(18, 98)");
        source.Should().NotContain("Size = new Size(800, 418)");
        source.Should().NotContain("Size = new Size(800, 106)");
    }

    [Fact]
    public void ConfigurationDropDowns_AreClampedToTheWorkingArea()
    {
        var placement = ReadSource(
            "SysBot.Pokemon.WinForms",
            "DropDownPlacement.cs");
        var settings = ReadSource(
            "SysBot.Pokemon.WinForms",
            "ConfigurationSettingsTree.cs");
        var collections = ReadSource(
            "SysBot.Pokemon.WinForms",
            "ConfigurationCollectionEditor.cs");

        placement.Should().Contain("Screen.FromControl(owner).WorkingArea");
        placement.Should().Contain("workingArea.Right - width");
        placement.Should().Contain("workingArea.Bottom");
        settings.Should().Contain("DropDownPlacement.ShowBelow(this, menu, menuWidth)");
        collections.Should().Contain("DropDownPlacement.ShowBelow(owner, _dropDown, control.Width)");
    }

    private static string ReadSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            Path.Combine(relativePath)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SysBot.NET.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the PokeBot repository root.");
    }
}
