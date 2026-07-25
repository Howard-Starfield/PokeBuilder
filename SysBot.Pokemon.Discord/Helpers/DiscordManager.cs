using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon.Discord;

public class DiscordManager(DiscordSettings Config)
{
    public readonly DiscordSettings Config = Config;

    public RemoteControlAccessList BlacklistedServers => Config.ServerBlacklist;

    public RemoteControlAccessList BlacklistedUsers => Config.UserBlacklist;

    public RemoteControlAccessList FavoredRoles => Config.RoleFavored;

    public ulong Owner { get; internal set; }

    public RemoteControlAccessList RolesClone => Config.RoleCanClone;

    public RemoteControlAccessList RolesDump => Config.RoleCanDump;

    public RemoteControlAccessList RolesFixOT => Config.RoleCanFixOT;

    public RemoteControlAccessList RolesRemoteControl => Config.RoleRemoteControl;

    public RemoteControlAccessList RolesSeed => Config.RoleCanSeedCheckorSpecialRequest;

    public RemoteControlAccessList RolesTrade => Config.RoleCanTrade;

    public RemoteControlAccessList SudoDiscord => Config.GlobalSudoList;

    public RemoteControlAccessList SudoRoles => Config.RoleSudo;

    public RemoteControlAccessList WhitelistedChannels => Config.ChannelWhitelist;

    public bool CanUseCommandChannel(ulong channel) => (WhitelistedChannels.List.Count == 0 && WhitelistedChannels.AllowIfEmpty) || WhitelistedChannels.Contains(channel);

    public bool CanUseCommandUser(ulong uid) => !BlacklistedUsers.Contains(uid);

    public bool CanUseSudo(ulong uid) => SudoDiscord.Contains(uid);

    public bool CanUseSudo(IEnumerable<SocketRole> roles) => HasConfiguredRole(SudoRoles, roles);

    public bool GetHasRoleAccess(string type, IEnumerable<SocketRole> roles)
    {
        var set = GetSet(type);
        return set is { AllowIfEmpty: true, List.Count: 0 } || HasConfiguredRole(set, roles);
    }

    public RequestSignificance GetSignificance(IEnumerable<SocketRole> roles)
    {
        var result = RequestSignificance.None;
        foreach (var role in roles)
        {
            if (SudoRoles.Contains(role.Id) || (!string.IsNullOrWhiteSpace(role.Name) && SudoRoles.Contains(role.Name)))
                result = RequestSignificance.Favored;
            if (FavoredRoles.Contains(role.Id) || (!string.IsNullOrWhiteSpace(role.Name) && FavoredRoles.Contains(role.Name)))
                result = RequestSignificance.Favored;
        }
        return result;
    }

    private static bool HasConfiguredRole(RemoteControlAccessList set, IEnumerable<SocketRole> roles) =>
        roles.Any(role => set.Contains(role.Id) || (!string.IsNullOrWhiteSpace(role.Name) && set.Contains(role.Name)));

    private RemoteControlAccessList GetSet(string type) => type switch
    {
        nameof(RolesClone) => RolesClone,
        nameof(RolesTrade) => RolesTrade,
        nameof(RolesSeed) => RolesSeed,
        nameof(RolesDump) => RolesDump,
        nameof(RolesFixOT) => RolesFixOT,
        nameof(RolesRemoteControl) => RolesRemoteControl,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
