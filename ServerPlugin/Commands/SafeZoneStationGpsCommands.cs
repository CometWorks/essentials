using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using ServerPlugin;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private static readonly HashSet<string> ProtectedStationTags = new HashSet<string>(
        StringComparer.InvariantCultureIgnoreCase
    )
    {
        "ACME",
        "UNIN",
        "FEDR",
        "CONS",
    };

    [Command("zone", "Add or remove a player/faction from a nearby safe zone.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EditZone(string addOrRemove, string playerOrFaction, string nameOrTag)
    {
        bool add =
            (addOrRemove ?? string.Empty).IndexOf(
                "add",
                StringComparison.InvariantCultureIgnoreCase
            ) >= 0;
        bool remove =
            (addOrRemove ?? string.Empty).IndexOf(
                "remove",
                StringComparison.InvariantCultureIgnoreCase
            ) >= 0;
        if (add == remove)
            return "Could not read input. Use add or remove.";

        bool player =
            (playerOrFaction ?? string.Empty).IndexOf(
                "player",
                StringComparison.InvariantCultureIgnoreCase
            ) >= 0;
        bool factionInput =
            (playerOrFaction ?? string.Empty).IndexOf(
                "fac",
                StringComparison.InvariantCultureIgnoreCase
            ) >= 0;
        if (player == factionInput)
            return "Could not read input. Use player or fac.";

        MyCharacter character = GetCallerCharacter(
            "Only players can edit the nearest safe zone. Move near the zone and run the command in game."
        );
        if (character == null)
            return null;

        MySafeZone zone = FindNearestSafeZone(character.PositionComp.GetPosition(), 500);
        if (zone == null)
            return "Cannot find a safe zone within 500m.";

        if (player)
        {
            IMyIdentity identity = Utilities.GetIdentityByNameOrIds(nameOrTag);
            if (identity == null)
                return "Could not find that player.";

            zone.Players.Remove(identity.IdentityId);
            if (add)
                zone.Players.Add(identity.IdentityId);
        }
        else
        {
            MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(nameOrTag);
            if (faction == null)
                return "Could not find that faction.";

            zone.Factions.RemoveAll(existing => existing?.FactionId == faction.FactionId);
            if (add)
                zone.Factions.Add(faction);
        }

        MySessionComponentSafeZones.UpdateSafeZone(
            (MyObjectBuilder_SafeZone)zone.GetObjectBuilder(),
            true
        );
        return add ? "Added." : "Removed.";
    }

    [Command("place station", "Register an NPC economy station at the caller position.")]
    [Permission(MyPromoteLevel.Admin)]
    public string PlaceStation(string npcTag, string type)
    {
        if (MySession.Static.Factions.TryGetFactionByTag(npcTag) is not MyFaction faction)
            return "Could not find that faction.";

        MyCharacter character = GetCallerCharacter("Command has to be run from in game.");
        if (character == null)
            return null;

        if (
            !TryResolveStationType(
                type,
                out MyDefinitionId stationListId,
                out MyStationTypeEnum stationType,
                out string error
            )
        )
            return error;

        MyStationsListDefinition stationDefinition =
            MyDefinitionManager.Static.GetDefinition<MyStationsListDefinition>(stationListId);
        string stationName = GetRandomStationName(stationDefinition);
        Vector3D position = character.PositionComp.GetPosition();
        MyFactionStation station = new MyFactionStation(
            MyEntityIdentifier.AllocateId(
                MyEntityIdentifier.ID_OBJECT_TYPE.STATION,
                MyEntityIdentifier.ID_ALLOCATION_METHOD.RANDOM
            ),
            stationName,
            position,
            stationType,
            faction,
            stationName,
            stationDefinition?.GeneratedItemsContainerType
        );

        faction.AddStation(station);
        Plugin.Instance?.Log.Info(
            "Registered economy station {0} for faction {1} at {2}",
            stationName,
            faction.Tag,
            position
        );
        return $"Registered {stationType} station '{stationName}' for {faction.Tag} at {position}.";
    }

    [Command(
        "fixallstations",
        "Delete duplicate NPC station grids around registered stations after confirmation."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public void FixAllStations()
    {
        List<MyCubeGrid> duplicates = new List<MyCubeGrid>();
        foreach (MyFaction faction in MySession.Static.Factions.Select(pair => pair.Value))
        {
            foreach (IMyFactionStation station in faction.Stations)
                duplicates.AddRange(
                    FindDuplicateStationGrids(station.Position, 250, station.StationEntityId)
                );
        }

        duplicates = duplicates.Distinct().Where(grid => !IsProtectedStationGrid(grid)).ToList();

        if (duplicates.Count == 0)
        {
            Context.Respond("No duplicate station grids found.");
            return;
        }

        if (
            !ConfirmMaintenance(
                "fixallstations",
                $"This will delete {duplicates.Count:#,##0} duplicate station grid(s). Run the command again within 30 seconds to confirm."
            )
        )
            return;

        int deleted = DeleteStationGrids(duplicates);
        Context.Respond($"Deleted {deleted:#,##0} duplicate station grid(s).");
        Plugin.Instance?.Log.Info(
            "Deleted {0} duplicate station grids with fixallstations",
            deleted
        );
    }

    [Command(
        "fixstation",
        "Delete duplicate NPC station grids near the caller after confirmation."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public void FixStation()
    {
        MyCharacter character = GetCallerCharacter("Only players can run nearby station cleanup.");
        if (character == null)
            return;

        Vector3D position = character.PositionComp.GetPosition();
        List<MyCubeGrid> candidates = FindNpcStationCandidateGrids(position, 500);
        if (candidates.Count == 0)
        {
            Context.Respond("Cannot find nearby NPC station grids.");
            return;
        }

        MySafeZone zone = FindNearestSafeZone(position, 500);
        if (zone == null)
        {
            List<MyCubeGrid> unzoned = candidates
                .Where(grid => !IsProtectedStationGrid(grid))
                .ToList();
            if (unzoned.Count == 0)
            {
                Context.Respond("Only protected station grids found.");
                return;
            }

            if (
                !ConfirmMaintenance(
                    "fixstation:nozone:"
                        + Context.Caller.IdentityId.ToString(CultureInfo.InvariantCulture),
                    $"This will delete {unzoned.Count:#,##0} nearby NPC station grid(s). Run the command again within 30 seconds to confirm."
                )
            )
                return;

            int deleted = DeleteStationGrids(unzoned);
            Context.Respond($"Deleted {deleted:#,##0} nearby NPC station grid(s).");
            Plugin.Instance?.Log.Info(
                "Deleted {0} nearby NPC station grids without safe zone",
                deleted
            );
            return;
        }

        MyCubeGrid original = candidates.FirstOrDefault(grid =>
            MySession.Static.Factions.GetStationByGridId(grid.EntityId) != null
            && Vector3D.DistanceSquared(
                grid.PositionComp.GetPosition(),
                zone.PositionComp.GetPosition()
            ) < 1
        );
        if (original == null)
        {
            Context.Respond("Could not find the registered station grid.");
            return;
        }

        List<MyCubeGrid> duplicates = candidates
            .Where(grid => grid.EntityId != original.EntityId && !IsProtectedStationGrid(grid))
            .ToList();
        if (duplicates.Count == 0)
        {
            Context.Respond("Cannot find a duplicate station.");
            return;
        }

        if (
            !ConfirmMaintenance(
                "fixstation:" + original.EntityId.ToString(CultureInfo.InvariantCulture),
                $"Ensure no player grids are connected. This will delete {duplicates.Count:#,##0} duplicate station grid(s). Run the command again within 30 seconds to confirm."
            )
        )
            return;

        int duplicateCount = DeleteStationGrids(duplicates);
        Context.Respond($"Deleted {duplicateCount:#,##0} duplicate station grid(s).");
        Plugin.Instance?.Log.Info(
            "Deleted {0} duplicate station grids near {1}",
            duplicateCount,
            original.DisplayName
        );
    }

    [Command("isecon", "Report whether nearby NPC station grids are economy stations.")]
    [Permission(MyPromoteLevel.None)]
    public string IsEconomyStation() => BuildNearbyStationEconomyReport();

    [Command("sywavefix", "Report whether nearby NPC station grids are economy stations.")]
    [Permission(MyPromoteLevel.Admin)]
    public string SyWaveFix() => BuildNearbyStationEconomyReport();

    [Command("ez hide", "Hide GPS signals whose names contain the input.")]
    [Permission(MyPromoteLevel.None)]
    public string HideGps(params string[] nameWords) => SetGpsVisibility(false, nameWords);

    [Command("ez show", "Show GPS signals whose names contain the input.")]
    [Permission(MyPromoteLevel.None)]
    public string ShowGps(params string[] nameWords) => SetGpsVisibility(true, nameWords);

    [Command("ez delete", "Delete GPS signals whose names contain the input.")]
    [Permission(MyPromoteLevel.None)]
    public string DeleteGps(params string[] nameWords)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Only players can use this command.";

        string filter = string.Join(" ", nameWords ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(filter))
            return "Usage: !ess ez delete <name|all>";

        List<IMyGps> gpsList =
            MyAPIGateway.Session?.GPS.GetGpsList(Context.Caller.IdentityId) ?? new List<IMyGps>();
        List<IMyGps> matches = gpsList.Where(gps => GpsMatches(gps, filter)).ToList();
        foreach (IMyGps gps in matches)
            MyAPIGateway.Session?.GPS.RemoveGps(Context.Caller.IdentityId, gps);

        return $"Deleting {matches.Count:#,##0} signal(s).";
    }

    private string SetGpsVisibility(bool visible, string[] nameWords)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Only players can use this command.";

        string filter = string.Join(" ", nameWords ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(filter))
            return visible ? "Usage: !ess ez show <name|all>" : "Usage: !ess ez hide <name|all>";

        List<IMyGps> gpsList =
            MyAPIGateway.Session?.GPS.GetGpsList(Context.Caller.IdentityId) ?? new List<IMyGps>();
        int count = 0;
        foreach (IMyGps gps in gpsList.Where(gps => GpsMatches(gps, filter)))
        {
            gps.ShowOnHud = visible;
            MyAPIGateway.Session?.GPS.ModifyGps(Context.Caller.IdentityId, gps);
            count++;
        }

        return visible ? $"Showing {count:#,##0} signal(s)." : $"Hiding {count:#,##0} signal(s).";
    }

    private string BuildNearbyStationEconomyReport()
    {
        MyCharacter character = GetCallerCharacter("Only players can scan nearby stations.");
        if (character == null)
            return null;

        List<MyCubeGrid> grids = FindNpcStationCandidateGrids(
            character.PositionComp.GetPosition(),
            500
        );
        if (grids.Count == 0)
            return "No nearby NPC station grids with store blocks found.";

        StringBuilder sb = new StringBuilder();
        foreach (MyCubeGrid grid in grids.OrderBy(grid => grid.DisplayName))
        {
            bool registered = MySession.Static.Factions.GetStationByGridId(grid.EntityId) != null;
            sb.AppendLine(
                $"{grid.DisplayName}: {(registered ? "economy station" : "not an economy station")}"
            );
        }

        return sb.ToString();
    }

    private static bool GpsMatches(IMyGps gps, string filter) =>
        string.Equals(filter, "all", StringComparison.InvariantCultureIgnoreCase)
        || (gps?.Name ?? string.Empty).IndexOf(filter, StringComparison.InvariantCultureIgnoreCase)
            >= 0;

    private static MySafeZone FindNearestSafeZone(Vector3D position, double radius)
    {
        BoundingSphereD sphere = new BoundingSphereD(position, radius);
        return MyAPIGateway
            .Entities.GetEntitiesInSphere(ref sphere)
            .OfType<MySafeZone>()
            .OrderBy(zone => Vector3D.DistanceSquared(zone.PositionComp.GetPosition(), position))
            .FirstOrDefault();
    }

    private static List<MyCubeGrid> FindDuplicateStationGrids(
        Vector3D position,
        double radius,
        long registeredGridId
    ) =>
        FindNpcStationCandidateGrids(position, radius)
            .Where(grid =>
                grid.EntityId != registeredGridId
                && MySession.Static.Factions.GetStationByGridId(grid.EntityId) == null
            )
            .ToList();

    private static List<MyCubeGrid> FindNpcStationCandidateGrids(Vector3D position, double radius)
    {
        BoundingSphereD sphere = new BoundingSphereD(position, radius);
        return MyAPIGateway
            .Entities.GetEntitiesInSphere(ref sphere)
            .OfType<MyCubeGrid>()
            .Where(IsUsableGrid)
            .Where(IsNpcStoreGrid)
            .ToList();
    }

    private static bool IsNpcStoreGrid(MyCubeGrid grid)
    {
        MyFaction faction = GetGridOwnerFaction(grid);
        return faction != null
            && !string.IsNullOrWhiteSpace(faction.Tag)
            && faction.Tag.Length > 3
            && grid.GetFatBlocks().OfType<MyStoreBlock>().Any();
    }

    private static bool IsProtectedStationGrid(MyCubeGrid grid)
    {
        MyFaction faction = GetGridOwnerFaction(grid);
        return faction != null && ProtectedStationTags.Contains(faction.Tag);
    }

    private static MyFaction GetGridOwnerFaction(MyCubeGrid grid)
    {
        long ownerId = GetGridPrimaryOwner(grid);
        return ownerId == 0
            ? null
            : MySession.Static.Factions.TryGetPlayerFaction(ownerId) as MyFaction;
    }

    private static long GetGridPrimaryOwner(MyCubeGrid grid)
    {
        if (grid?.BigOwners == null)
            return 0;

        return grid.BigOwners.FirstOrDefault(owner => owner != 0);
    }

    private static int DeleteStationGrids(IEnumerable<MyCubeGrid> grids)
    {
        int deleted = 0;
        foreach (
            MyCubeGrid grid in grids
                .Distinct()
                .Where(grid => grid != null && !grid.MarkedForClose)
                .ToList()
        )
        {
            foreach (MyShipConnector connector in grid.GetFatBlocks().OfType<MyShipConnector>())
                connector.TryDisconnect();

            grid.Close();
            deleted++;
        }

        return deleted;
    }

    private static bool TryResolveStationType(
        string type,
        out MyDefinitionId definitionId,
        out MyStationTypeEnum stationType,
        out string error
    )
    {
        switch ((type ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "spacestations":
            case "spacestation":
            case "space":
                definitionId = new MyDefinitionId(
                    typeof(MyObjectBuilder_StationsListDefinition),
                    "SpaceStations"
                );
                stationType = MyStationTypeEnum.SpaceStation;
                error = null;
                return true;
            case "orbitalstations":
            case "orbitalstation":
            case "orbital":
                definitionId = new MyDefinitionId(
                    typeof(MyObjectBuilder_StationsListDefinition),
                    "OrbitalStations"
                );
                stationType = MyStationTypeEnum.OrbitalStation;
                error = null;
                return true;
            case "outposts":
            case "outpost":
                definitionId = new MyDefinitionId(
                    typeof(MyObjectBuilder_StationsListDefinition),
                    "Outposts"
                );
                stationType = MyStationTypeEnum.Outpost;
                error = null;
                return true;
            case "miningstations":
            case "miningstation":
            case "mining":
                definitionId = new MyDefinitionId(
                    typeof(MyObjectBuilder_StationsListDefinition),
                    "MiningStations"
                );
                stationType = MyStationTypeEnum.MiningStation;
                error = null;
                return true;
            default:
                definitionId = default(MyDefinitionId);
                stationType = default(MyStationTypeEnum);
                error =
                    "Cannot find that type. Use SpaceStations, OrbitalStations, Outposts or MiningStations.";
                return false;
        }
    }

    private static string GetRandomStationName(MyStationsListDefinition stationDefinition)
    {
        if (stationDefinition?.StationNames == null || stationDefinition.StationNames.Count == 0)
            return "Economy_SpaceStation_1";

        int index = MyUtils.GetRandomInt(0, stationDefinition.StationNames.Count);
        return stationDefinition.StationNames[index].ToString();
    }
}
