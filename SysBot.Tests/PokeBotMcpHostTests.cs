using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SysBot.Pokemon;
using SysBot.Pokemon.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SysBot.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class McpEnvironmentCollection
{
    public const string Name = "MCP environment";
}

[Collection(McpEnvironmentCollection.Name)]
public sealed class PokeBotMcpHostTests
{
    private const string TestToken =
        "5R2tV8mP4xK9qW7nC3jH6sD1fG8yL2bZ";

    private static readonly string[] ExpectedTools =
    [
        "cancel_trade_operation",
        "create_trade_plan",
        "enqueue_trade_plan",
        "get_trade_operation",
        "get_trade_plan",
        "list_bot_instances",
        "list_trade_events",
        "pause_trade_operation",
        "resolve_trade_attention",
        "resume_trade_operation",
        "validate_trade_plan",
    ];

    [Fact]
    public async Task Host_IsLoopbackAuthenticated_AndPublishesExactV1Tools()
    {
        var previous = Environment.GetEnvironmentVariable(
            PokeBotMcpHost.TokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            PokeBotMcpHost.TokenEnvironmentVariable,
            TestToken);
        var port = ReserveTcpPort();
        var api = new FakeTradeControlApi();
        await using var host = new PokeBotMcpHost();

        try
        {
            await host.StartAsync(api, new() { Port = port });
            using var http = new HttpClient();
            var health = await http.GetAsync($"http://127.0.0.1:{port}/health");
            health.StatusCode.Should().Be(HttpStatusCode.OK);

            using var hostileHostRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{port}/health");
            hostileHostRequest.Headers.Host = "attacker.example";
            var hostileHost = await http.SendAsync(hostileHostRequest);
            hostileHost.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var unauthorizedBody = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json");
            var unauthorized = await http.PostAsync(
                $"http://127.0.0.1:{port}/mcp",
                unauthorizedBody);
            unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var hostileRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{port}/mcp")
            {
                Content = new StringContent(
                    "{}",
                    Encoding.UTF8,
                    "application/json"),
            };
            hostileRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", TestToken);
            hostileRequest.Headers.Add("Origin", "https://attacker.example");
            var hostile = await http.SendAsync(hostileRequest);
            hostile.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string>
                    {
                        ["Authorization"] = $"Bearer {TestToken}",
                    },
                });
            await using var client = await McpClient.CreateAsync(transport);
            var tools = await client.ListToolsAsync();
            tools.Select(z => z.Name).Order().Should().Equal(ExpectedTools);

