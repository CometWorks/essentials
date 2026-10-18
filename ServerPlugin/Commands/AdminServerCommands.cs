using System;
using System.Globalization;
using System.Linq;
using PluginSdk.Commands;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ModAPI;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("admin playercount", "Get or set the max player count.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminPlayerCount(int count = -1)
    {
        if (MyMultiplayer.Static == null)
            return "Multiplayer is not available.";

        if (count >= 0)
            MyMultiplayer.Static.MemberLimit = count;

        return $"Max player count: {MyMultiplayer.Static.MemberLimit}. Current online players: {Utilities.GetOnlinePlayerCount()}.";
    }

    [Command("admin setrank", "Set a player's promote level.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminSetRank(string playerNameOrId, string rank)
    {
        if (!TryResolvePlayer(playerNameOrId, out PlayerLookup player))
            return $"Player '{playerNameOrId}' not found or ID is invalid.";

        if (
            !Enum.TryParse(rank, true, out MyPromoteLevel promoteLevel)
            || promoteLevel > MyPromoteLevel.Admin
        )
            return $"Invalid rank '{rank}'. Valid ranks: None, Scripter, Moderator, SpaceMaster, Admin.";

        MySession.Static.SetUserPromoteLevel(player.SteamId, promoteLevel);
        Plugin.Instance?.Log.Info(
            "Set promote level for {0} ({1}) to {2}",
            player.DisplayName,
            player.SteamId,
            promoteLevel
        );
        return $"Player '{player.DisplayName}' promoted to '{promoteLevel}'.";
    }

    [Command("admin reserve", "Add a player to the reserved slots list.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminReserve(string playerNameOrId)
    {
        if (!TryResolvePlayer(playerNameOrId, out PlayerLookup player))
            return $"Player '{playerNameOrId}' not found or ID is invalid.";

        if (MySandboxGame.ConfigDedicated.Reserved.Contains(player.SteamId))
            return $"ID {player.SteamId} is already reserved.";

        MySandboxGame.ConfigDedicated.Reserved.Add(player.SteamId);
        MySandboxGame.ConfigDedicated.Save();
        Plugin.Instance?.Log.Info(
            "Reserved slot for {0} ({1})",
            player.DisplayName,
            player.SteamId
        );
        return $"ID {player.SteamId} added to reserved slots.";
    }

    [Command("admin unreserve", "Remove a player from the reserved slots list.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminUnreserve(string playerNameOrId)
    {
        if (!TryResolvePlayer(playerNameOrId, out PlayerLookup player))
            return $"Player '{playerNameOrId}' not found or ID is invalid.";

        if (!MySandboxGame.ConfigDedicated.Reserved.Remove(player.SteamId))
            return $"ID {player.SteamId} is already unreserved.";

        MySandboxGame.ConfigDedicated.Save();
        Plugin.Instance?.Log.Info(
            "Unreserved slot for {0} ({1})",
            player.DisplayName,
            player.SteamId
        );
        return $"ID {player.SteamId} removed from reserved slots.";
    }

    [Command("admin give", "Insert an item into player inventories.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminGive(string playerName, string itemType, string itemSubtype, int quantity)
    {
        if (quantity <= 0)
            return "Quantity must be greater than zero.";

        if (
            !MyDefinitionId.TryParse(
                "MyObjectBuilder_" + itemType,
                itemSubtype,
                out MyDefinitionId definitionId
            ) || definitionId.TypeId.IsNull
        )
            return "Invalid item type or subtype.";

        if (string.Equals(playerName, "*", StringComparison.InvariantCultureIgnoreCase))
        {
            int affected = 0;
            foreach (
                MyPlayer player in Utilities
                    .GetOnlinePlayers()
                    .Where(player => player.Identity?.IdentityId != 0)
            )
            {
                MyVisualScriptLogicProvider.AddToPlayersInventory(
                    player.Identity.IdentityId,
                    definitionId,
                    quantity
                );
                MyVisualScriptLogicProvider.ShowNotification(
                    $"You have been given {quantity:#,##0} {itemSubtype} {itemType}",
                    5000,
                    MyFontEnum.Blue,
                    player.Identity.IdentityId
                );
                affected++;
            }

            return $"Item(s) given to {affected:#,##0} online player(s).";
        }

        if (
            Utilities.GetPlayerByNameOrId(playerName) is not MyPlayer target
            || target.Identity?.IdentityId == 0
        )
            return "Player not found.";

        MyVisualScriptLogicProvider.AddToPlayersInventory(
            target.Identity.IdentityId,
            definitionId,
            quantity
        );
        MyVisualScriptLogicProvider.ShowNotification(
            $"You have been given {quantity:#,##0} {itemSubtype} {itemType}",
            5000,
            MyFontEnum.Blue,
            target.Identity.IdentityId
        );
        return "Item(s) given.";
    }

    [Command("kick", "Kick a player from the game.")]
    [Permission(MyPromoteLevel.Moderator)]
    public string Kick(string playerName)
    {
        if (!TryResolvePlayer(playerName, out PlayerLookup player))
            return "Player not found.";

        MyMultiplayer.Static?.KickClient(player.SteamId);
        Plugin.Instance?.Log.Info("Kicked {0} ({1})", player.DisplayName, player.SteamId);
        return $"Player '{player.DisplayName}' kicked.";
    }

    [Command("ban", "Ban a player from the game.")]
    [Permission(MyPromoteLevel.Moderator)]
    public string Ban(string nameOrSteamId) => SetBanState(nameOrSteamId, banned: true);

    [Command("unban", "Unban a player from the game.")]
    [Permission(MyPromoteLevel.Moderator)]
    public string Unban(string nameOrSteamId) => SetBanState(nameOrSteamId, banned: false);

    private string SetBanState(string nameOrSteamId, bool banned)
    {
        ulong steamId;
        string displayName;
        if (TryResolvePlayer(nameOrSteamId, out PlayerLookup player))
        {
            steamId = player.SteamId;
            displayName = player.DisplayName;
        }
        else if (
            ulong.TryParse(
                nameOrSteamId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ulong parsedSteamId
            )
            && parsedSteamId != 0
        )
        {
            steamId = parsedSteamId;
            displayName = steamId.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            return $"Player '{nameOrSteamId}' not found.";
        }

        MyMultiplayer.Static?.BanClient(steamId, banned);
        MySandboxGame.ConfigDedicated.Save();
        Plugin.Instance?.Log.Info(
            "{0} {1} ({2})",
            banned ? "Banned" : "Unbanned",
            displayName,
            steamId
        );
        return banned
            ? $"Player {displayName} banned. ({steamId})"
            : $"Player {displayName} unbanned. ({steamId})";
    }
}
