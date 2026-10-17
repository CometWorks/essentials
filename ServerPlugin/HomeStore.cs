using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using VRage.FileSystem;
using VRageMath;

namespace ServerPlugin;

internal static class HomeStore
{
    private static readonly object Sync = new object();
    private static HomeStoreData data;

    private static string Path =>
        System.IO.Path.Combine(MyFileSystem.UserDataPath, "Essentials.Homes.xml");

    public static bool TryAdd(
        ulong steamId,
        string name,
        Vector3D position,
        int maxHomes,
        out string error
    )
    {
        error = null;
        name = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Usage: !ess home add <name>";
            return false;
        }

        lock (Sync)
        {
            HomePlayerRecord player = GetOrCreatePlayer(steamId);
            if (player.Homes.Any(home => SameHomeName(home.Name, name)))
            {
                error = $"Home '{name}' already exists.";
                return false;
            }

            if (maxHomes >= 0 && player.Homes.Count >= maxHomes)
            {
                error = $"You have the maximum amount of homes ({maxHomes}).";
                return false;
            }

            player.Homes.Add(
                new HomeLocation
                {
                    Name = name,
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                }
            );

            Save();
            return true;
        }
    }

    public static bool TryRemove(ulong steamId, string name)
    {
        name = NormalizeName(name);
        lock (Sync)
        {
            HomePlayerRecord player = FindPlayer(steamId);
            if (player == null)
                return false;

            HomeLocation home = player.Homes.FirstOrDefault(candidate =>
                SameHomeName(candidate.Name, name)
            );
            if (home == null)
                return false;

            player.Homes.Remove(home);
            Save();
            return true;
        }
    }

    public static IReadOnlyList<string> List(ulong steamId)
    {
        lock (Sync)
        {
            HomePlayerRecord player = FindPlayer(steamId);
            return player?.Homes.Select(home => home.Name).OrderBy(name => name).ToList()
                ?? new List<string>();
        }
    }

    public static bool TryGet(ulong steamId, string name, out Vector3D position)
    {
        name = NormalizeName(name);
        lock (Sync)
        {
            HomeLocation home = FindPlayer(steamId)
                ?.Homes.FirstOrDefault(candidate => SameHomeName(candidate.Name, name));
            if (home != null)
            {
                position = new Vector3D(home.X, home.Y, home.Z);
                return true;
            }
        }

        position = default;
        return false;
    }

    private static HomePlayerRecord GetOrCreatePlayer(ulong steamId)
    {
        EnsureLoaded();
        HomePlayerRecord player = FindPlayer(steamId);
        if (player != null)
            return player;

        player = new HomePlayerRecord { SteamId = steamId };
        data.Players.Add(player);
        return player;
    }

    private static HomePlayerRecord FindPlayer(ulong steamId)
    {
        EnsureLoaded();
        return data.Players.FirstOrDefault(player => player.SteamId == steamId);
    }

    private static void EnsureLoaded()
    {
        if (data != null)
            return;

        try
        {
            if (File.Exists(Path))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(HomeStoreData));
                using Stream stream = File.OpenRead(Path);
                data = (HomeStoreData)serializer.Deserialize(stream);
                data.Players ??= new List<HomePlayerRecord>();
                foreach (HomePlayerRecord player in data.Players)
                    player.Homes ??= new List<HomeLocation>();
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.Warning(ex, "Failed to load home store: {0}", Path);
        }

        data = new HomeStoreData();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            XmlSerializer serializer = new XmlSerializer(typeof(HomeStoreData));
            using Stream stream = File.Create(Path);
            serializer.Serialize(stream, data);
        }
        catch (Exception ex)
        {
            Plugin.Instance?.Log.Warning(ex, "Failed to save home store: {0}", Path);
        }
    }

    private static string NormalizeName(string name) => name?.Trim();

    private static bool SameHomeName(string left, string right) =>
        string.Equals(left, right, StringComparison.InvariantCultureIgnoreCase);
}

[XmlRoot("Homes")]
public sealed class HomeStoreData
{
    public List<HomePlayerRecord> Players { get; set; } = new List<HomePlayerRecord>();
}

public sealed class HomePlayerRecord
{
    [XmlAttribute]
    public ulong SteamId { get; set; }

    public List<HomeLocation> Homes { get; set; } = new List<HomeLocation>();
}

public sealed class HomeLocation
{
    [XmlAttribute]
    public string Name { get; set; }

    [XmlAttribute]
    public double X { get; set; }

    [XmlAttribute]
    public double Y { get; set; }

    [XmlAttribute]
    public double Z { get; set; }
}
