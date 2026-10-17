using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ServerPlugin;

internal sealed class ChatMuteRecord
{
    public ChatMuteRecord(
        ulong steamId,
        long identityId,
        string displayName,
        DateTime? expiresAtUtc
    )
    {
        SteamId = steamId;
        IdentityId = identityId;
        DisplayName = displayName;
        ExpiresAtUtc = expiresAtUtc;
    }

    public ulong SteamId { get; }
    public long IdentityId { get; }
    public string DisplayName { get; }
    public DateTime? ExpiresAtUtc { get; }

    public bool IsExpired(DateTime utcNow) => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= utcNow;

    public string RemainingText(DateTime utcNow)
    {
        if (!ExpiresAtUtc.HasValue)
            return "inf";

        TimeSpan remaining = ExpiresAtUtc.Value - utcNow;
        return remaining <= TimeSpan.Zero ? "expired" : FormatDuration(remaining);
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalDays >= 1
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

internal static class ChatMuteService
{
    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<ulong, ChatMuteRecord> MutedPlayers =
        new Dictionary<ulong, ChatMuteRecord>();

    public static void Mute(
        ulong steamId,
        long identityId,
        string displayName,
        DateTime? expiresAtUtc
    )
    {
        lock (SyncRoot)
            MutedPlayers[steamId] = new ChatMuteRecord(
                steamId,
                identityId,
                displayName,
                expiresAtUtc
            );
    }

    public static bool Unmute(ulong steamId)
    {
        lock (SyncRoot)
            return MutedPlayers.Remove(steamId);
    }

    public static bool IsMuted(ulong steamId, out ChatMuteRecord record)
    {
        lock (SyncRoot)
        {
            CleanupExpiredLocked(DateTime.UtcNow);
            return MutedPlayers.TryGetValue(steamId, out record);
        }
    }

    public static List<ChatMuteRecord> List()
    {
        lock (SyncRoot)
        {
            CleanupExpiredLocked(DateTime.UtcNow);
            return MutedPlayers
                .Values.OrderBy(record => record.DisplayName)
                .ThenBy(record => record.SteamId)
                .ToList();
        }
    }

    private static void CleanupExpiredLocked(DateTime utcNow)
    {
        List<ulong> expired = MutedPlayers
            .Where(pair => pair.Value.IsExpired(utcNow))
            .Select(pair => pair.Key)
            .ToList();

        foreach (ulong steamId in expired)
            MutedPlayers.Remove(steamId);
    }
}
