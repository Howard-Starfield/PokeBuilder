using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace SysBot.Pokemon;

public sealed class ProgramConfigLoadResult
{
    private readonly List<string> _messages = [];

    public ProgramConfigLoadResult(ProgramConfig config) => Config = config;

    public ProgramConfig Config { get; }
    public bool CreatedNew { get; init; }
    public bool RecoveredFromBackup { get; set; }
    public bool Migrated { get; set; }
    public bool Normalized { get; set; }
    public int OriginalVersion { get; set; }
    public string? StartupBackupPath { get; set; }
    public string? RecoverySourcePath { get; set; }
    public string? SaveBackupPath { get; set; }
    public IReadOnlyList<string> Messages => _messages;

    internal void AddMessage(string message) => _messages.Add(message);
    internal void AddMessages(IEnumerable<string> messages) => _messages.AddRange(messages);
}

public static class ProgramConfigPersistence
{
    private const int MaxDurableBackups = 20;

    public static ProgramConfigLoadResult LoadOrCreate(string configPath)
    {
        configPath = Path.GetFullPath(configPath);
        if (!File.Exists(configPath))
        {
            var created = new ProgramConfigLoadResult(CreateNormalizedConfig()) { CreatedNew = true };
            created.AddMessage("No config file was found; created a new default configuration.");
            created.AddMessages(Validate(created.Config));
            return created;
        }

        var startupBackup = CreateDurableBackup(configPath, "startup");
        if (TryLoadFile(configPath, out var config, out var rawJson, out var loadError))
            return FinalizeLoadedConfig(configPath, config!, rawJson!, startupBackup, recoveredFromBackup: false, recoverySourcePath: null);

        return TryRecover(configPath, startupBackup, loadError);
    }

    public static bool SaveAtomic(ProgramConfig config, string configPath, out string? backupPath)
    {
        configPath = Path.GetFullPath(configPath);
        Normalize(config);
        SynchronizeBatchTradeCompatibility(config);
        config.ConfigVersion = ProgramConfig.CurrentConfigVersion;

        var json = JsonSerializer.Serialize(config, ProgramConfigContext.Default.ProgramConfig);
        if (File.Exists(configPath) && File.ReadAllText(configPath) == json)
        {
            backupPath = null;
            return false;
        }

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = configPath + ".tmp";
        backupPath = null;

        File.WriteAllText(tempPath, json);
        if (File.Exists(configPath))
        {
            backupPath = configPath + ".bak";
            File.Copy(configPath, backupPath, true);
        }

        File.Move(tempPath, configPath, true);
        return true;
    }

