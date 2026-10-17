using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using ServerPlugin;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Groups;
using VRage.ModAPI;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("entities find", "Find entities with matching text in their name.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public string EntitiesFind(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Usage: !ess entities find <name>";

        List<IMyEntity> matches = MyEntities
            .GetEntities()
            .Cast<IMyEntity>()
            .Where(entity =>
                EntitySearchName(entity).IndexOf(name, StringComparison.InvariantCultureIgnoreCase)
                >= 0
            )
            .OrderBy(EntitySearchName)
            .ToList();

        if (matches.Count == 0)
            return $"Found 0 entities matching '{name}'.";

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Count:#,##0} entities:");
        foreach (IMyEntity entity in matches)
            sb.AppendLine($"{EntitySearchName(entity)} ({entity.EntityId})");

        return sb.ToString();
    }

    [Command("entities stop", "Stop an entity from moving.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void EntitiesStop(string entityName)
    {
        if (!TryResolveEntity(entityName, out IMyEntity entity))
            return;

        entity.Physics?.ClearSpeed();
        Context.Respond($"Entity '{EntityDisplayName(entity)}' stopped.");
    }

    [Command("entities delete", "Delete an entity after confirmation.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void EntitiesDelete(string entityName)
    {
        if (!TryResolveEntity(entityName, out IMyEntity entity))
            return;

        if (entity is IMyCharacter)
        {
            Context.Respond("You cannot delete characters.");
            return;
        }

        string operation =
            "entities delete:" + entity.EntityId.ToString(CultureInfo.InvariantCulture);
        if (
            !ConfirmMaintenance(
                operation,
                $"This will delete entity '{EntityDisplayName(entity)}' ({entity.EntityId}). Run the same command again within 30 seconds to confirm."
            )
        )
            return;

        entity.Close();
        Context.Respond($"Entity '{EntityDisplayName(entity)}' deleted.");
        Plugin.Instance?.Log.Info(
            "Deleted entity {0}: {1}",
            entity.EntityId,
            EntityDisplayName(entity)
        );
    }

    [Command("entities poweroff", "Power off generators on a grid.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void EntitiesPowerOff(string name) => SetGridPower(name, enabled: false);

    [Command("entities poweron", "Power on generators on a grid.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void EntitiesPowerOn(string name) => SetGridPower(name, enabled: true);

    [Command("entities eject", "Eject one player from a seat, or all seated players with 'all'.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EntitiesEject(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            Context.Respond("Usage: !ess entities eject <playerName|all>");
            return;
        }

        if (string.Equals(playerName, "all", StringComparison.InvariantCultureIgnoreCase))
        {
            int count = EjectAllSeatedPlayers();
            Context.Respond($"Ejected {count:#,##0} player(s) from their seats.");
            return;
        }

        if (EjectPlayer(playerName))
            Context.Respond($"Player '{playerName}' ejected.");
        else
            Context.Respond(
                $"Player '{playerName}' is not online/seated, or could not be found seated offline."
            );
    }

    [Command("grids list", "List grids owned by the caller.")]
    [Permission(MyPromoteLevel.None)]
    public string GridsList()
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Console cannot list player-owned grids.";

        long identityId = Context.Caller.IdentityId;
        List<MyCubeGrid> grids = MyEntities
            .GetEntities()
            .OfType<MyCubeGrid>()
            .Where(grid => IsActiveRealGrid(grid) && grid.BigOwners.Contains(identityId))
            .OrderBy(grid => grid.DisplayName)
            .ToList();

        if (grids.Count == 0)
            return "You own no grids.";

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Ships/stations owned by {Utilities.GetPlayerNameById(identityId)}:");
        foreach (MyCubeGrid grid in grids)
        {
            string position =
                Plugin.Instance?.Config?.UtilityShowPosition == true
                    ? grid.PositionComp.GetPosition().ToString()
                    : "Unknown";
            sb.AppendLine(
                $"{grid.DisplayName} - {grid.GridSizeEnum} - {grid.BlocksCount:#,##0} block(s) - Position {position}"
            );
            AddGridGps(identityId, grid);
        }

        return sb.ToString();
    }

    [Command("grids ejectall", "Eject all pilots from a named or targeted mechanical grid group.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void GridsEjectAll(string gridName = null)
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> groups;

        if (string.IsNullOrWhiteSpace(gridName))
        {
            MyCharacter character = GetCallerCharacter(
                "Console has no Character so cannot use this command. Use !ess grids ejectall <gridname> instead!"
            );
            if (character == null)
                return;

            groups = GridGroupFinder.FindLookAtMechanicalGridGroup(character);
            if (groups.Count == 0)
            {
                Context.Respond(
                    "No grid in your line of sight found. Remember to not use spectator."
                );
                return;
            }
        }
        else
        {
            groups = GridGroupFinder.FindMechanicalGridGroup(gridName);
            if (groups.Count == 0)
            {
                Context.Respond($"Grid with name '{gridName}' was not found.");
                return;
            }

            if (groups.Count > 1)
            {
                Context.Respond($"Found multiple grids with name '{gridName}'. Rename one first.");
                return;
            }
        }

        int count = EjectGridGroupPilots(groups.First());
        Context.Respond($"Ejected {count:#,##0} player(s) from their seats.");
    }

    [Command("grids stopall", "Stop all non-projected grids from moving.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public string GridsStopAll()
    {
        int count = 0;
        foreach (
            MyCubeGrid grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(IsActiveRealGrid)
        )
        {
            if (grid.Physics == null)
                continue;

            grid.Physics.ClearSpeed();
            count++;
        }

        return $"Stopped {count:#,##0} grid(s).";
    }

    [Command("grids static large", "Convert all large ship grids to stations after confirmation.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void GridsStaticLarge()
    {
        List<MyCubeGrid> grids = MyEntities
            .GetEntities()
            .OfType<MyCubeGrid>()
            .Where(grid =>
                IsActiveRealGrid(grid) && grid.GridSizeEnum == MyCubeSize.Large && !grid.IsStatic
            )
            .ToList();

        if (grids.Count == 0)
        {
            Context.Respond("No large ship grids found.");
            return;
        }

        if (
            !ConfirmMaintenance(
                "grids static large",
                $"This will convert {grids.Count:#,##0} large ship grid(s) to stations. Run the same command again within 30 seconds to confirm."
            )
        )
            return;

        int count = 0;
        foreach (MyCubeGrid grid in grids)
        {
            grid.OnConvertedToStationRequest();
            count++;
        }

        Context.Respond($"Converted {count:#,##0} large grid(s) to stations.");
        Plugin.Instance?.Log.Info("Converted {0} large grids to stations", count);
    }

    private bool TryResolveEntity(string name, out IMyEntity entity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            entity = null;
            Context.Respond(
                "Usage: !ess entities <find|stop|delete|poweroff|poweron|eject> <name>"
            );
            return false;
        }

        if (Utilities.TryGetEntityByNameOrId(name, out entity))
            return true;

        Context.Respond($"Entity '{name}' not found.");
        return false;
    }

    private bool TryResolveGrid(string name, out MyCubeGrid grid)
    {
        grid = null;
        if (!TryResolveEntity(name, out IMyEntity entity))
            return false;

        if (entity is MyCubeGrid cubeGrid)
        {
            grid = cubeGrid;
            return true;
        }

        Context.Respond($"Entity '{EntityDisplayName(entity)}' is not a grid.");
        return false;
    }

    private void SetGridPower(string name, bool enabled)
    {
        if (!TryResolveGrid(name, out MyCubeGrid grid))
            return;

        List<MyFunctionalBlock> blocks = grid.GetFatBlocks()
            .OfType<MyFunctionalBlock>()
            .Where(IsPowerProducer)
            .ToList();

        int changed = 0;
        foreach (MyFunctionalBlock block in blocks)
        {
            if (block.Enabled == enabled)
                continue;

            block.Enabled = enabled;
            changed++;
        }

        string state = enabled ? "Enabled" : "Disabled";
        Context.Respond(
            $"{state} {changed:#,##0} of {blocks.Count:#,##0} power block(s) on '{grid.DisplayName}'."
        );
    }

    private static bool IsPowerProducer(MyFunctionalBlock block) =>
        block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Reactor)
        || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_BatteryBlock)
        || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel)
        || block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_FueledPowerProducer);

    private static int EjectAllSeatedPlayers()
    {
        int count = 0;
        foreach (MyShipController controller in AllShipControllers())
        {
            if (controller.Pilot == null)
                continue;

            controller.Use();
            count++;
        }

        return count;
    }

    private static bool EjectPlayer(string playerName)
    {
        if (Utilities.GetPlayerByNameOrId(playerName) is MyPlayer player)
        {
            if (player.Controller?.ControlledEntity is MyShipController controller)
            {
                controller.Use();
                return true;
            }

            return false;
        }

        foreach (MyShipController controller in AllShipControllers())
        {
            MyCharacter pilot = controller.Pilot;
            if (
                pilot == null
                || !string.Equals(
                    pilot.DisplayName,
                    playerName,
                    StringComparison.InvariantCultureIgnoreCase
                )
            )
                continue;

            controller.Use();
            return true;
        }

        return false;
    }

    private static int EjectGridGroupPilots(
        MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group
    )
    {
        int count = 0;
        foreach (MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Node node in group.Nodes)
        {
            foreach (
                MyShipController controller in node
                    .NodeData.GetFatBlocks()
                    .OfType<MyShipController>()
            )
            {
                if (controller.Pilot == null)
                    continue;

                controller.Use();
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<MyShipController> AllShipControllers() =>
        MyEntities
            .GetEntities()
            .OfType<MyCubeGrid>()
            .Where(IsActiveRealGrid)
            .SelectMany(grid => grid.GetFatBlocks().OfType<MyShipController>());

    private static bool IsActiveRealGrid(MyCubeGrid grid) =>
        grid != null
        && grid.Projector == null
        && !grid.MarkedForClose
        && !grid.MarkedAsTrash
        && grid.InScene;

    private static string EntitySearchName(IMyEntity entity)
    {
        if (entity is IMyVoxelBase voxel && !string.IsNullOrWhiteSpace(voxel.StorageName))
            return voxel.StorageName;

        return EntityDisplayName(entity);
    }

    private static string EntityDisplayName(IMyEntity entity) =>
        string.IsNullOrWhiteSpace(entity?.DisplayName)
            ? entity?.EntityId.ToString(CultureInfo.InvariantCulture) ?? "<null>"
            : entity.DisplayName;

    private static void AddGridGps(long identityId, MyCubeGrid grid)
    {
        if (Plugin.Instance?.Config?.MarkerShowPosition != true)
            return;

        var gps = MyAPIGateway.Session?.GPS.Create(
            grid.DisplayName,
            $"{grid.DisplayName} - {grid.GridSizeEnum} - {grid.BlocksCount:#,##0} block(s)",
            grid.PositionComp.GetPosition(),
            true
        );

        if (gps != null)
            MyAPIGateway.Session?.GPS.AddGps(identityId, gps);
    }
}
