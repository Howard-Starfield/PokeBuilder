using FluentAssertions;
using SysBot.Pokemon;
using System.ComponentModel;
using Xunit;

namespace SysBot.Tests;

public class LegacySettingsVisibilityTests
{
    [Theory]
    [InlineData(nameof(TwitchSettings.ThrottleWhispers))]
    [InlineData(nameof(TwitchSettings.ThrottleWhispersSeconds))]
    public void LegacyTwitchWhisperThrottle_IsNotShownAsAnActiveSetting(string propertyName)
    {
        TypeDescriptor.GetProperties(typeof(TwitchSettings))[propertyName]!
            .IsBrowsable.Should().BeFalse();
    }

    [Fact]
    public void DuplicateRecoveryResetDelay_IsNotShownAsAnActiveSetting()
    {
        TypeDescriptor.GetProperties(typeof(RecoverySettings))[
                nameof(RecoverySettings.SuccessfulRecoveryResetDelaySeconds)]!
            .IsBrowsable.Should().BeFalse();
    }
}
