namespace SysBot.Pokemon.Helpers
{
    public static class PokeBot
    {
        public const string RepositoryOwner = "Howard-Starfield";
        public const string RepositoryName = "PokeBuilder";
        public const string RepositoryUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}";
        public const string ReleasesUrl = $"{RepositoryUrl}/releases";
        public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
        public const string Attribution = RepositoryUrl;

        public const string ConfigPath = "config.json";

        public const string Version = "v1.3.8";
    }
}
