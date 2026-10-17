using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk;
using PluginSdk.Commands;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private sealed class PlayerLookup
    {
        public ulong SteamId { get; set; }
        public long IdentityId { get; set; }
        public string DisplayName { get; set; }
    }

    [Command("motd", "Show the server Message of the Day.")]
    [Permission(MyPromoteLevel.None)]
    public string Motd()
    {
        string motd = BuildMotdBody();
        if (string.IsNullOrWhiteSpace(motd))
            return "MOTD is not configured.";

        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return motd;

        bool opened = MissionScreens.ShowToPlayer(
            Context.Caller.IdentityId,
            "Message of the Day",
            null,
            MySession.Static?.Name ?? "Essentials",
            motd,
            "Close"
        );

        return opened ? "MOTD opened." : "Mission screen unavailable. MOTD:\n" + motd;
    }

    [Command("stats", "Show server runtime statistics.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Stats()
    {
        MySession session = MySession.Static;
        Process process = Process.GetCurrentProcess();
        int entities = MyEntities.GetEntities().Count();
        int grids = MyEntities
            .GetEntities()
            .OfType<MyCubeGrid>()
            .Count(grid => grid.Projector == null);
        int floatingObjects = MyEntities.GetEntities().OfType<MyFloatingObject>().Count();
        int onlinePlayers = Utilities.GetOnlinePlayerCount();
        int maxPlayers =
            MyMultiplayer.Static != null
                ? MyMultiplayer.Static.MemberLimit
                : session?.MaxPlayers ?? 0;
        TimeSpan processUptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Server stats:");
        sb.AppendLine($"World: {session?.Name ?? "Unknown"}");
        sb.AppendLine($"Players: {onlinePlayers:#,##0}/{maxPlayers:#,##0}");
        sb.AppendLine(
            $"Sim speed: server {session?.SessionSimSpeedServer ?? 0f:0.00}, player {session?.SessionSimSpeedPlayer ?? 0f:0.00}"
        );
        sb.AppendLine(
            $"Entities: {entities:#,##0} total, {grids:#,##0} grids, {floatingObjects:#,##0} floating objects"
        );
        sb.AppendLine(
            $"PCU: {session?.TotalSessionPCU ?? 0:#,##0} session / {session?.TotalPCU ?? 0:#,##0} limit"
        );
        sb.AppendLine($"Game time: {FormatDuration(session?.ElapsedGameTime ?? TimeSpan.Zero)}");
        sb.AppendLine($"Process uptime: {FormatDuration(processUptime)}");
        sb.AppendLine(
            $"Memory: {FormatBytes(process.WorkingSet64)} working set, {FormatBytes(GC.GetTotalMemory(false))} managed"
        );
        return sb.ToString();
    }

    [Command("playerlist", "List current online players.")]
    [Permission(MyPromoteLevel.Admin)]
    public string PlayerList()
    {
        List<MyPlayer> players = Utilities
            .GetOnlinePlayers()
            .OrderBy(player => player.DisplayName)
            .ToList();

        if (players.Count == 0)
            return "No players online.";

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Found {players.Count:#,##0} player(s) online:");
        foreach (MyPlayer player in players)
        {
            sb.AppendLine($"{player.DisplayName}");
            sb.AppendLine($"> IdentityId: {player.Identity?.IdentityId ?? 0}");
            sb.AppendLine($"> SteamId: {player.Id.SteamId}");
            sb.AppendLine($"> Promote: {MySession.Static.GetUserPromoteLevel(player.Id.SteamId)}");
        }

        return sb.ToString();
    }

    [Command("mute", "Mute a player in chat for minutes, or indefinitely with 0.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Mute(string player, int minutes = 0)
    {
        if (minutes < 0)
            return "Mute minutes must be 0 or greater.";

        if (!TryResolvePlayer(player, out PlayerLookup lookup))
            return $"Could not find user {player}.";

        DateTime? expires = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : null;
        ChatMuteService.Mute(lookup.SteamId, lookup.IdentityId, lookup.DisplayName, expires);

        Plugin.Instance?.Log.Info(
            "Muted chat for {0} ({1}) until {2}",
            lookup.DisplayName,
            lookup.SteamId,
            expires?.ToString("O", CultureInfo.InvariantCulture) ?? "forever"
        );
        return minutes > 0
            ? $"Muted user {lookup.DisplayName} for {minutes:#,##0} minute(s)."
            : $"Muted user {lookup.DisplayName} indefinitely.";
    }

    [Command("unmute", "Remove a chat mute from a player.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Unmute(string player)
    {
        if (!TryResolvePlayer(player, out PlayerLookup lookup))
            return $"Could not find user {player}.";

        if (!ChatMuteService.Unmute(lookup.SteamId))
            return $"Failed to unmute user {lookup.DisplayName}. They are not muted.";

        Plugin.Instance?.Log.Info("Unmuted chat for {0} ({1})", lookup.DisplayName, lookup.SteamId);
        return $"Unmuted user {lookup.DisplayName}.";
    }

    [Command("list mute", "List muted players and remaining mute time.")]
    [Permission(MyPromoteLevel.Admin)]
    public string ListMute()
    {
        List<ChatMuteRecord> records = ChatMuteService.List();
        if (records.Count == 0)
            return "No muted users.";

        DateTime now = DateTime.UtcNow;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Muted users:");
        foreach (ChatMuteRecord record in records)
            sb.AppendLine($"{record.DisplayName} ({record.SteamId}): {record.RemainingText(now)}");

        return sb.ToString();
    }

    [Command("msg", "Message another online player.")]
    [Permission(MyPromoteLevel.None)]
    public void Message(string player, params string[] words) => PrivateMessage(player, words);

    [Command("whis", "Message another online player.")]
    [Permission(MyPromoteLevel.None)]
    public void Whisper(string player, params string[] words) => PrivateMessage(player, words);

    private void PrivateMessage(string player, string[] words)
    {
        if (
            Context.Caller.IsConsole
            || Context.Caller.IdentityId == 0
            || Context.Caller.SteamId == 0
        )
        {
            Context.Respond("Only players can use this command.");
            return;
        }

        if (ChatMuteService.IsMuted(Context.Caller.SteamId, out _))
        {
            Context.Respond("You are muted in chat.");
            return;
        }

        string message = string.Join(" ", words);
        if (string.IsNullOrWhiteSpace(player) || string.IsNullOrWhiteSpace(message))
        {
            Context.Respond("Usage: !ess msg <player> <message>");
            return;
        }

        if (Utilities.GetPlayerByNameOrId(player) is not MyPlayer target || target.Id.SteamId == 0)
        {
            Context.Respond($"Could not find online player {player}.");
            return;
        }

        string senderName = string.IsNullOrWhiteSpace(Context.Caller.Name)
            ? Utilities.GetPlayerNameById(Context.Caller.IdentityId)
            : Context.Caller.Name;
        string targetName = target.DisplayName;
        long targetIdentityId = target.Identity?.IdentityId ?? 0;
        if (targetIdentityId == 0)
        {
            Context.Respond($"Could not find online player {player}.");
            return;
        }

        MyVisualScriptLogicProvider.SendChatMessageColored(
            message,
            Color.MediumPurple,
            "PM from " + senderName,
            targetIdentityId,
            MyFontEnum.White
        );

        MyVisualScriptLogicProvider.SendChatMessageColored(
            message,
            Color.MediumPurple,
            "PM to " + targetName,
            Context.Caller.IdentityId,
            MyFontEnum.White
        );
    }

    private static string BuildMotdBody()
    {
        StringBuilder sb = new StringBuilder();
        string motd = Plugin.Instance?.Config?.Motd;
        string url = Plugin.Instance?.Config?.MotdUrl;

        if (!string.IsNullOrWhiteSpace(motd))
            sb.AppendLine(motd.Trim());

        if (!string.IsNullOrWhiteSpace(url))
        {
            if (sb.Length > 0)
                sb.AppendLine();

            sb.AppendLine("URL:");
            sb.AppendLine(url.Trim());
        }

        return sb.ToString().Trim();
    }

    private static bool TryResolvePlayer(string value, out PlayerLookup lookup)
    {
        lookup = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (
            Utilities.GetPlayerByNameOrId(value) is MyPlayer onlinePlayer
            && onlinePlayer.Id.SteamId != 0
        )
        {
            lookup = new PlayerLookup
            {
                SteamId = onlinePlayer.Id.SteamId,
                IdentityId = onlinePlayer.Identity?.IdentityId ?? 0,
                DisplayName = onlinePlayer.DisplayName,
            };
            return true;
        }

        if (Utilities.GetIdentityByNameOrIds(value) is IMyIdentity identity)
        {
            ulong steamId = Utilities.GetSteamId(identity.IdentityId);
            if (steamId == 0)
                return false;

            lookup = new PlayerLookup
            {
                SteamId = steamId,
                IdentityId = identity.IdentityId,
                DisplayName = identity.DisplayName,
            };
            return true;
        }

        if (
            !ulong.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ulong parsedSteamId
            )
            || parsedSteamId == 0
        )
            return false;

        long identityId = MySession.Static?.Players?.TryGetIdentityId(parsedSteamId) ?? 0;
        string displayName = MySession.Static?.Players?.TryGetIdentityNameFromSteamId(
            parsedSteamId
        );
        lookup = new PlayerLookup
        {
            SteamId = parsedSteamId,
            IdentityId = identityId,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? parsedSteamId.ToString(CultureInfo.InvariantCulture)
                : displayName,
        };
        return true;
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return span.TotalDays >= 1
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0:#,##0}d {1:00}:{2:00}:{3:00}",
                (int)span.TotalDays,
                span.Hours,
                span.Minutes,
                span.Seconds
            )
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}",
                span.Hours,
                span.Minutes,
                span.Seconds
            );
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, units[unit]);
    }
}
