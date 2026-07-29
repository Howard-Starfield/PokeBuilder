using FluentAssertions;
using SysBot.Pokemon;
using SysBot.Pokemon.Helpers;
using System;
using System.Text.Json;
using Xunit;

namespace SysBot.Tests;

public sealed class StartupBehaviorTests
{
    [Fact]
    public void StartupOptions_RoundTripThroughConfigJson()
    {
        var config = new ProgramConfig();
        config.Hub.StartWithWindows = true;
        config.Hub.AutoStartBots = true;
        config.Hub.ScheduledRestartEnabled = true;
        config.Hub.RestartCronSchedule = "30 3 * * 1-5";

        var json = JsonSerializer.Serialize(config, ProgramConfigContext.Default.ProgramConfig);
        var restored = JsonSerializer.Deserialize(json, ProgramConfigContext.Default.ProgramConfig);

        restored.Should().NotBeNull();
        restored!.Hub.StartWithWindows.Should().BeTrue();
        restored.Hub.AutoStartBots.Should().BeTrue();
        restored.Hub.ScheduledRestartEnabled.Should().BeTrue();
        restored.Hub.RestartCronSchedule.Should().Be("30 3 * * 1-5");
    }

    [Fact]
    public void StartupOptions_DefaultOffForOlderConfigs()
    {
        const string json = """{"ConfigVersion":1,"Mode":4,"Bots":[],"Hub":{}}""";

        var restored = JsonSerializer.Deserialize(json, ProgramConfigContext.Default.ProgramConfig);

        restored.Should().NotBeNull();
        restored!.Hub.StartWithWindows.Should().BeFalse();
        restored.Hub.AutoStartBots.Should().BeFalse();
        restored.Hub.ScheduledRestartEnabled.Should().BeFalse();
        restored.Hub.RestartCronSchedule.Should().Be(BaseConfig.DefaultRestartCronSchedule);
    }

    [Fact]
    public void WindowsStartupCommand_PreservesExecutableAndCustomConfigPaths()
    {
        var command = PokeBotStartupCommand.Build(
            @"C:\Program Files\PokeBuilder\PokeBot.exe",
            @"D:\Bot Configs\Scarlet settings.json");

        command.Should().Be(
            "\"C:\\Program Files\\PokeBuilder\\PokeBot.exe\" \"D:\\Bot Configs\\Scarlet settings.json\"");
    }

    [Theory]
    [InlineData("", @"C:\Bots\config.json")]
    [InlineData(@"C:\Bots\PokeBot.exe", "")]
    [InlineData("bad\"path.exe", @"C:\Bots\config.json")]
    public void WindowsStartupCommand_RejectsUnsafePaths(string executable, string config)
    {
        var action = () => PokeBotStartupCommand.Build(executable, config);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("0 4 * * *", "2026-07-28T03:59:00", "2026-07-28T04:00:00")]
    [InlineData("0 4 * * *", "2026-07-28T04:00:00", "2026-07-29T04:00:00")]
    [InlineData("*/15 8-9 * * 1-5", "2026-07-27T08:01:00", "2026-07-27T08:15:00")]
    [InlineData("0 2 1 * 0", "2026-07-27T02:00:00", "2026-08-01T02:00:00")]
    public void CronSchedule_FindsNextLocalOccurrence(
        string expression,
        string afterText,
        string expectedText)
    {
        var after = DateTime.Parse(afterText);
        var expected = DateTime.Parse(expectedText);

        CronSchedule.Parse(expression).GetNextOccurrence(after).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 4 * *")]
    [InlineData("60 4 * * *")]
    [InlineData("0 4 * 13 *")]
    [InlineData("0 4 * * MON")]
    [InlineData("0/0 4 * * *")]
    [InlineData("0 4 31 2 *")]
    public void CronSchedule_RejectsInvalidExpressions(string expression)
    {
        var action = () => CronSchedule.Parse(expression);

        action.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("0 4 * * *", 4, 0)]
    [InlineData("45 23 * * *", 23, 45)]
    public void CronSchedule_ConvertsDailySchedulesForTimePicker(
        string expression,
        int expectedHour,
        int expectedMinute)
    {
        CronSchedule.TryGetDailyTime(expression, out var time).Should().BeTrue();
        time.Should().Be(new TimeSpan(expectedHour, expectedMinute, 0));
        CronSchedule.FromDailyTime(time).Should().Be(expression);
    }

    [Fact]
    public void CronSchedule_DoesNotPresentComplexScheduleAsDailyTime()
    {
        CronSchedule.TryGetDailyTime("0 */6 * * *", out _).Should().BeFalse();
    }

    [Fact]
    public void Normalize_RepairsInvalidRestartCron()
    {
        var config = new ProgramConfig();
        config.Hub.RestartCronSchedule = "not a schedule";

        ProgramConfigPersistence.Normalize(config).Should().BeTrue();
        config.Hub.RestartCronSchedule.Should().Be(BaseConfig.DefaultRestartCronSchedule);
    }
}
