using System;
using HarmonyLib;
using PluginSdk.Clustering;
using Sandbox.Game.World;
using VRageMath;

namespace ServerPlugin.Regions;

internal static class RegionRuntime
{
    private static volatile RegionStore store;
    private static volatile bool loadFailed;
    public static RegionStore Store => loadFailed ? null : store;

    public static string Unavailable =>
        PluginCluster.IsClusterProcess ? "Regions currently require a standalone world."
        : loadFailed ? "Could not load this world's regions. See the server log before editing."
        : "Regions are unavailable until the world has loaded.";

    [HarmonyPatch(typeof(MySession), nameof(MySession.BeforeStartComponents))]
    private static class WorldStarting
    {
        private static void Prefix(MySession __instance)
        {
            Reset();
            if (PluginCluster.IsClusterProcess)
                return;
            try
            {
                store = new RegionStore(__instance.CurrentPath);
            }
            catch (Exception error)
            {
                loadFailed = true;
                Plugin.Instance?.Log.Critical(
                    error,
                    "Cannot read world regions. Vanilla firing is blocked until the file is repaired and the world reloaded."
                );
            }
        }
    }

    [HarmonyPatch(typeof(MySession), nameof(MySession.SaveDataComponents))]
    private static class WorldSaving
    {
        private static void Prefix(MySession __instance)
        {
            var current = store;
            if (current == null)
                return;
            try
            {
                current.FollowSave(__instance.CurrentPath);
                loadFailed = false;
            }
            catch (Exception error)
            {
                // Keep the source snapshot for a retry, but never accept edits against
                // the template or allow combat under the wrong destination's policy.
                loadFailed = true;
                Plugin.Instance?.Log.Critical(
                    error,
                    "Cannot save regions in the destination world."
                );
                throw;
            }
        }
    }

    public static void Reset()
    {
        store = null;
        loadFailed = false;
    }

    // Queries read an immutable snapshot without I/O or entity scans.
    private static CombatRegion[] Active =>
        Plugin.Instance?.PluginConfig?.Enabled == true ? Store?.Snapshot : null;
    private static bool BlockOnLoadError =>
        loadFailed && Plugin.Instance?.PluginConfig?.Enabled == true;

    public static bool BlocksFiring(BoundingBoxD bounds)
    {
        var regions = Active;
        if (regions == null)
            return BlockOnLoadError;
        foreach (var region in regions)
            if ((region.Rules & RegionRules.Firing) != 0 && region.Intersects(bounds))
                return true;
        return false;
    }

    public static bool BlocksEntry(Vector3D from, Vector3D to)
    {
        var regions = Active;
        if (regions == null)
            return BlockOnLoadError;
        foreach (var region in regions)
            if ((region.Rules & RegionRules.Incoming) != 0 && region.Entry(from, to).HasValue)
                return true;
        return false;
    }

    public static void ClipPenetration(Vector3D from, ref Vector3D to)
    {
        var regions = Active;
        if (regions == null)
            return;
        double first = 1;
        foreach (var region in regions)
        {
            if ((region.Rules & RegionRules.Incoming) == 0)
                continue;
            double? entry = region.Entry(from, to);
            if (entry.HasValue)
                first = Math.Min(first, entry.Value);
        }
        if (first < 1)
            to = from + (to - from) * first;
    }
}
