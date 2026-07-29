using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace SysBot.Tests;

public sealed class WebsiteControlPlaneConvergenceTests
{
    [Fact]
    public void WebsiteAdapters_RouteThroughSharedDurableOrchestrator()
    {
        var root = FindRepositoryRoot();
        var handler = File.ReadAllText(Path.Combine(
            root,
            "SysBot.Pokemon.WinForms",
            "WebApi",
            "TradeApiHandler.cs"));
        var poller = File.ReadAllText(Path.Combine(
            root,
            "SysBot.Pokemon.WinForms",
            "WebApi",
            "SupabasePoller.cs"));
        var bridge = File.ReadAllText(Path.Combine(
            root,
            "SysBot.Pokemon.WinForms",
            "WebApi",
            "ControlPlaneHttpTradeBridge.cs"));
        var startup = File.ReadAllText(Path.Combine(
            root,
            "SysBot.Pokemon.WinForms",
            "McpControlPlaneService.cs"));

        Count(handler, "ControlPlaneHttpTradeBridge.SubmitAsync(")
            .Should().Be(3,
                "single, one-valid-item batch, and multi-item batch routes must converge");
        poller.Should().Contain(
            "ControlPlaneHttpTradeBridge.SubmitQueueAsync(",
            "Supabase website mode must share the same durable dispatcher");
        bridge.Should().Contain("orchestrator.CreateTradePlan(command)");
        bridge.Should().Contain("orchestrator.EnqueueTradePlanWithQueueHints(");
        bridge.Should().Contain("Evolution = TradeEvolutionPolicy.Block");

        startup.IndexOf(
            "_orchestrator = orchestrator;",
            StringComparison.Ordinal).Should().BeLessThan(
                startup.IndexOf(
                    "TokenEnvironmentVariable",
                    StringComparison.Ordinal),
                "website orchestration must remain active when MCP transport is disabled");
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(
            value,
            index,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

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
