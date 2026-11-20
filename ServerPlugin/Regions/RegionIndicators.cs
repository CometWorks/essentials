using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;

namespace ServerPlugin.Regions;

internal static class RegionIndicators
{
    private sealed class Observer
    {
        public long Identity;
        public RegionProximity Proximity = new();
        public CombatRegion[] Sent = Array.Empty<CombatRegion>();
        public long Refresh;
        public int DistanceKm;
    }

    private static readonly Dictionary<ulong, Observer> observers = new();

    public static void Reset() => observers.Clear();

    public static void Update(long tick)
    {
        if (tick % 15 != 0 || MySession.Static?.Ready != true)
            return;
        var regions = RegionRuntime.Active ?? Array.Empty<CombatRegion>();
        if (regions.Length == 0 && observers.Count == 0)
            return;

        int distanceKm = Plugin.Instance?.PluginConfig?.RegionBarrierDistanceKm ?? 0;
        var online = new HashSet<ulong>();
        foreach (var player in MySession.Static.Players.GetOnlinePlayers())
        {
            if (!player.IsRealPlayer)
                continue;
            ulong id = player.Id.SteamId;
            online.Add(id);
            try
            {
                observers.TryGetValue(id, out var observer);
                var entity = player.Controller?.ControlledEntity?.Entity;
                long identity = player.Identity?.IdentityId ?? 0;
                if (entity == null || identity == 0 || regions.Length == 0)
                {
                    if (observer != null)
                    {
                        if (
                            observer.Sent.Length != 0
                            && !Send(id, Array.Empty<CombatRegion>(), distanceKm)
                        )
                            continue;
                        observers.Remove(id);
                    }
                    continue;
                }
                if (observer == null)
                    observers[id] = observer = new Observer();
                if (observer.Identity != identity)
                {
                    observer.Identity = identity;
                    observer.Proximity = new RegionProximity();
                }

                observer.Proximity.Sample(
                    regions,
                    entity.PositionComp.GetPosition(),
                    message =>
                        MyVisualScriptLogicProvider.ShowNotification(
                            message,
                            4500,
                            MyFontEnum.White,
                            identity
                        ),
                    distanceKm
                );
                var nearby = observer.Proximity.Nearby;
                if (
                    !nearby.SequenceEqual(observer.Sent)
                    || (
                        nearby.Length > 0
                        && (distanceKm != observer.DistanceKm || tick >= observer.Refresh)
                    )
                )
                {
                    if (Send(id, nearby, distanceKm))
                    {
                        observer.DistanceKm = distanceKm;
                        observer.Sent = nearby;
                        observer.Refresh = tick + 120;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Log.Warning(ex, "Region indicators failed for player {0}", id);
            }
        }
        foreach (var id in observers.Keys.Except(online).ToArray())
            observers.Remove(id);
    }

    private static bool Send(ulong id, CombatRegion[] regions, int distanceKm)
    {
        var multiplayer = MyAPIGateway.Multiplayer;
        // Older companions ignore v2 and keep their close-range grid. Ordered reliable
        // delivery gives current clients the styled snapshot after the compatibility frame.
        return multiplayer != null
            && multiplayer.SendMessageTo(48733, RegionProximity.Encode(regions), id)
            && (
                regions.Length == 0
                || multiplayer.SendMessageTo(48733, RegionProximity.Encode(regions, distanceKm), id)
            );
    }
}
