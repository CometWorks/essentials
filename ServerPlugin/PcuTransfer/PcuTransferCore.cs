using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Groups;
using VRage.Network;

namespace ServerPlugin.PcuTransfer;

public sealed class PcuTransferCore
{
    private readonly VanillaLimitsChecker vanillaLimitsChecker = new VanillaLimitsChecker();

    public bool TryGetTransferGroup(
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups,
        MyIdentity newAuthor,
        bool pcu,
        bool force,
        out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        out string error
    )
    {
        if (!TryGetSingleGroup(groups, out group, out error))
            return false;

        if (pcu && !force && !CheckLimits(group, newAuthor, out error))
            return false;

        return true;
    }

    public bool TryGetNobodyTransferGroup(
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups,
        out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        out string error
    ) => TryGetSingleGroup(groups, out group, out error);

    public string Transfer(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        MyIdentity newAuthor,
        bool pcu,
        bool ownership
    )
    {
        if (!pcu && !ownership)
            return "The plugindev did an oopsie and nothing was changed!";

        long newAuthorId = newAuthor.IdentityId;
        HashSet<long> knownIdentities = new HashSet<long>();
        HashSet<long> unknownIdentities = new HashSet<long>();
        List<MyCubeGrid> grids = group.Nodes.Select(node => node.NodeData).ToList();

        foreach (MyCubeGrid grid in grids)
        {
            HashSet<long> authors = new HashSet<long>();
            foreach (MySlimBlock block in new HashSet<MySlimBlock>(grid.GetBlocks()))
            {
                if (block == null || block.CubeGrid == null || block.IsDestroyed)
                    continue;

                if (ownership)
                    TransferBlockOwnership(grid, block.FatBlock, newAuthorId);

                if (!pcu)
                    continue;

                long oldAuthor = block.BuiltBy;
                bool forceTransfer = oldAuthor == 0 || unknownIdentities.Contains(oldAuthor);

                if (
                    !forceTransfer
                    && oldAuthor != newAuthorId
                    && !knownIdentities.Contains(oldAuthor)
                )
                {
                    if (MySession.Static.Players.TryGetIdentity(oldAuthor) == null)
                    {
                        unknownIdentities.Add(oldAuthor);
                        forceTransfer = true;
                    }
                    else
                    {
                        knownIdentities.Add(oldAuthor);
                    }
                }

                if (forceTransfer)
                {
                    block.TransferAuthorshipClient(newAuthorId);
                    block.AddAuthorship();
                }

                authors.Add(oldAuthor);
            }

            foreach (long author in authors)
                MyMultiplayer.RaiseEvent(
                    grid,
                    x => new Action<long, long>(x.TransferBlocksBuiltByID),
                    author,
                    newAuthorId,
                    new EndpointId()
                );
        }

        if (pcu && ownership)
            return $"PCU and Ownership was transferred to {newAuthor.DisplayName}!";

        if (pcu)
            return $"PCU was transferred to {newAuthor.DisplayName}!";

        return $"Ownership was transferred to {newAuthor.DisplayName}!";
    }

