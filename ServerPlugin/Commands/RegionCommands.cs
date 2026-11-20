using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Game.Multiplayer;
using ServerPlugin.Regions;
using VRage.Game.ModAPI;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command(
        "region sphere",
        "Create a sphere at a GPS point or your position. Radius is in kilometres."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string RegionSphere(
        string name,
        double radiusKm,
        RegionRules rules = RegionRules.Peace,
        string center = "here"
    ) =>
        WithRegions(store =>
        {
            var point = RegionPoint(center);
            return store.Change(
                name,
                create: new CombatRegion(name, rules, point, point, radiusKm * 1000)
            );
        });

    [Command("region box", "Create an axis-aligned box between two GPS points.")]
    [Permission(MyPromoteLevel.Admin)]
    public string RegionBox(
        string name,
        string corner1,
        string corner2,
        RegionRules rules = RegionRules.Peace
    ) =>
        WithRegions(store =>
            store.Change(
                name,
                create: new CombatRegion(
                    name,
                    rules,
                    RegionPoint(corner1),
                    RegionPoint(corner2),
                    null
                )
            )
        );

    [Command("region rules", "Set a region's rules to firing, incoming, peace, or none.")]
    [Permission(MyPromoteLevel.Admin)]
    public string RegionSetRules(string name, RegionRules rules) =>
        WithRegions(store => store.Change(name, edit: rules));

    [Command("region delete", "Remove a named region from this world.")]
    [Permission(MyPromoteLevel.Admin)]
    public string RegionDelete(string name) => WithRegions(store => store.Change(name));

    [Command("region list", "List all regions in this world with their dimensions and rules.")]
    [Permission(MyPromoteLevel.Admin)]
    public string RegionList() => WithRegions(store => DescribeRegions(store.Snapshot));

    [Command("region inspect", "Show regions and restrictions at a GPS point or your position.")]
    [Permission(MyPromoteLevel.Admin)]
    public string RegionInspect(string point = "here") =>
        WithRegions(store =>
        {
            var position = RegionPoint(point);
            return DescribeRegions(store.Snapshot.Where(r => r.Contains(position)));
        });

    private Vector3D RegionPoint(string input)
    {
        if (string.Equals(input, "here", StringComparison.OrdinalIgnoreCase))
        {
            if (
                !Context.Caller.IsConsole
                && Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId)?.Character
                    is { } character
            )
                return character.PositionComp.GetPosition();
            throw new ArgumentException(
                "Supply a GPS point or run this command in game with a character."
            );
        }
        Vector3D position = default;
        if (MyGpsCollection.ParseOneGPS(input, new StringBuilder(), ref position))
            return position;
        throw new ArgumentException("Use here or a GPS marker (quote markers containing spaces).");
    }

    private static string DescribeRegions(
        System.Collections.Generic.IEnumerable<CombatRegion> regions
    )
    {
        var lines = regions
            .Select(r =>
                r.Radius is double radius
                    ? FormattableString.Invariant(
                        $"{r.Name} [{r.Rules}] sphere at {Gps(r.First)}, radius {radius / 1000:R} km"
                    )
                    : $"{r.Name} [{r.Rules}] box from {Gps(r.First)} to {Gps(r.Second)}"
            )
            .ToArray();
        return lines.Length == 0 ? "No matching regions." : string.Join("\n", lines);
    }

    private static string Gps(Vector3D p) =>
        string.Format(CultureInfo.InvariantCulture, "GPS:Region:{0:R}:{1:R}:{2:R}:", p.X, p.Y, p.Z);

    private static string WithRegions(Func<RegionStore, string> action)
    {
        if (Plugin.Instance?.PluginConfig?.Enabled != true)
            return "Essentials is disabled.";
        var store = RegionRuntime.Store;
        if (store == null)
            return RegionRuntime.Unavailable;
        try
        {
            return action(store);
        }
        catch (ArgumentException error)
        {
            return error.Message;
        }
        catch (System.Xml.XmlException)
        {
            return "The name contains invalid characters.";
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Plugin.Instance.Log.Warning(error, "Could not save world regions");
            return "Could not save the region change. See the server log.";
        }
    }
}
