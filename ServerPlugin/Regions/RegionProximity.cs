using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRageMath;

namespace ServerPlugin.Regions;

// One observer's transitions are independent of other players and overlapping regions.
internal sealed class RegionProximity
{
    private enum Location
    {
        Far,
        Near,
        Inside,
    }

    private readonly Dictionary<CombatRegion, Location> locations = new();
    private CombatRegion[] previous;
    public CombatRegion[] Nearby { get; private set; } = Array.Empty<CombatRegion>();

    public void Sample(CombatRegion[] regions, Vector3D position, Action<string> notify)
    {
        if (previous != regions)
        {
            foreach (var old in locations.Keys.Except(regions).ToArray())
                locations.Remove(old);
            previous = regions;
        }
        const int noticeLimit = 3;
        int changes = 0;
        void Announce(string message)
        {
            if (++changes <= noticeLimit)
                notify(message);
        }

        List<(CombatRegion Region, double Distance)> visible = null;
        foreach (var region in regions)
        {
            if (region.Rules == RegionRules.None)
                continue;
            var distance = region.BoundaryDistance(position);
            locations.TryGetValue(region, out var before);
            // Hold the last notice through small position changes on either threshold.
            var now =
                distance <= 0 || (before == Location.Inside && distance < 10) ? Location.Inside
                : distance <= 2000 || (before != Location.Far && distance < 2200) ? Location.Near
                : Location.Far;
            if (now != before)
            {
                if (now == Location.Far)
                    locations.Remove(region);
                else
                    locations[region] = now;
                var rules = region.Rules switch
                {
                    RegionRules.Firing => "Firing blocked",
                    RegionRules.Incoming => "Incoming fire blocked",
                    _ => "Firing and incoming fire blocked",
                };
                if (now == Location.Inside)
                    Announce($"Entered {region.Name} — {rules}");
                else if (before == Location.Inside)
                    Announce($"Left {region.Name}");
                else if (now == Location.Near)
                {
                    string range =
                        distance < 1000
                            ? $"{Math.Ceiling(distance):0} m"
                            : $"{distance / 1000:0.0} km";
                    Announce($"Near {region.Name} ({range}) — {rules}");
                }
            }
            if (Math.Abs(distance) <= 2500)
                (visible ??= new()).Add((region, Math.Abs(distance)));
        }
        if (changes > noticeLimit)
            notify($"+{changes - noticeLimit} more region notices");
        Nearby = visible
            ?.OrderBy(item => item.Distance)
            .Take(4)
            .Select(item => item.Region)
            .ToArray()
            ?? Array.Empty<CombatRegion>();
    }

    // MagnetarMod.RegionBarriers consumes this little-endian snapshot on channel 48733.
    public static byte[] Encode(CombatRegion[] nearby)
    {
        using var stream = new MemoryStream(2 + 56 * nearby.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write((byte)nearby.Length);
        foreach (var region in nearby)
        {
            writer.Write(region.First.X);
            writer.Write(region.First.Y);
            writer.Write(region.First.Z);
            writer.Write(region.Second.X);
            writer.Write(region.Second.Y);
            writer.Write(region.Second.Z);
            writer.Write(region.Radius ?? 0);
        }
        return stream.ToArray();
    }
}
