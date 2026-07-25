using SysBot.Pokemon;
using System;
using TwitchLib.Client.Models.Interfaces;

namespace SysBot.Pokemon.Twitch;

internal sealed class ConfiguredTwitchSendOptions : ISendOptions
{
    public ConfiguredTwitchSendOptions(TwitchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        SendsAllowedInPeriod = (uint)Math.Max(1, settings.ThrottleMessages);
        ThrottlingPeriod = TimeSpan.FromSeconds(Math.Max(1, settings.ThrottleSeconds));
    }

    public uint SendsAllowedInPeriod { get; }
    public ushort SendDelay => 50;
    public TimeSpan ThrottlingPeriod { get; }
    public uint QueueCapacity => 10_000;
    public TimeSpan CacheItemTimeout => TimeSpan.FromMinutes(30);
}
