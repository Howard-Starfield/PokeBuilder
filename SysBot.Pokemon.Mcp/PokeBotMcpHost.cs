using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Mcp;

public sealed record PokeBotMcpHostOptions
{
    public int Port { get; init; } = PokeBotMcpHost.DefaultPort;

    public long MaxRequestBodyBytes { get; init; } = 1_048_576;

    public int RequestsPerMinute { get; init; } = 120;

    public int MutationsPerMinute { get; init; } = 30;
}

public sealed class PokeBotMcpHost : IAsyncDisposable
{
    public const string TokenEnvironmentVariable = "POKEBOT_MCP_TOKEN";
    public const string PortEnvironmentVariable = "POKEBOT_MCP_PORT";
    public const int DefaultPort = 8090;
    private WebApplication? _application;

    public bool IsRunning => _application is not null;

    public async Task StartAsync(
        ITradeControlApi api,
        PokeBotMcpHostOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (_application is not null)
            throw new InvalidOperationException("The PokeBot MCP host is already running.");

        options ??= new();
        ValidateOptions(options);
        var token = ReadAndValidateToken();
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var ownerId = CreateOwnerId(tokenHash);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost";
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Limits.MaxRequestBodySize = options.MaxRequestBodyBytes;
            server.Listen(IPAddress.Loopback, options.Port);
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(api);
        builder.Services.AddSingleton(new McpRequestRateLimiter(
            options.RequestsPerMinute,
            options.MutationsPerMinute));
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithToolsFromAssembly();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";

            if (context.Request.Path == "/health")
            {
                await next(context);
                return;
            }

            if (!context.Request.Path.StartsWithSegments("/mcp"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!IsAllowedOrigin(context.Request.Headers.Origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!TryReadBearerToken(context.Request, out var presented) ||
                !FixedTimeTokenEquals(tokenHash, presented))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }

            var limiter = context.RequestServices
                .GetRequiredService<McpRequestRateLimiter>();
            if (!limiter.TryAcquireRequest(ownerId, DateTimeOffset.UtcNow))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "60";
                return;
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, ownerId),
                    new Claim(ClaimTypes.Name, "PokeBot MCP client"),
                ],
                authenticationType: "PokeBotMcpBearer"));
            await next(context);
        });

        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            service = "pokebot-mcp",
        }));
        app.MapMcp("/mcp");

        try
        {
            await app.StartAsync(cancellationToken);
            _application = app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var app = Interlocked.Exchange(ref _application, null);
        if (app is null)
            return;

        await app.StopAsync(cancellationToken);
        await app.DisposeAsync();
    }

    public ValueTask DisposeAsync() =>
        _application is null
            ? ValueTask.CompletedTask
            : new ValueTask(StopAsync());

    internal static string ReadAndValidateToken()
    {
        var token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
        {
            throw new InvalidOperationException(
                $"{TokenEnvironmentVariable} must contain at least 32 non-whitespace characters.");
        }

        if (token.Any(char.IsWhiteSpace) ||
            token.Equals("change-me-change-me-change-me-change-me", StringComparison.OrdinalIgnoreCase) ||
            token.Distinct().Count() < 8)
        {
            throw new InvalidOperationException(
                $"{TokenEnvironmentVariable} must be a non-default high-entropy token without whitespace.");
        }

        return token;
    }

    public static int ReadConfiguredPort()
    {
        var configured = Environment.GetEnvironmentVariable(
            PortEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultPort;
        if (!int.TryParse(configured, out var port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"{PortEnvironmentVariable} must be an integer between 1 and 65535.");
        }
        return port;
    }

    internal static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme is "http" or "https" &&
            (uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateOptions(PokeBotMcpHostOptions options)
    {
        if (options.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "Port must be between 1 and 65535.");
        if (options.MaxRequestBodyBytes is < 4096 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(options), "Request body limit is outside the supported range.");
        if (options.RequestsPerMinute is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Request rate limit is outside the supported range.");
        if (options.MutationsPerMinute is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Mutation rate limit is outside the supported range.");
    }

    private static bool TryReadBearerToken(
        HttpRequest request,
        out string token)
    {
        token = string.Empty;
        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        token = authorization[prefix.Length..];
        return token.Length > 0;
    }

    private static bool FixedTimeTokenEquals(
        byte[] expectedHash,
        string presented)
    {
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(expectedHash, presentedHash);
    }

    private static string CreateOwnerId(byte[] tokenHash) =>
        $"mcp:{Convert.ToHexString(tokenHash.AsSpan(0, 12)).ToLowerInvariant()}";
}

public sealed class McpRequestRateLimiter
{
    private readonly int _requestLimit;
    private readonly int _mutationLimit;
    private readonly object _sync = new();
    private readonly Dictionary<string, Counter> _requests = [];
    private readonly Dictionary<string, Counter> _mutations = [];

    public McpRequestRateLimiter(int requestLimit, int mutationLimit)
    {
        _requestLimit = requestLimit;
        _mutationLimit = mutationLimit;
    }

    public bool TryAcquireRequest(string ownerId, DateTimeOffset now) =>
        TryAcquire(_requests, ownerId, _requestLimit, now);

    public bool TryAcquireMutation(string ownerId, DateTimeOffset now) =>
        TryAcquire(_mutations, ownerId, _mutationLimit, now);

    private bool TryAcquire(
        Dictionary<string, Counter> counters,
        string key,
        int limit,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            if (!counters.TryGetValue(key, out var counter) ||
                now - counter.WindowStart >= TimeSpan.FromMinutes(1))
            {
                counters[key] = new(now, 1);
                return true;
            }

            if (counter.Count >= limit)
                return false;
            counters[key] = counter with { Count = counter.Count + 1 };
            return true;
        }
    }

    private sealed record Counter(DateTimeOffset WindowStart, int Count);
}
