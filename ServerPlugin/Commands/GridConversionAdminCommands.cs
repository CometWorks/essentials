using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Groups;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("admin makeship", "Convert a station grid group to ships.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminMakeShip(string gridName = null)
    {
        if (
            !TryResolveConversionGroup(
                gridName,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        int converted = 0;
        foreach (MyCubeGrid grid in GetConversionGrids(group).Where(grid => grid.IsStatic))
        {
            grid.RequestConversionToShip(null);
            converted++;
        }

        return converted == 0
            ? "No station grids found in the target group."
            : $"Converting {converted:#,##0} grid(s) to ships.";
    }

    [Command("admin makestation", "Convert a ship grid group to stations.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminMakeStation(string gridName = null)
    {
        if (
            !TryResolveConversionGroup(
                gridName,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        int converted = 0;
        foreach (MyCubeGrid grid in GetConversionGrids(group).Where(grid => !grid.IsStatic))
        {
            grid.Physics?.ClearSpeed();
            grid.OnConvertedToStationRequest();
            converted++;
        }

        return converted == 0
            ? "No ship grids found in the target group."
            : $"Converting {converted:#,##0} grid(s) to stations.";
    }

    [Command("convert", "Toggle the looked-at grid group between ship and station.")]
    [Permission(MyPromoteLevel.None)]
    public string ConvertGridGroup()
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Only players can use this command.";

        MyCharacter character = GetCallerCharacter("Only players can use this command.");
        if (character == null)
            return null;

        if (
            MyGravityProviderSystem.IsPositionInNaturalGravity(character.PositionComp.GetPosition())
        )
            return "You cannot use this command in natural gravity.";

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups =
            GridGroupFinder.FindLookAtGridGroup(character);
        if (
            !TryGetSingleGroup(
                groups,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        List<MyCubeGrid> grids = GetConversionGrids(group).ToList();
        if (grids.Count == 0)
            return "Could not find usable grids in the target group.";

        if (!TryValidateGridAccess(Context.Caller.IdentityId, grids, out _, out _, out error))
            return error;

        bool hasStatic = grids.Any(grid => grid.IsStatic);
        bool hasDynamic = grids.Any(grid => !grid.IsStatic);
        if (hasStatic && hasDynamic)
            return "Target grid group has mixed ship/station state. Use admin makeship or admin makestation to force it.";

        if (hasStatic)
        {
            foreach (MyCubeGrid grid in grids)
                grid.RequestConversionToShip(null);

            return $"Converting {grids.Count:#,##0} grid(s) to ships.";
        }

        if (grids.Any(grid => grid.GridSizeEnum == MyCubeSize.Small))
            return "Small grids cannot be converted to stations.";

        MyCubeGrid fastGrid = grids.FirstOrDefault(grid =>
            grid.Physics != null && grid.Physics.Speed > 10f
        );
        if (fastGrid != null)
            return $"{fastGrid.DisplayName} is moving too fast to convert.";

        foreach (MyCubeGrid grid in grids)
        {
            grid.Physics?.Clear();
            grid.Physics?.ClearSpeed();
            grid.OnConvertedToStationRequest();
        }

        return $"Converting {grids.Count:#,##0} grid(s) to stations.";
    }

    [Command("admin rename", "Rename a grid without ownership checks.")]
    [Permission(MyPromoteLevel.Admin)]
    public void AdminRenameGrid(string gridName, params string[] newNameWords)
    {
        string newName = string.Join(" ", newNameWords ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(gridName) || string.IsNullOrWhiteSpace(newName))
        {
            Context.Respond("Usage: !ess admin rename <gridName> <newName>");
            return;
        }

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups =
            GridGroupFinder.FindGridGroup(gridName);
        if (
            !TryGetSingleGroup(
                groups,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        MyCubeGrid grid = GetConversionGrids(group).FirstOrDefault();
        if (grid == null)
        {
            Context.Respond("Could not find a usable grid.");
            return;
        }

        string oldName = grid.DisplayName;
        grid.ChangeDisplayNameRequest(newName);
        Context.Respond($"Renaming {oldName} to {newName}. You may need to relog to see changes.");
    }

    [Command("gridtype", "Report whether the target grid group is ships or stations.")]
    [Permission(MyPromoteLevel.None)]
    public string GridType(string gridName = null)
    {
        if (
            !TryResolveConversionGroup(
                gridName,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        StringBuilder sb = new StringBuilder();
        foreach (MyCubeGrid grid in GetConversionGrids(group).OrderBy(grid => grid.DisplayName))
            sb.AppendLine($"{grid.DisplayName}: {(grid.IsStatic ? "STATION" : "SHIP")}");

        return sb.Length == 0 ? "Could not find usable grids in the target group." : sb.ToString();
    }

    private bool TryResolveConversionGroup(
        string gridName,
        out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        out string error
    )
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups;
        if (string.IsNullOrWhiteSpace(gridName))
        {
            MyCharacter character = GetCallerCharacter("Console has to input a grid name.");
            if (character == null)
            {
                group = null;
                error = null;
                return false;
            }

            groups = GridGroupFinder.FindLookAtGridGroup(character);
        }
        else
        {
            groups = GridGroupFinder.FindGridGroup(gridName);
        }

        return TryGetSingleGroup(groups, out group, out error);
    }

    private static IEnumerable<MyCubeGrid> GetConversionGrids(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group
    ) => group.Nodes.Select(node => node.NodeData).Where(IsUsableGrid);
}
