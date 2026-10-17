using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PluginSdk.Commands;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Groups;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command(
        "protect",
        "Protect a named or looked-at mechanical grid group from damage and/or editing."
    )]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void ProtectGrid(params string[] arguments)
    {
        if (
            !TryParseProtectionArguments(
                arguments,
                out string gridName,
                out bool allowDamage,
                out bool allowEdit
            )
        )
            return;

        if (
            !TryResolveProtectionGroup(
                "protect",
                gridName,
                out MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group
            )
        )
            return;

        List<MyCubeGrid> grids = group
            .Nodes.Select(node => node.NodeData)
            .Where(IsUsableGrid)
            .ToList();
        if (grids.Count == 0)
        {
            Context.Respond("Could not find a usable grid in that mechanical group.");
            return;
        }

        foreach (MyCubeGrid grid in grids)
        {
            grid.DestructibleBlocks = allowDamage;
            grid.Editable = allowEdit;
        }

        Context.Respond(
            $"Protected {grids.Count} grid(s): damage {(allowDamage ? "allowed" : "blocked")}, "
                + $"editing {(allowEdit ? "allowed" : "blocked")}."
        );
    }

    [Command(
        "unprotect",
        "Remove damage and edit protection from a named or looked-at mechanical grid group."
    )]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void UnprotectGrid(params string[] gridNameWords)
    {
        string gridName = string.Join(" ", gridNameWords ?? Array.Empty<string>()).Trim();
        if (
            !TryResolveProtectionGroup(
                "unprotect",
                gridName,
                out MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group
            )
        )
            return;

        List<MyCubeGrid> grids = group
            .Nodes.Select(node => node.NodeData)
            .Where(IsUsableGrid)
            .ToList();
        if (grids.Count == 0)
        {
            Context.Respond("Could not find a usable grid in that mechanical group.");
            return;
        }

        foreach (MyCubeGrid grid in grids)
        {
            grid.DestructibleBlocks = true;
            grid.Editable = true;
        }

        Context.Respond($"Unprotected {grids.Count} grid(s): damage and editing allowed.");
    }

    private bool TryParseProtectionArguments(
        string[] arguments,
        out string gridName,
        out bool allowDamage,
        out bool allowEdit
    )
    {
        allowDamage = false;
        allowEdit = false;
        List<string> gridNameWords = new List<string>();

        foreach (string argument in arguments ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            if (string.Equals(argument, "-allowDamage", StringComparison.OrdinalIgnoreCase))
            {
                allowDamage = true;
                continue;
            }

            if (string.Equals(argument, "-allowEdit", StringComparison.OrdinalIgnoreCase))
            {
                allowEdit = true;
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                gridName = null;
                Context.Respond(
                    $"Unknown option '{argument}'. Usage: !ess protect [gridName] [-allowDamage] [-allowEdit]"
                );
                return false;
            }

            gridNameWords.Add(argument);
        }

        gridName = string.Join(" ", gridNameWords).Trim();
        return true;
    }

    private bool TryResolveProtectionGroup(
        string command,
        string gridName,
        out MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group
    )
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> groups;
        if (string.IsNullOrWhiteSpace(gridName))
        {
            MyCharacter character = GetCallerCharacter(
                $"Console has no Character so cannot use this command. Use !ess {command} <gridName> instead!"
            );
            if (character == null)
            {
                group = null;
                return false;
            }

            groups = GridGroupFinder.FindLookAtMechanicalGridGroup(character);
        }
        else
        {
            groups = GridGroupFinder.FindMechanicalGridGroup(gridName);
        }

        if (TryGetSingleMechanicalGroup(groups, out group, out string error))
            return true;

        Context.Respond(error);
        return false;
    }
}