    public static string? CreateDurableBackup(string configPath, string reason)
    {
        configPath = Path.GetFullPath(configPath);
        if (!File.Exists(configPath))
            return null;

        try
        {
            var backupDir = GetBackupDirectory(configPath);
            Directory.CreateDirectory(backupDir);

            var safeReason = SanitizeFilePart(reason);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(backupDir, $"config.{stamp}.{safeReason}.json");
            var suffix = 1;
            while (File.Exists(backupPath))
                backupPath = Path.Combine(backupDir, $"config.{stamp}.{safeReason}.{suffix++}.json");

            File.Copy(configPath, backupPath, false);
            PruneDurableBackups(backupDir);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    public static bool Normalize(ProgramConfig config)
    {
        var changed = FillMissingValues(config, new ProgramConfig(), new HashSet<object>(ReferenceEqualityComparer.Instance));
        var fontScale = Math.Clamp(
            config.ConfigurationFontScalePercent,
            ProgramConfig.MinConfigurationFontScalePercent,
            ProgramConfig.MaxConfigurationFontScalePercent);
        if (config.ConfigurationFontScalePercent != fontScale)
        {
            config.ConfigurationFontScalePercent = fontScale;
            changed = true;
        }

        if (config.ConfigVersion != ProgramConfig.CurrentConfigVersion)
        {
            config.ConfigVersion = ProgramConfig.CurrentConfigVersion;
            changed = true;
        }

        return changed;
    }

    public static IReadOnlyList<string> Validate(ProgramConfig config)
    {
        var messages = new List<string>();
        var hub = config.Hub;

        if (config.Mode == ProgramMode.None)
            messages.Add("Config warning: Program mode is None; choose a game mode before starting bots.");

        if (config.Bots.Length == 0)
            messages.Add("Config warning: No console bots are configured yet.");
        else
        {
            for (var i = 0; i < config.Bots.Length; i++)
            {
                var bot = config.Bots[i];
                if (bot.Connection == null || !bot.IsValid())
                    messages.Add($"Config warning: Bot #{i + 1} has an invalid Switch connection setting.");
            }
        }

        var discord = hub.Discord;
        if (string.IsNullOrWhiteSpace(discord.Token) && HasDiscordConfiguration(discord))
            messages.Add("Config warning: Discord settings/roles are configured, but Discord.Token is empty; Discord commands, favored roles, and sudo roles will not start.");

        if (HasEntries(discord.RoleFavored) && hub.Favoritism.Mode == FavoredMode.None)
            messages.Add("Config warning: Discord.RoleFavored has entries, but Favoritism.Mode is None; favored users will not receive priority.");

        var twitch = hub.Twitch;
        if (!string.IsNullOrWhiteSpace(twitch.Token) && (string.IsNullOrWhiteSpace(twitch.Username) || string.IsNullOrWhiteSpace(twitch.Channel)))
            messages.Add("Config warning: Twitch token is set, but Twitch.Username or Twitch.Channel is empty; Twitch integration will not start.");

        var youtube = hub.YouTube;
        if (!string.IsNullOrWhiteSpace(youtube.ClientID) && (string.IsNullOrWhiteSpace(youtube.ClientSecret) || string.IsNullOrWhiteSpace(youtube.ChannelID)))
            messages.Add("Config warning: YouTube ClientID is set, but ClientSecret or ChannelID is empty; YouTube integration will not start.");

        var web = hub.WebTrade;
        if (web.EnableWebTrades && web.Mode == WebTradeMode.Supabase && (string.IsNullOrWhiteSpace(web.SupabaseUrl) || string.IsNullOrWhiteSpace(web.SupabaseServiceKey)))
            messages.Add("Config warning: WebTrade is in Supabase mode, but SupabaseUrl or SupabaseServiceKey is empty; web trades will not poll.");

        if (web.EnableWebTrades && web.Mode == WebTradeMode.Tunnel && string.IsNullOrWhiteSpace(web.SupabaseJwtSecret))
            messages.Add("Config warning: WebTrade is in Tunnel mode, but SupabaseJwtSecret is empty; authenticated tunnel requests will fail.");

        return messages;
    }

    private static ProgramConfigLoadResult TryRecover(string configPath, string? startupBackup, Exception? loadError)
    {
        var candidates = GetRecoveryCandidates(configPath).ToList();
        foreach (var candidate in candidates)
        {
            if (!TryLoadFile(candidate, out var config, out var rawJson, out _))
                continue;

            var recovered = FinalizeLoadedConfig(configPath, config!, rawJson!, startupBackup, recoveredFromBackup: true, recoverySourcePath: candidate);
            recovered.AddMessage($"Config warning: Primary config could not be loaded ({loadError?.Message ?? "unknown error"}); recovered from {candidate}.");
            return recovered;
        }

        var created = new ProgramConfigLoadResult(CreateNormalizedConfig())
        {
            CreatedNew = true,
            StartupBackupPath = startupBackup,
        };
        created.AddMessage($"Config warning: Primary config could not be loaded ({loadError?.Message ?? "unknown error"}) and no usable backup was found; created a new default configuration.");
        created.AddMessages(Validate(created.Config));
        return created;
    }

    private static ProgramConfigLoadResult FinalizeLoadedConfig(string configPath, ProgramConfig config, string rawJson, string? startupBackup, bool recoveredFromBackup, string? recoverySourcePath)
    {
        var originalVersion = TryReadConfigVersion(rawJson);
        var normalized = Normalize(config);
        var legacyMigrated = ApplyLegacyJsonMigrations(config, rawJson);
        normalized |= legacyMigrated;
        var migrated = originalVersion < ProgramConfig.CurrentConfigVersion;

        var result = new ProgramConfigLoadResult(config)
        {
            StartupBackupPath = startupBackup,
            RecoveredFromBackup = recoveredFromBackup,
            RecoverySourcePath = recoverySourcePath,
            OriginalVersion = originalVersion,
            Migrated = migrated,
            Normalized = normalized,
        };

        if (migrated)
            result.AddMessage($"Config migrated from version {originalVersion} to {ProgramConfig.CurrentConfigVersion}.");
        if (normalized)
            result.AddMessage("Config repaired missing or null settings with current defaults.");
        if (legacyMigrated)
            result.AddMessage("Config migrated legacy trade settings into current nested sections.");
        if (!string.IsNullOrWhiteSpace(startupBackup))
            result.AddMessage($"Config backup saved: {startupBackup}");

        if (migrated || normalized || legacyMigrated || recoveredFromBackup)
        {
            SaveAtomic(config, configPath, out var saveBackupPath);
            result.SaveBackupPath = saveBackupPath;
        }

        result.AddMessages(Validate(config));
        return result;
    }

    private static ProgramConfig CreateNormalizedConfig()
    {
        var config = new ProgramConfig();
        Normalize(config);
        return config;
    }

    private static bool TryLoadFile(string path, out ProgramConfig? config, out string? rawJson, out Exception? error)
    {
        config = null;
        rawJson = null;
        error = null;

        try
        {
            rawJson = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(rawJson) || rawJson.Contains('\0'))
                throw new JsonException("Config file is empty or contains null bytes.");

            config = JsonSerializer.Deserialize(rawJson, ProgramConfigContext.Default.ProgramConfig);
            if (config == null)
                throw new JsonException("Config file deserialized to null.");

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex;
            return false;
        }
    }

    private static bool ApplyLegacyJsonMigrations(ProgramConfig config, string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!TryGetObject(document.RootElement, "Hub", out var hub) ||
                !TryGetObject(hub, "Trade", out var trade))
                return false;

            var changed = false;
            var hasTradeConfiguration = TryGetObject(trade, nameof(TradeSettings.TradeConfiguration), out var tradeConfiguration);
            var hasTradeEmbedSettings = TryGetObject(trade, nameof(TradeSettings.TradeEmbedSettings), out var tradeEmbedSettings);
            var hasRequestFolderSettings = TryGetObject(trade, nameof(TradeSettings.RequestFolderSettings), out var requestFolderSettings);
            var hasCountStatsSettings = TryGetObject(trade, nameof(TradeSettings.CountStatsSettings), out var countStatsSettings);
            var hasLegality = TryGetObject(hub, nameof(PokeTradeHubConfig.Legality), out var legalitySection);

            var tradeConfig = config.Hub.Trade.TradeConfiguration;
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.MinTradeCode), v => tradeConfig.MinTradeCode = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.MaxTradeCode), v => tradeConfig.MaxTradeCode = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.StoreTradeCodes), v => tradeConfig.StoreTradeCodes = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.EnableHourlyTradeLimit), v => tradeConfig.EnableHourlyTradeLimit = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.FreeTradeLimitPerHour), v => tradeConfig.FreeTradeLimitPerHour = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.TradeLimitWindowMinutes), v => tradeConfig.TradeLimitWindowMinutes = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.TradeWaitTime), v => tradeConfig.TradeWaitTime = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.MaxTradeConfirmTime), v => tradeConfig.MaxTradeConfirmTime = v);
            changed |= ApplyLegacyValue<Species>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.ItemTradeSpecies), v => tradeConfig.ItemTradeSpecies = v);
            changed |= ApplyLegacyValue<TradeSettings.TradeSettingsCategory.HeldItem>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.DefaultHeldItem), v => tradeConfig.DefaultHeldItem = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.SuggestRelearnMoves), v => tradeConfig.SuggestRelearnMoves = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.AllowBatchTrades), v => tradeConfig.AllowBatchTrades = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.EnableSpamCheck), v => tradeConfig.EnableSpamCheck = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.MaxPkmsPerTrade), v => tradeConfig.MaxPkmsPerTrade = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.MaxDumpsPerTrade), v => tradeConfig.MaxDumpsPerTrade = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.MaxDumpTradeTime), v => tradeConfig.MaxDumpTradeTime = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.DumpTradeLegalityCheck), v => tradeConfig.DumpTradeLegalityCheck = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.DisallowTradeEvolve), v => tradeConfig.DisallowTradeEvolve = v);
            changed |= ApplyLegacyValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null, nameof(tradeConfig.TradeAnimationMaxDelaySeconds), v => tradeConfig.TradeAnimationMaxDelaySeconds = v);

            var legality = config.Hub.Legality;
            changed |= ApplyMovedValue<bool>(trade, hasTradeConfiguration ? tradeConfiguration : null,
                hasLegality ? legalitySection : null, nameof(legality.AllowBatchTrades), v => legality.AllowBatchTrades = v);
            changed |= ApplyMovedValue<int>(trade, hasTradeConfiguration ? tradeConfiguration : null,
                hasLegality ? legalitySection : null, nameof(legality.MaxPkmsPerTrade), v => legality.MaxPkmsPerTrade = v);

            var embed = config.Hub.Trade.TradeEmbedSettings;
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, "UseEmbeds", v => embed.UseEmbeds = v);
            changed |= ApplyLegacyValue<TradeSettings.ImageSize>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.PreferredImageSize), v => embed.PreferredImageSize = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.MoveTypeEmojis), v => embed.MoveTypeEmojis = v);
            changed |= ApplyLegacyValue<List<TradeSettings.MoveTypeEmojiInfo>>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.CustomTypeEmojis), v => embed.CustomTypeEmojis = v);
            changed |= ApplyLegacyValue<TradeSettings.EmojiInfo>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.MaleEmoji), v => embed.MaleEmoji = v);
            changed |= ApplyLegacyValue<TradeSettings.EmojiInfo>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.FemaleEmoji), v => embed.FemaleEmoji = v);
            changed |= ApplyLegacyValue<TradeSettings.EmojiInfo>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.MysteryGiftEmoji), v => embed.MysteryGiftEmoji = v);
            changed |= ApplyLegacyValue<TradeSettings.EmojiInfo>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.AlphaMarkEmoji), v => embed.AlphaMarkEmoji = v);
            changed |= ApplyLegacyValue<TradeSettings.EmojiInfo>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.MightiestMarkEmoji), v => embed.MightiestMarkEmoji = v);
            changed |= ApplyLegacyValue<TradeSettings.EmojiInfo>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.AlphaPLAEmoji), v => embed.AlphaPLAEmoji = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.UseTeraEmojis), v => embed.UseTeraEmojis = v);
            changed |= ApplyLegacyValue<List<TradeSettings.TeraTypeEmojiInfo>>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.TeraTypeEmojis), v => embed.TeraTypeEmojis = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowScale), v => embed.ShowScale = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowTeraType), v => embed.ShowTeraType = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowLevel), v => embed.ShowLevel = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowMetDate), v => embed.ShowMetDate = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowAbility), v => embed.ShowAbility = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowNature), v => embed.ShowNature = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowLanguage), v => embed.ShowLanguage = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowIVs), v => embed.ShowIVs = v);
            changed |= ApplyLegacyValue<bool>(trade, hasTradeEmbedSettings ? tradeEmbedSettings : null, nameof(embed.ShowEVs), v => embed.ShowEVs = v);

            var requestFolders = config.Hub.Trade.RequestFolderSettings;
            changed |= ApplyLegacyValue<string>(trade, hasRequestFolderSettings ? requestFolderSettings : null, nameof(requestFolders.EventsFolder), v => requestFolders.EventsFolder = v);
            changed |= ApplyLegacyValue<string>(trade, hasRequestFolderSettings ? requestFolderSettings : null, nameof(requestFolders.BattleReadyPKMFolder), v => requestFolders.BattleReadyPKMFolder = v);

            var counts = config.Hub.Trade.CountStatsSettings;
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedSurprise), v => counts.CompletedSurprise = v);
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedDistribution), v => counts.CompletedDistribution = v);
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedTrades), v => counts.CompletedTrades = v);
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedSeedChecks), v => counts.CompletedSeedChecks = v);
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedClones), v => counts.CompletedClones = v);
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedDumps), v => counts.CompletedDumps = v);
            changed |= ApplyLegacyValue<int>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.CompletedFixOTs), v => counts.CompletedFixOTs = v);
            changed |= ApplyLegacyValue<bool>(trade, hasCountStatsSettings ? countStatsSettings : null, nameof(counts.EmitCountsOnStatusCheck), v => counts.EmitCountsOnStatusCheck = v);

            return changed;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetObject(JsonElement source, string name, out JsonElement value)
    {
        if (source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(name, out value) &&
            value.ValueKind == JsonValueKind.Object)
            return true;

        value = default;
        return false;
    }

    private static bool ApplyLegacyValue<T>(JsonElement legacySection, JsonElement? currentSection, string propertyName, Action<T> apply)
    {
        if (HasProperty(currentSection, propertyName) || !legacySection.TryGetProperty(propertyName, out var valueElement))
            return false;

        if (!TryDeserializeLegacyValue(valueElement, out T? value) || value == null)
            return false;

        apply(value);
        return true;
    }

    private static bool ApplyMovedValue<T>(JsonElement legacySection, JsonElement? nestedLegacySection,
        JsonElement? currentSection, string propertyName, Action<T> apply)
    {
        if (HasProperty(currentSection, propertyName))
            return false;

        JsonElement valueElement;
        if (nestedLegacySection is { ValueKind: JsonValueKind.Object } nested && nested.TryGetProperty(propertyName, out var nestedValue))
            valueElement = nestedValue;
        else if (!legacySection.TryGetProperty(propertyName, out valueElement))
            return false;

        if (!TryDeserializeLegacyValue(valueElement, out T? value) || value == null)
            return false;

        apply(value);
        return true;
    }

    private static void SynchronizeBatchTradeCompatibility(ProgramConfig config)
    {
        var legality = config.Hub.Legality;
        var compatibility = config.Hub.Trade.TradeConfiguration;
        compatibility.AllowBatchTrades = legality.AllowBatchTrades;
        compatibility.MaxPkmsPerTrade = legality.MaxPkmsPerTrade;
    }

    private static bool HasProperty(JsonElement? section, string propertyName) =>
        section is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(propertyName, out _);

    private static bool TryDeserializeLegacyValue<T>(JsonElement element, out T? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(element.GetRawText());
            return true;
        }
        catch (JsonException) when (typeof(T).IsEnum && element.ValueKind == JsonValueKind.String)
        {
            if (Enum.TryParse(typeof(T), element.GetString(), ignoreCase: true, out var parsed))
            {
                value = (T)parsed;
                return true;
            }
        }

        value = default;
        return false;
    }
    private static int TryReadConfigVersion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(nameof(ProgramConfig.ConfigVersion), out var version) && version.TryGetInt32(out var value)
                ? value
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> GetRecoveryCandidates(string configPath)
    {
        var bak = configPath + ".bak";
        if (File.Exists(bak))
            yield return bak;

        var backupDir = GetBackupDirectory(configPath);
        if (!Directory.Exists(backupDir))
            yield break;

        foreach (var file in Directory.GetFiles(backupDir, "config.*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            yield return file;
    }

    private static string GetBackupDirectory(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath) ?? Environment.CurrentDirectory;
        return Path.Combine(directory, "config.backups");
    }

    private static void PruneDurableBackups(string backupDir)
    {
        var files = Directory.GetFiles(backupDir, "config.*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(MaxDurableBackups);

        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch { }
        }
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "backup" : sanitized;
    }

    private static bool FillMissingValues(object target, object defaults, HashSet<object> visited)
    {
        if (!visited.Add(target))
            return false;

        var changed = false;
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
                continue;

            object? value;
            object? defaultValue;
            try
            {
                value = property.GetValue(target);
                defaultValue = property.GetValue(defaults);
            }
            catch
            {
                continue;
            }

            if (value == null)
            {
                if (defaultValue == null)
                    continue;

                try
                {
                    property.SetValue(target, defaultValue);
                    changed = true;
                }
                catch
                {
                    // Init-only or guarded properties can refuse reflection writes; leave them untouched.
                }
                continue;
            }

            if (!ShouldRecurseInto(value.GetType()))
                continue;

            if (defaultValue == null)
            {
                try { defaultValue = Activator.CreateInstance(value.GetType()); }
                catch { continue; }
            }

            if (defaultValue != null)
                changed |= FillMissingValues(value, defaultValue, visited);
        }

        return changed;
    }

    private static bool ShouldRecurseInto(Type type)
    {
        if (type == typeof(string) || type.IsValueType || type.IsEnum || type.IsArray)
            return false;
        if (typeof(Delegate).IsAssignableFrom(type))
            return false;
        if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(RemoteControlAccessList))
            return false;

        return type.Namespace?.StartsWith("SysBot", StringComparison.Ordinal) == true;
    }

    private static bool HasDiscordConfiguration(DiscordSettings discord) =>
        HasEntries(discord.AnnouncementChannels) ||
        HasEntries(discord.AbuseLogChannels) ||
        HasEntries(discord.ChannelWhitelist) ||
        HasEntries(discord.GlobalSudoList) ||
        HasEntries(discord.LoggingChannels) ||
        HasEntries(discord.RoleCanClone) ||
        HasEntries(discord.RoleCanDump) ||
        HasEntries(discord.RoleCanFixOT) ||
        HasEntries(discord.RoleCanSeedCheckorSpecialRequest) ||
        HasEntries(discord.RoleCanTrade) ||
        HasEntries(discord.RoleFavored) ||
        HasEntries(discord.RoleRemoteControl) ||
        HasEntries(discord.RoleSudo) ||
        HasEntries(discord.ServerBlacklist) ||
        HasEntries(discord.TradeStartingChannels) ||
        HasEntries(discord.UserBlacklist);

    private static bool HasEntries(RemoteControlAccessList? list) => list?.List is { Count: > 0 };
}
