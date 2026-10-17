using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Game.ModAPI;
using VRage.Groups;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("pcu checkowner", "Checks block ownership on a grid.")]
    [Permission(MyPromoteLevel.Moderator)]
    public string CheckOwner(string gridName = null) => CheckGrid(gridName, showAuthors: false);

    [Command("pcu checkauthor", "Checks PCU authorship on a grid.")]
    [Permission(MyPromoteLevel.Moderator)]
    public string CheckAuthor(string gridName = null) => CheckGrid(gridName, showAuthors: true);

    private string CheckGrid(string gridName, bool showAuthors)
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups;

        if (!string.IsNullOrWhiteSpace(gridName))
        {
            groups = GridGroupFinder.FindGridGroup(gridName);
        }
        else
        {
            MyPlayer player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
            if (player?.Character is not MyCharacter character)
                return $"Console has no Character so cannot use this command. Use !ess pcu {(showAuthors ? "checkauthor" : "checkowner")} <gridname> instead!";

            groups = GridGroupFinder.FindLookAtGridGroup(character);
        }

        if (
            !TryGetSingleGroup(
                groups,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        return showAuthors ? BuildAuthorReport(group) : BuildOwnerReport(group);
    }

    private static bool TryGetSingleGroup(
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups,
        out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        out string error
    )
    {
        if (groups.Count < 1)
        {
            group = null;
            error = "Could not find the Grid.";
            return false;
        }

        if (groups.Count > 1)
        {
            group = null;
            error = "Found multiple Grids with same Name. Make sure the name is unique.";
            return false;
        }

        if (!groups.TryPeek(out group))
        {
            error = "Could not work with found grid for unknown reason.";
            return false;
        }

        error = null;
        return true;
    }

    private static string BuildOwnerReport(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group
    )
    {
        StringBuilder sb = new StringBuilder();

        foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Node node in group.Nodes)
        {
            MyCubeGrid grid = node.NodeData;
            if (grid?.Physics == null)
                continue;

            Dictionary<long, int> blocksByOwner = new Dictionary<long, int>();
            foreach (MySlimBlock block in grid.GetBlocks())
            {
                if (block?.CubeGrid == null || block.IsDestroyed || block.FatBlock == null)
                    continue;

                long ownerId = block.FatBlock.OwnerId;
                blocksByOwner[ownerId] = blocksByOwner.TryGetValue(ownerId, out int count)
                    ? count + 1
                    : 1;
            }

            sb.AppendLine("Owners at grid: " + grid.DisplayName);
            foreach (
                KeyValuePair<long, int> pair in blocksByOwner.OrderByDescending(pair => pair.Value)
            )
                sb.AppendLine(
                    "   " + Utilities.GetPlayerNameById(pair.Key) + " = " + pair.Value + " blocks"
                );
        }

        return sb.ToString();
    }

    private static string BuildAuthorReport(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group
    )
    {
        StringBuilder sb = new StringBuilder();

        foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Node node in group.Nodes)
        {
            MyCubeGrid grid = node.NodeData;
            if (grid?.Physics == null)
                continue;

            Dictionary<long, int> pcuByAuthor = new Dictionary<long, int>();
            foreach (MySlimBlock block in grid.GetBlocks())
            {
                if (block?.CubeGrid == null || block.IsDestroyed)
                    continue;

                int pcu = block.BlockDefinition.PCU;
                long authorId = block.BuiltBy;
                pcuByAuthor[authorId] = pcuByAuthor.TryGetValue(authorId, out int total)
                    ? total + pcu
                    : pcu;
            }

            sb.AppendLine("Authors at grid: " + grid.DisplayName);
            foreach (
                KeyValuePair<long, int> pair in pcuByAuthor.OrderByDescending(pair => pair.Value)
            )
                sb.AppendLine(
                    "   " + Utilities.GetPlayerNameById(pair.Key) + " = " + pair.Value + " PCU"
                );
        }

        return sb.ToString();
    }
}