    public string TransferNobody(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        bool pcu,
        bool ownership
    )
    {
        if (!pcu && !ownership)
            return "The plugindev did an oopsie and nothing was changed!";

        List<MyCubeGrid> grids = group.Nodes.Select(node => node.NodeData).ToList();

        foreach (MyCubeGrid grid in grids)
        {
            HashSet<long> authors = new HashSet<long>();
            foreach (MySlimBlock block in new HashSet<MySlimBlock>(grid.GetBlocks()))
            {
                if (block == null || block.CubeGrid == null || block.IsDestroyed)
                    continue;

                if (ownership)
                    TransferBlockOwnership(grid, block.FatBlock, 0);

                if (!pcu || block.BuiltBy == 0)
                    continue;

                long oldAuthor = block.BuiltBy;
                block.RemoveAuthorship();
                block.TransferAuthorshipClient(0L);
                authors.Add(oldAuthor);
            }

            foreach (long author in authors)
            {
                MyIdentity identity = MySession.Static.Players.TryGetIdentity(author);
                identity?.BlockLimits.SetAllDirty();
                identity?.BlockLimits.CallLimitsChanged();
            }
        }

        if (pcu && ownership)
            return "PCU and Ownership was removed!";

        if (pcu)
            return "PCU was removed!";

        return "Ownership was removed!";
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

    private bool CheckLimits(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        MyIdentity newAuthor,
        out string error
    )
    {
        List<MySlimBlock> blocks = GetAllBlocksForGroup(group, newAuthor);

        if (
            (
                BlockLimitsPluginBridge.IsAvailableAndEnabled()
                || Plugin.Instance.Config.UseBlockLimitsPlugin
            )
            && BlockLimitsPluginBridge.TryCanAdd(
                blocks,
                newAuthor.IdentityId,
                out bool allowed,
                out List<MySlimBlock> deniedBlocks
            )
        )
        {
            if (allowed)
            {
                error = null;
                return true;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Player '{newAuthor.DisplayName}' does not satisfy BlockLimits Plugin limits!"
            );
            foreach (MySlimBlock deniedBlock in deniedBlocks)
                sb.AppendLine(deniedBlock.BlockDefinition.BlockPairName);

            error = sb.ToString();
            return false;
        }

        LimitCheckResponse response = vanillaLimitsChecker.CheckLimits(blocks, newAuthor);

        if (!response.BlockLimitFine)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Player '{newAuthor.DisplayName}' does not have a high enough Blocklimit!"
            );
            sb.AppendLine("Max: " + response.BlockLimit);
            sb.AppendLine("Built: " + response.CurrentBlocks);
            sb.AppendLine("After Transfer: " + response.BlockLimitAfterTransfer);
            sb.AppendLine("Vertified with: Vanilla Limits");
            error = sb.ToString();
            return false;
        }

        if (!response.PcuFine)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Player '{newAuthor.DisplayName}' does not have a high enough PCU limit!"
            );
            sb.AppendLine("Max: " + response.PcuLimit);
            sb.AppendLine("Built: " + response.CurrentPcu);
            sb.AppendLine("After Transfer: " + response.PcuAfterTransfer);
            sb.AppendLine("Vertified with: Vanilla Limits");
            error = sb.ToString();
            return false;
        }

        if (!response.TypeLimitsFine)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Player '{newAuthor.DisplayName}' does not have high enough Limit for following Block Types:"
            );
            foreach (
                KeyValuePair<
                    Sandbox.Definitions.MyCubeBlockDefinition,
                    int
                > overLimitBlock in response.OverLimitBlocks
            )
                sb.AppendLine(
                    overLimitBlock.Key.BlockPairName + ": " + overLimitBlock.Value + " too many!"
                );

            sb.AppendLine("Vertified with: Vanilla Limits");
            error = sb.ToString();
            return false;
        }

        error = null;
        return true;
    }

    private static List<MySlimBlock> GetAllBlocksForGroup(
        MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        MyIdentity newAuthor
    )
    {
        long authorId = newAuthor.IdentityId;
        List<MySlimBlock> blocks = new List<MySlimBlock>();

        foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Node node in group.Nodes)
        {
            foreach (MySlimBlock block in node.NodeData.GetBlocks())
            {
                if (block.BuiltBy != authorId)
                    blocks.Add(block);
            }
        }

        return blocks;
    }

    private static void TransferBlockOwnership(
        MyCubeGrid grid,
        MyCubeBlock cubeBlock,
        long newOwnerId
    )
    {
        if (cubeBlock == null || cubeBlock.OwnerId == newOwnerId)
            return;

        grid.ChangeOwnerRequest(grid, cubeBlock, 0, MyOwnershipShareModeEnum.Faction);
        if (newOwnerId != 0)
            grid.ChangeOwnerRequest(grid, cubeBlock, newOwnerId, MyOwnershipShareModeEnum.Faction);
    }
}
