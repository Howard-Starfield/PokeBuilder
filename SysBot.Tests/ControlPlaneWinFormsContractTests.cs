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
    public void LeftNavigation_WiresMcpAndTestingButtonsToRealPanels()
    {
        var main = ReadSource("SysBot.Pokemon.WinForms", "Main.cs");
        var designer = ReadSource(
            "SysBot.Pokemon.WinForms",
            "Main.Designer.cs");

        main.Should().Contain("NavigationMcpIndex = 2");
        main.Should().Contain("NavigationTestingIndex = 3");
        main.Should().Contain("NavigationLogsIndex = 4");
        designer.Should().Contain("navButtonsPanel.Controls.Add(btnNavMcp);");
        designer.Should().Contain("navButtonsPanel.Controls.Add(btnNavTesting);");
        designer.Should().Contain("case NavigationMcpIndex:");
        designer.Should().Contain("mcpPanel.Visible = true;");
        designer.Should().Contain("case NavigationTestingIndex:");
        designer.Should().Contain("testingPanel.Visible = true;");
        designer.Should().Contain(
            "private McpControlPlanePanel mcpPanel;");
        designer.Should().Contain(
            "private ControlPlaneTestingPanel testingPanel;");
    }

    [Fact]
    public void TestingPanel_UsesOnlyReadOnlyControlPlaneChecks()
    {
        var source = ReadSource(
            "SysBot.Pokemon.WinForms",
            "ControlPlaneOperatorPanels.cs");

        source.Should().Contain("GetAsync(status.HealthEndpoint)");
        source.Should().Contain("api.ListBotInstances(");
        source.Should().Contain("These tests do not enqueue or trade");
        source.Should().NotContain("CreateTradePlan(");
        source.Should().NotContain("EnqueueTradePlan(");
        source.Should().NotContain("CancelTradeOperation(");
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