            await client.CallToolAsync(
                "list_bot_instances",
                new Dictionary<string, object?>
                {
                    ["include_offline"] = true,
                });
            api.LastOwnerId.Should().StartWith("mcp:");
            api.LastOwnerId.Should().NotContain(TestToken);
        }
        finally
        {
            await host.StopAsync();
            Environment.SetEnvironmentVariable(
                PokeBotMcpHost.TokenEnvironmentVariable,
                previous);
        }
    }

    [Fact]
    public void TokenValidation_RejectsMissingShortDefaultAndLowEntropyValues()
    {
        var previous = Environment.GetEnvironmentVariable(
            PokeBotMcpHost.TokenEnvironmentVariable);
        try
        {
            foreach (var invalid in new[]
            {
                null,
                "short",
                "change-me-change-me-change-me-change-me",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "has whitespace and is definitely longer than thirty two",
            })
            {
                Environment.SetEnvironmentVariable(
                    PokeBotMcpHost.TokenEnvironmentVariable,
                    invalid);
                var action = PokeBotMcpHost.ReadAndValidateToken;
                action.Should().Throw<InvalidOperationException>();
            }

            Environment.SetEnvironmentVariable(
                PokeBotMcpHost.TokenEnvironmentVariable,
                TestToken);
            PokeBotMcpHost.ReadAndValidateToken().Should().Be(TestToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                PokeBotMcpHost.TokenEnvironmentVariable,
                previous);
        }
    }

    [Fact]
    public void PortConfiguration_UsesDedicatedDefault_AndRejectsInvalidValues()
    {
        var previous = Environment.GetEnvironmentVariable(
            PokeBotMcpHost.PortEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                PokeBotMcpHost.PortEnvironmentVariable,
                null);
            PokeBotMcpHost.ReadConfiguredPort()
                .Should().Be(PokeBotMcpHost.DefaultPort);

            Environment.SetEnvironmentVariable(
                PokeBotMcpHost.PortEnvironmentVariable,
                "19090");
            PokeBotMcpHost.ReadConfiguredPort().Should().Be(19090);

            foreach (var invalid in new[] { "0", "65536", "not-a-port" })
            {
                Environment.SetEnvironmentVariable(
                    PokeBotMcpHost.PortEnvironmentVariable,
                    invalid);
                Func<int> action = PokeBotMcpHost.ReadConfiguredPort;
                action.Should().Throw<InvalidOperationException>();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                PokeBotMcpHost.PortEnvironmentVariable,
                previous);
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://127.0.0.1:3000", true)]
    [InlineData("https://localhost", true)]
    [InlineData("https://attacker.example", false)]
    [InlineData("not a uri", false)]
    public void OriginValidation_IsFailClosed(
        string? origin,
        bool expected) =>
        PokeBotMcpHost.IsAllowedOrigin(origin).Should().Be(expected);

    [Fact]
    public void RateLimiter_UsesIndependentRequestAndMutationBudgets()
    {
        var limiter = new McpRequestRateLimiter(2, 1);
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");

        limiter.TryAcquireRequest("owner", now).Should().BeTrue();
        limiter.TryAcquireRequest("owner", now).Should().BeTrue();
        limiter.TryAcquireRequest("owner", now).Should().BeFalse();
        limiter.TryAcquireMutation("owner", now).Should().BeTrue();
        limiter.TryAcquireMutation("owner", now).Should().BeFalse();
        limiter.TryAcquireRequest("other", now).Should().BeTrue();
        limiter.TryAcquireRequest("owner", now.AddMinutes(1)).Should().BeTrue();
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FakeTradeControlApi : ITradeControlApi
    {
        public string? LastOwnerId { get; private set; }

        public TradeControlResponse<TradeRuntimeSnapshot> ListBotInstances(
            string ownerId,
            bool includeOffline)
        {
            LastOwnerId = ownerId;
            return TradeControlResponse<TradeRuntimeSnapshot>.Ok(new(
                ProgramMode.SV,
                true,
                true,
                true,
                0,
                "runtime-1",
                []));
        }

        public TradeControlResponse<TradePlanStructuralValidation> ValidateTradePlan(
            CreateTradePlanCommand command) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradePlanSnapshot> CreateTradePlan(
            CreateTradePlanCommand command) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradePlanSnapshot> GetTradePlan(
            string ownerId,
            string planId) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradeOperationSnapshot> EnqueueTradePlan(
            string ownerId,
            string planId,
            string idempotencyKey) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradeOperationSnapshot> GetTradeOperation(
            string ownerId,
            string operationId) =>
            throw new NotSupportedException();

        public TradeControlResponse<IReadOnlyList<TradeEventSnapshot>> ListTradeEvents(
            string ownerId,
            string operationId,
            long afterSequence,
            int limit) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradeOperationSnapshot> PauseTradeOperation(
            string ownerId,
            string operationId,
            string idempotencyKey,
            string reason) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradeOperationSnapshot> ResumeTradeOperation(
            string ownerId,
            string operationId,
            string idempotencyKey) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradeOperationSnapshot> CancelTradeOperation(
            string ownerId,
            string operationId,
            string idempotencyKey,
            bool confirm,
            string reason) =>
            throw new NotSupportedException();

        public TradeControlResponse<TradeOperationSnapshot> ResolveTradeAttention(
            string ownerId,
            string operationId,
            string idempotencyKey,
            string itemId,
            TradeAttentionResolution resolution,
            bool confirm,
            string reason) =>
            throw new NotSupportedException();
    }
}
