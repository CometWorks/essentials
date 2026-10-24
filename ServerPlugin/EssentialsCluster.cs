using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PluginSdk.Clustering;
using PluginSdk.Storage;

namespace ServerPlugin;

// Essentials' handle on the Magnetar cluster's plugin services (PluginSdk). On a plain server,
// and in a cluster build without them, there is no client and the callers keep the local view.
internal static class EssentialsCluster
{
    private const string PluginId = "DF09925B-6D53-41F2-899C-CA6214B5DD6F";

    private static PluginClusterClient client;

    /// <summary>The cluster client, or null on a plain server or under another plugin id.</summary>
    public static PluginClusterClient Client()
    {
        if (client != null || !PluginCluster.IsClusterProcess)
            return client;

        return Bind();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PluginClusterClient Bind()
    {
        try
        {
            client = PluginCluster.ForPlugin(PluginId);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
        {
            // Loaded under another id (a dev folder with its own manifest id): no cluster views
        }

        return client;
    }

    private static string sharedDirectory;

    /// <summary>
    /// Essentials' folder in the cluster's shared storage, the same on every node; null on a plain
    /// server, or when the cluster has none (the callers then keep their local files).
    /// </summary>
    public static string SharedDirectory()
    {
        if (sharedDirectory != null || !PluginCluster.IsClusterProcess)
            return sharedDirectory;

        return BindStorage();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string BindStorage()
    {
        try
        {
            sharedDirectory = PluginStorage.GetSharedDirectory(PluginId);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
        {
            // Another plugin id, or a cluster without shared storage
        }

        return sharedDirectory;
    }

    /// <summary>
    /// Game thread: every player online anywhere in the cluster, or null when this process has no
    /// cluster view (a plain server: use the local players).
    /// </summary>
    public static IReadOnlyList<PluginPlayerInfo> OnlinePlayers() => Client()?.OnlinePlayers();
}
