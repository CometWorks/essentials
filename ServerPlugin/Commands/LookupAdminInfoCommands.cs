using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.World;
using VRage.Game.ModAPI;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("getsteamid", "Look up Steam and identity ids for matching identities.")]
    [Permission(MyPromoteLevel.None)]
    public string GetSteamId(string target, bool online = false)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "Usage: !ess getsteamid <name|steamId|identityId> [online]";

        List<IMyIdentity> identities = online
            ? GetOnlineIdentitiesMatching(target)
            : GetIdentitiesMatching(target);

        if (identities.Count == 0)
            return "Could not find that player.";

        StringBuilder sb = new StringBuilder();
        foreach (
            IMyIdentity identity in identities
                .OrderBy(identity => identity.DisplayName)
                .ThenBy(identity => identity.IdentityId)
        )
            sb.AppendLine(FormatIdentityLookup(identity));

        return sb.ToString();
    }

    [Command("listids", "List online player Steam IDs.")]
    [Permission(MyPromoteLevel.None)]
    public string ListIds()
    {
        List<MyPlayer> players = Utilities
            .GetOnlinePlayers()
            .OrderBy(player => player.DisplayName)
            .ToList();

        if (players.Count == 0)
            return "No players online.";

        StringBuilder sb = new StringBuilder();
        foreach (MyPlayer player in players)
        {
            string steamName = GetSteamMemberName(player.Id.SteamId);
            string identityName = player.Identity?.DisplayName ?? player.DisplayName;
            sb.AppendLine($"Names: {steamName} : {identityName} | ID: {player.Id.SteamId}");
        }

        return sb.ToString();
    }

    [Command("listnames", "List online players whose Steam and identity names differ.")]
    [Permission(MyPromoteLevel.None)]
    public string ListNames()
    {
        List<MyPlayer> mismatches = Utilities
            .GetOnlinePlayers()
            .Where(player =>
                !string.Equals(
                    GetSteamMemberName(player.Id.SteamId),
                    player.Identity?.DisplayName ?? player.DisplayName,
                    StringComparison.InvariantCulture
                )
            )
            .OrderBy(player => GetSteamMemberName(player.Id.SteamId))
            .ToList();

        if (mismatches.Count == 0)
            return "No players with mismatching names.";

        StringBuilder sb = new StringBuilder();
        foreach (MyPlayer player in mismatches)
            sb.AppendLine(
                $"Steam: {GetSteamMemberName(player.Id.SteamId)} | Identity: {player.Identity?.DisplayName ?? player.DisplayName}"
            );

        return sb.ToString();
    }

    [Command("updatename", "Update an identity display name.")]
    [Permission(MyPromoteLevel.Admin)]
    public string UpdateName(string playerNameOrId, params string[] newNameWords)
    {
        string newName = string.Join(" ", newNameWords ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(playerNameOrId) || string.IsNullOrWhiteSpace(newName))
            return "Usage: !ess updatename <player|steamId|identityId> <newName>";

        if (Utilities.GetIdentityByNameOrIds(playerNameOrId) is not MyIdentity identity)
            return "Could not find that identity.";

        identity.SetDisplayName(newName);
        return "New identity name: " + identity.DisplayName;
    }

    [Command("lastlogin", "Show when an identity last logged in.")]
    [Permission(MyPromoteLevel.Admin)]
    public string LastLogin(string playerNameOrId)
    {
        MyIdentity identity = Utilities.GetIdentityByNameOrIds(playerNameOrId) as MyIdentity;
        return identity == null
            ? "Could not find that player."
            : $"{identity.DisplayName} last login: {identity.LastLoginTime.ToString("O", CultureInfo.InvariantCulture)}";
    }

    [Command("isnpc", "Report whether an identity is an NPC.")]
    [Permission(MyPromoteLevel.Admin)]
    public string IsNpc(string playerNameOrId)
    {
        IMyIdentity identity = Utilities.GetIdentityByNameOrIds(playerNameOrId);
        return identity == null
            ? "Could not find that identity."
            : MySession
                .Static.Players.IdentityIsNpc(identity.IdentityId)
                .ToString(CultureInfo.InvariantCulture);
    }

    [Command("getfacid", "Get a faction id from its tag.")]
    [Permission(MyPromoteLevel.Admin)]
    public string GetFactionId(string tag)
    {
        MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
        return faction == null
            ? "Error faction not found."
            : faction.FactionId.ToString(CultureInfo.InvariantCulture);
    }

    [Command("worldpcu", "Show total world PCU.")]
    [Permission(MyPromoteLevel.Admin)]
    public string WorldPcu() =>
        "Total world PCU: "
        + (MySession.Static?.TotalSessionPCU ?? 0).ToString("#,##0", CultureInfo.InvariantCulture);

    private static List<IMyIdentity> GetIdentitiesMatching(string target)
    {
        List<IMyIdentity> matches = new List<IMyIdentity>();
        bool hasLong = long.TryParse(
            target,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long longId
        );
        bool hasUlong = ulong.TryParse(
            target,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out ulong steamId
        );

        foreach (IMyIdentity identity in MySession.Static.Players.GetAllIdentities())
        {
            if (
                string.Equals(
                    identity.DisplayName,
                    target,
                    StringComparison.InvariantCultureIgnoreCase
                )
                || identity.DisplayName.IndexOf(target, StringComparison.InvariantCultureIgnoreCase)
                    >= 0
                || hasLong && identity.IdentityId == longId
                || hasUlong && Utilities.GetSteamId(identity.IdentityId) == steamId
            )
            {
                matches.Add(identity);
            }
        }

        return matches;
    }

    private static List<IMyIdentity> GetOnlineIdentitiesMatching(string target) =>
        Utilities
            .GetOnlinePlayers()
            .Where(player =>
                player.Identity != null
                && (
                    player.DisplayName.IndexOf(target, StringComparison.InvariantCultureIgnoreCase)
                        >= 0
                    || (player.Identity.DisplayName ?? string.Empty).IndexOf(
                        target,
                        StringComparison.InvariantCultureIgnoreCase
                    ) >= 0
                    || GetSteamMemberName(player.Id.SteamId)
                        .IndexOf(target, StringComparison.InvariantCultureIgnoreCase) >= 0
                    || player.Id.SteamId.ToString(CultureInfo.InvariantCulture) == target
                )
            )
            .Select(player => (IMyIdentity)player.Identity)
            .Distinct()
            .ToList();

    private static string FormatIdentityLookup(IMyIdentity identity)
    {
        ulong steamId = Utilities.GetSteamId(identity.IdentityId);
        string steamName = steamId == 0 ? "Unknown" : GetSteamMemberName(steamId);
        return string.Join(
            "\n",
            "Steam Name: " + steamName,
            "Display Name: " + identity.DisplayName,
            "Steam ID: " + steamId.ToString(CultureInfo.InvariantCulture),
            "Identity ID: " + identity.IdentityId.ToString(CultureInfo.InvariantCulture)
        );
    }

    private static string GetSteamMemberName(ulong steamId)
    {
        if (steamId == 0 || MyMultiplayer.Static == null)
            return "Unknown";

        string name = MyMultiplayer.Static.GetMemberName(steamId);
        return string.IsNullOrWhiteSpace(name)
            ? steamId.ToString(CultureInfo.InvariantCulture)
            : name;
    }
}
