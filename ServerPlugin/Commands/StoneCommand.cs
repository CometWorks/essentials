using System;
using System.Collections.Generic;
using System.Linq;
using PluginSdk.Commands;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Groups;

namespace ServerPlugin.Commands;

[CommandRoot("stone", "Stone Cleanup", "Remove Stone ore from an owned grid")]
public sealed class StoneCommand : CommandModule
{
    private static readonly Dictionary<long, DateTime> Cooldowns = new Dictionary<long, DateTime>();
    private static MyFixedPoint totalDeleted;

    [Command("", "Remove Stone ore from the controlled or looked-at grid group.")]
    [Permission(MyPromoteLevel.None)]
    public void DeleteStone(bool outputCount = false)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
        {
            Context.Respond("Only players can use !stone.");
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (
            Cooldowns.TryGetValue(Context.Caller.IdentityId, out DateTime expiresAt)
            && expiresAt > now
        )
        {
            int remainingSeconds = (int)Math.Ceiling((expiresAt - now).TotalSeconds);
            Context.Respond($"Command is still on cooldown for {remainingSeconds} seconds.");
            return;
        }

        MyPlayer player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
        if (player == null)
        {
            Context.Respond("Player not found.");
            return;
        }

        List<MyCubeGrid> grids;
        if (player.Controller?.ControlledEntity is MyCockpit cockpit)
        {
            grids = new List<MyCubeGrid> { cockpit.CubeGrid };
        }
        else
        {
            if (player.Character == null)
            {
                Context.Respond("You have no character or controlled grid.");
                return;
            }

            if (
                !GridGroupFinder
                    .FindLookAtGridGroup(player.Character)
                    .TryPeek(out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group)
            )
            {
                Context.Respond("No grid found in your line of sight.");
                return;
            }

            grids = group.Nodes.Select(node => node.NodeData).Where(IsUsableGrid).ToList();
        }

        Cooldowns[Context.Caller.IdentityId] = now.AddSeconds(
            Plugin.Instance.PluginConfig.StoneCooldownInSeconds
        );

        MyFixedPoint deleted = 0;
        int skippedGrids = 0;
        foreach (MyCubeGrid grid in grids.Where(IsUsableGrid).Distinct())
        {
            if (!IsOwnerOrFactionOwned(grid, Context.Caller.IdentityId))
            {
                skippedGrids++;
                continue;
            }

            foreach (
                MyCubeBlock block in grid.GetFatBlocks().Where(block => block?.HasInventory == true)
            )
            {
                for (
                    int inventoryIndex = 0;
                    inventoryIndex < block.InventoryCount;
                    inventoryIndex++
                )
                    deleted += DeleteStone(block.GetInventory(inventoryIndex));
            }
        }

        if (deleted == 0)
            Cooldowns.Remove(Context.Caller.IdentityId);

        Context.Respond(
            $"{deleted} Stone Deleted"
                + (skippedGrids > 0 ? $"; skipped {skippedGrids} unowned grid(s)." : ".")
        );

        totalDeleted += deleted;
        if (outputCount)
            Context.Respond($"{totalDeleted} Stone Deleted in total on this instance.");
    }

    private static MyFixedPoint DeleteStone(MyInventory inventory)
    {
        MyFixedPoint deleted = 0;
        List<MyPhysicalInventoryItem> items = inventory.GetItems();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            MyPhysicalInventoryItem item = items[i];
            if (
                item.Content == null
                || item.Content.TypeId != typeof(MyObjectBuilder_Ore)
                || !string.Equals(item.Content.SubtypeName, "Stone", StringComparison.Ordinal)
            )
                continue;

            deleted += item.Amount;
            inventory.RemoveItemsAt(i, item.Amount, sendEvent: true, spawn: false);
        }

        return deleted;
    }

    private static bool IsUsableGrid(MyCubeGrid grid) =>
        grid != null
        && grid.Projector == null
        && !grid.MarkedForClose
        && !grid.MarkedAsTrash
        && grid.InScene;

    private static bool IsOwnerOrFactionOwned(MyCubeGrid grid, long identityId)
    {
        if (grid.BigOwners.Contains(identityId))
            return true;

        long ownerId = grid.BigOwners.FirstOrDefault(owner => owner != 0);
        if (ownerId == 0)
            return false;

        IMyFaction ownerFaction = MySession.Static.Factions.TryGetPlayerFaction(ownerId);
        IMyFaction playerFaction = MySession.Static.Factions.TryGetPlayerFaction(identityId);
        return ownerFaction != null && ownerFaction == playerFaction;
    }
}
