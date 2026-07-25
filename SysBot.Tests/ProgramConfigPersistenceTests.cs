using FluentAssertions;
using SysBot.Pokemon;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SysBot.Tests;

public class ProgramConfigPersistenceTests
{
    [Fact]
    public void LoadOrCreate_MigratesOldConfigAndPreservesOperatorSettings()
    {
        var dir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            var oldConfig = new ProgramConfig
            {
                ConfigVersion = 0,
                Mode = ProgramMode.SV,
            };
            oldConfig.Hub.Legality.UseTradePartnerInfo = false;
            oldConfig.Hub.Discord.Token = "discord-token";
            oldConfig.Hub.Discord.RoleFavored.AllowIfEmpty = false;
            oldConfig.Hub.Discord.RoleFavored.List.Add(new RemoteControlAccess { ID = 123456789, Name = "VIP", Comment = "keep favored role" });
            oldConfig.Hub.Discord.RoleSudo.AllowIfEmpty = false;
            oldConfig.Hub.Discord.RoleSudo.List.Add(new RemoteControlAccess { ID = 987654321, Name = "Admin", Comment = "keep sudo role" });
            oldConfig.Hub.Favoritism.Mode = FavoredMode.Multiply;
            oldConfig.Hub.Favoritism.Multiply = 0.75f;
            File.WriteAllText(configPath, JsonSerializer.Serialize(oldConfig, ProgramConfigContext.Default.ProgramConfig));

            var result = ProgramConfigPersistence.LoadOrCreate(configPath);

            result.Migrated.Should().BeTrue();
            result.OriginalVersion.Should().Be(0);
            result.Config.ConfigVersion.Should().Be(ProgramConfig.CurrentConfigVersion);
            result.Config.Hub.Legality.UseTradePartnerInfo.Should().BeFalse();
            result.Config.Hub.Discord.Token.Should().Be("discord-token");
            result.Config.Hub.Discord.RoleFavored.List.Should().ContainSingle(x => x.ID == 123456789 && x.Name == "VIP");
            result.Config.Hub.Discord.RoleSudo.List.Should().ContainSingle(x => x.ID == 987654321 && x.Name == "Admin");
            result.Config.Hub.Favoritism.Mode.Should().Be(FavoredMode.Multiply);
            result.Config.Hub.Favoritism.Multiply.Should().Be(0.75f);
            Directory.GetFiles(Path.Combine(dir, "config.backups"), "config.*.startup.json").Should().NotBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_RepairsNullNestedSettingsWithoutDiscardingConfig()
    {
        var dir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, "{\"ConfigVersion\":1,\"Hub\":{\"Legality\":null,\"Discord\":null,\"Favoritism\":null},\"Mode\":4,\"Bots\":[]}");

            var result = ProgramConfigPersistence.LoadOrCreate(configPath);

            result.Normalized.Should().BeTrue();
            result.Config.Hub.Legality.Should().NotBeNull();
            result.Config.Hub.Legality.UseTradePartnerInfo.Should().BeTrue();
            result.Config.Hub.Discord.Should().NotBeNull();
            result.Config.Hub.Discord.RoleCanTrade.AllowIfEmpty.Should().BeTrue();
            result.Config.Hub.Discord.RoleFavored.AllowIfEmpty.Should().BeFalse();
            result.Config.Hub.Favoritism.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_MigratesLegacyFlatTradeSettingsIntoNestedSections()
    {
        var dir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, "{\"ConfigVersion\":0,\"Mode\":4,\"Bots\":[],\"Hub\":{\"Trade\":{\"TradeConfiguration\":{\"TradeWaitTime\":88},\"TradeWaitTime\":77,\"MaxPkmsPerTrade\":3,\"AllowBatchTrades\":false,\"TradeAnimationMaxDelaySeconds\":61,\"UseEmbeds\":false,\"PreferredImageSize\":\"Size128x128\",\"ShowIVs\":false,\"EventsFolder\":\"old-events\",\"BattleReadyPKMFolder\":\"old-battle-ready\",\"CompletedTrades\":42,\"EmitCountsOnStatusCheck\":true}}}");

            var result = ProgramConfigPersistence.LoadOrCreate(configPath);

            result.Config.Hub.Trade.TradeConfiguration.TradeWaitTime.Should().Be(88);
            result.Config.Hub.Trade.TradeConfiguration.MaxPkmsPerTrade.Should().Be(3);
            result.Config.Hub.Trade.TradeConfiguration.AllowBatchTrades.Should().BeFalse();
            GetLegalitySetting<int>(result.Config, "MaxPkmsPerTrade").Should().Be(3);
            GetLegalitySetting<bool>(result.Config, "AllowBatchTrades").Should().BeFalse("the new Legality value must win during load");
            result.Config.Hub.Trade.TradeConfiguration.TradeAnimationMaxDelaySeconds.Should().Be(61);
            result.Config.Hub.Trade.TradeEmbedSettings.UseEmbeds.Should().BeFalse();
            result.Config.Hub.Trade.TradeEmbedSettings.PreferredImageSize.Should().Be(TradeSettings.ImageSize.Size128x128);
            result.Config.Hub.Trade.TradeEmbedSettings.ShowIVs.Should().BeFalse();
            result.Config.Hub.Trade.RequestFolderSettings.EventsFolder.Should().Be("old-events");
            result.Config.Hub.Trade.RequestFolderSettings.BattleReadyPKMFolder.Should().Be("old-battle-ready");
            result.Config.Hub.Trade.CountStatsSettings.CompletedTrades.Should().Be(42);
            result.Config.Hub.Trade.CountStatsSettings.EmitCountsOnStatusCheck.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_DisablingEmbedsDoesNotEraseSavedEmbedChoices()
    {
        var dir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(
                configPath,
                "{\"ConfigVersion\":1,\"Mode\":4,\"Bots\":[],\"Hub\":{\"Trade\":{\"TradeEmbedSettings\":{\"PreferredImageSize\":1,\"MoveTypeEmojis\":true,\"ShowIVs\":true,\"UseEmbeds\":false}}}}");

            var result = ProgramConfigPersistence.LoadOrCreate(configPath);

            result.Config.Hub.Trade.TradeEmbedSettings.UseEmbeds.Should().BeFalse();
            result.Config.Hub.Trade.TradeEmbedSettings.PreferredImageSize.Should().Be(TradeSettings.ImageSize.Size128x128);
            result.Config.Hub.Trade.TradeEmbedSettings.MoveTypeEmojis.Should().BeTrue();
            result.Config.Hub.Trade.TradeEmbedSettings.ShowIVs.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_PrefersLegalityBatchSettingsAndSynchronizesCompatibilityValues()
    {
        var dir = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(dir, "config.json");
            const string json = "{\"ConfigVersion\":1,\"Mode\":4,\"Bots\":[],\"Hub\":{\"Legality\":{\"AllowBatchTrades\":false,\"MaxPkmsPerTrade\":7},\"Trade\":{\"TradeConfiguration\":{\"AllowBatchTrades\":true,\"MaxPkmsPerTrade\":3}}}}";
            var deserialized = JsonSerializer.Deserialize(json, ProgramConfigContext.Default.ProgramConfig)!;
            GetLegalitySetting<bool>(deserialized, "AllowBatchTrades").Should().BeFalse("source-generated config metadata must include the new setting");
            GetLegalitySetting<int>(deserialized, "MaxPkmsPerTrade").Should().Be(7);
            File.WriteAllText(configPath, json);

            var result = ProgramConfigPersistence.LoadOrCreate(configPath);

            GetLegalitySetting<bool>(result.Config, "AllowBatchTrades").Should().BeFalse("the new Legality value must win during load");
            GetLegalitySetting<int>(result.Config, "MaxPkmsPerTrade").Should().Be(7);

            ProgramConfigPersistence.SaveAtomic(result.Config, configPath, out _);
            using var saved = JsonDocument.Parse(File.ReadAllText(configPath));
            var hub = saved.RootElement.GetProperty("Hub");
            var legality = hub.GetProperty("Legality");
            var compatibility = hub.GetProperty("Trade").GetProperty("TradeConfiguration");
            legality.GetProperty("AllowBatchTrades").GetBoolean().Should().BeFalse("the authoritative Legality value must be saved");
            legality.GetProperty("MaxPkmsPerTrade").GetInt32().Should().Be(7);
            compatibility.GetProperty("AllowBatchTrades").GetBoolean().Should().BeFalse("the compatibility value must synchronize on save");
            compatibility.GetProperty("MaxPkmsPerTrade").GetInt32().Should().Be(7);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FolderDefaults_DoNotOverwriteLoadedFolderSettings()
    {
        var dir = CreateTempDirectory();
        try
        {
            var dump = Path.Combine(dir, "existing-dump");
            var distribute = Path.Combine(dir, "existing-distribute");
            var folder = new FolderSettings { Dump = false, DumpFolder = dump, DistributeFolder = distribute };

            folder.CreateDefaults(Path.Combine(dir, "bin"), enableDumpWhenNew: false);

            folder.Dump.Should().BeFalse();
            folder.DumpFolder.Should().Be(dump);
            folder.DistributeFolder.Should().Be(distribute);
            Directory.Exists(dump).Should().BeTrue();
            Directory.Exists(distribute).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Validate_WarnsWhenDiscordChannelStatusCannotIdentifyAChannelOrState()
    {
        var config = new ProgramConfig();
        config.Hub.Discord.ChannelWhitelist.List.Clear();
        config.Hub.Discord.ChannelStatus = true;
        config.Hub.Discord.OnlineEmoji = "🟢";
        config.Hub.Discord.OfflineEmoji = "🟢";

        var warnings = ProgramConfigPersistence.Validate(config);

        warnings.Should().Contain(message => message.Contains("ChannelWhitelist has no entries"));
        warnings.Should().Contain(message => message.Contains("OnlineEmoji and OfflineEmoji are identical"));
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sysbot-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static T GetLegalitySetting<T>(ProgramConfig config, string propertyName)
    {
        var property = typeof(LegalitySettings).GetProperty(propertyName);
        property.Should().NotBeNull($"Legality should expose {propertyName}");
        return (T)property!.GetValue(config.Hub.Legality)!;
    }
}
