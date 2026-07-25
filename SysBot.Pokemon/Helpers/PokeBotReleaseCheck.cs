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
}
