using System;
using System.Globalization;
using System.Net;

namespace SysBot.Pokemon.Helpers;

public enum PokeBotReleaseCheckStatus
{
    Success,
    NoPublishedRelease,
    NetworkError,
    InvalidResponse,
    ApiError,
}

public static class PokeBotReleaseCheck
{
    public static bool IsNewerVersion(string? candidateVersion, string? currentVersion)
    {
        return TryParseVersion(candidateVersion, out var candidate) &&
               TryParseVersion(currentVersion, out var current) &&
               candidate > current;
    }

    public static bool ShouldInstallUpdate(bool updateAvailable, bool forceRequested)
    {
        _ = forceRequested;
        return updateAvailable;
    }

    public static PokeBotReleaseCheckStatus ClassifyHttpStatus(HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.NotFound)
            return PokeBotReleaseCheckStatus.NoPublishedRelease;

        return (int)statusCode is >= 200 and <= 299
            ? PokeBotReleaseCheckStatus.Success
            : PokeBotReleaseCheckStatus.ApiError;
    }

    public static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    public static string GetFailureMessage(PokeBotReleaseCheckStatus status, HttpStatusCode? statusCode = null) =>
        status switch
        {
            PokeBotReleaseCheckStatus.NoPublishedRelease =>
                "PokeBuilder is online, but this repository does not have a published GitHub release yet.",
            PokeBotReleaseCheckStatus.NetworkError =>
                "Could not reach GitHub. Check your internet connection and firewall, then try again.",
            PokeBotReleaseCheckStatus.InvalidResponse =>
                "GitHub returned release information that PokeBuilder could not read.",
            PokeBotReleaseCheckStatus.ApiError when statusCode.HasValue =>
                $"GitHub could not provide release information (HTTP {(int)statusCode.Value}). Try again later.",
            _ => "PokeBuilder could not retrieve the latest release information.",
        };

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        int suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            normalized = normalized[..suffixIndex];

        var components = normalized.Split('.');
        if (components.Length is < 1 or > 4)
            return false;

        var numbers = new int[4];
        for (int i = 0; i < components.Length; i++)
        {
            if (!int.TryParse(
                    components[i],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[i]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}
