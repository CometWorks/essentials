using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Groups;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private sealed class GridSaleOffer
    {
        public long BuyerIdentityId { get; set; }
        public long SellerIdentityId { get; set; }
        public string SellerName { get; set; }
        public long Price { get; set; }
        public DateTime ExpiresAt { get; set; }
        public HashSet<long> GridEntityIds { get; } = new HashSet<long>();
    }

    private static readonly Dictionary<long, GridSaleOffer> GridSaleOffers =
        new Dictionary<long, GridSaleOffer>();

    [Command("claim", "Claim a faction-shared looked-at grid group.")]
    [Permission(MyPromoteLevel.None)]
    public void Claim(bool shareWithFaction = false)
    {
        MyCharacter character = GetCallerCharacter("Only players can use this command.");
        if (character == null)
            return;

        long identityId = Context.Caller.IdentityId;
        MyFaction faction = MySession.Static.Factions.TryGetPlayerFaction(identityId) as MyFaction;
        if (faction == null)
        {
            Context.Respond("Not in a faction.");
            return;
        }

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> groups =
            GridGroupFinder.FindLookAtMechanicalGridGroup(character);
        if (
            !TryGetSingleMechanicalGroup(
                groups,
                out MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group,
                out string error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        List<MyCubeGrid> grids = group
            .Nodes.Select(node => node.NodeData)
            .Where(IsUsableGrid)
            .ToList();
        if (
            !TryValidateGridAccess(
                identityId,
                grids,
                out int accessibleBlocks,
                out int ownedBlocks,
                out error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        MyOwnershipShareModeEnum shareMode = shareWithFaction
            ? MyOwnershipShareModeEnum.Faction
            : MyOwnershipShareModeEnum.None;
        foreach (MyCubeGrid grid in grids)
            grid.ChangeGridOwnership(identityId, shareMode);

        Context.Respond(
            $"Claimed {grids.Count:#,##0} grid(s). Accessible owned blocks: {accessibleBlocks:#,##0}/{ownedBlocks:#,##0}."
        );
    }

    [Command("sellgrid", "Offer the looked-at grid group to a nearby player.")]
    [Permission(MyPromoteLevel.None)]
    public void SellGrid(string amount)
    {
        if (!TryParseNonNegativeAmount(amount, out long price, out string error))
        {
            Context.Respond(error);
            return;
        }

        MyCharacter character = GetCallerCharacter("Only players can use this command.");
        if (character == null)
            return;

        if (
            !TryFindNearbySaleTarget(
                character,
                Context.Caller.IdentityId,
                out MyIdentity buyer,
                out error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> groups =
            GridGroupFinder.FindLookAtMechanicalGridGroup(character);
        if (
            !TryGetSingleMechanicalGroup(
                groups,
                out MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group,
                out error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        List<MyCubeGrid> grids = group
            .Nodes.Select(node => node.NodeData)
            .Where(IsUsableGrid)
            .ToList();
        if (!TryValidateGridAccess(Context.Caller.IdentityId, grids, out _, out _, out error))
        {
            Context.Respond(error);
            return;
        }

        RemoveExpiredGridSaleOffers();
        if (GridSaleOffers.ContainsKey(buyer.IdentityId))
        {
            Context.Respond("They already have an offer. Wait or have them use !ess denygrid.");
            return;
        }

        GridSaleOffer offer = new GridSaleOffer
        {
            BuyerIdentityId = buyer.IdentityId,
            SellerIdentityId = Context.Caller.IdentityId,
            SellerName = string.IsNullOrWhiteSpace(Context.Caller.Name)
                ? Utilities.GetPlayerNameById(Context.Caller.IdentityId)
                : Context.Caller.Name,
            Price = price,
            ExpiresAt = DateTime.UtcNow.AddSeconds(30),
        };
        foreach (MyCubeGrid grid in grids)
            offer.GridEntityIds.Add(grid.EntityId);

        GridSaleOffers[buyer.IdentityId] = offer;

        Context.Respond(
            $"Offer to sell {grids.Count:#,##0} grid(s) sent to {buyer.DisplayName} for {FormatCredits(price)} credits. Offer is active for 30 seconds."
        );
        SendPrivateChat(
            buyer.IdentityId,
            $"{offer.SellerName} wants to sell you {grids.Count:#,##0} grid(s) for {FormatCredits(price)} credits. Use !ess acceptgrid within 30 seconds.",
            Color.Cyan
        );
    }

    [Command("denygrid", "Deny a pending grid sale offer.")]
    [Permission(MyPromoteLevel.None)]
    public void DenyGrid()
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
        {
            Context.Respond("Only players can use this command.");
            return;
        }

        if (!TryTakeGridSaleOffer(Context.Caller.IdentityId, out GridSaleOffer offer))
        {
            Context.Respond("You have no offers available to deny.");
            return;
        }

        Context.Respond("Denied the offer.");
        SendPrivateChat(
            offer.SellerIdentityId,
            "Player denied your offer to sell grid.",
            Color.Red
        );
    }

    [Command("acceptgrid", "Accept a pending grid sale offer.")]
    [Permission(MyPromoteLevel.None)]
    public void AcceptGrid()
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
        {
            Context.Respond("Only players can use this command.");
            return;
        }

        if (!TryTakeGridSaleOffer(Context.Caller.IdentityId, out GridSaleOffer offer))
        {
            Context.Respond("You have no offers available to accept.");
            return;
        }

        MyIdentity buyer = MySession.Static.Players.TryGetIdentity(Context.Caller.IdentityId);
        if (buyer == null)
        {
            Context.Respond("Could not find your identity.");
            return;
        }

        if (
            !TryResolveSaleGroups(
                offer,
                out List<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> physicalGroups,
                out string error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        List<MyCubeGrid> grids = physicalGroups
            .SelectMany(group => group.Nodes.Select(node => node.NodeData))
            .Where(IsUsableGrid)
            .ToList();
        if (!TryValidateGridAccess(offer.SellerIdentityId, grids, out _, out _, out error))
        {
            Context.Respond("Seller no longer has access to transfer this grid group: " + error);
            return;
        }

        foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group in physicalGroups)
        {
            ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> single =
                new ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group>();
            single.Add(group);
            if (
                !PcuTransferCoreInstance.TryGetTransferGroup(
                    single,
                    buyer,
                    pcu: true,
                    force: false,
                    out _,
                    out error
                )
            )
            {
                Context.Respond(error);
                return;
            }
        }

        if (
            offer.Price > 0
            && !TryTransferCredits(
                Context.Caller.IdentityId,
                offer.SellerIdentityId,
                offer.Price,
                out error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        foreach (MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group in physicalGroups)
            PcuTransferCoreInstance.Transfer(group, buyer, pcu: true, ownership: true);

        Context.Respond(
            $"Accepted the offer. Transferred {physicalGroups.Count:#,##0} physical grid group(s)."
        );
        SendPrivateChat(
            offer.SellerIdentityId,
            "Player accepted your offer to sell grid.",
            Color.Cyan
        );
    }

    [Command("pcucount", "Count PCU on the looked-at connected grid group.")]
    [Permission(MyPromoteLevel.None)]
    public string PcuCount()
    {
        MyCharacter character = GetCallerCharacter("Only players can use this command.");
        if (character == null)
            return null;

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

        long totalPcu = 0;
        long projectedPcu = 0;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Grid PCU:");
        foreach (
            MyCubeGrid grid in group
                .Nodes.Select(node => node.NodeData)
                .Where(IsUsableGrid)
                .OrderBy(grid => grid.DisplayName)
        )
        {
            totalPcu += grid.BlocksPCU;
            long gridProjectedPcu = CountProjectedPcu(grid);
            projectedPcu += gridProjectedPcu;
            sb.AppendLine($"{grid.DisplayName}: {grid.BlocksPCU:#,##0}");
            if (gridProjectedPcu > 0)
                sb.AppendLine($"  Projection: {gridProjectedPcu:#,##0}");
        }

        sb.AppendLine($"Total grid PCU: {totalPcu:#,##0}");
        if (projectedPcu > 0)
            sb.AppendLine($"Total with projections: {totalPcu + projectedPcu:#,##0}");

        return sb.ToString();
    }

    [Command("rename", "Rename a grid you own or have faction access to.")]
    [Permission(MyPromoteLevel.None)]
    public void RenameGrid(string gridName, params string[] newNameWords)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
        {
            Context.Respond("Only players can use this command.");
            return;
        }

        string newName = string.Join(" ", newNameWords ?? Array.Empty<string>()).Trim();
        if (string.IsNullOrWhiteSpace(gridName) || string.IsNullOrWhiteSpace(newName))
        {
            Context.Respond("Usage: !ess rename <gridName> <newName>");
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

        MyCubeGrid grid = group
            .Nodes.Select(node => node.NodeData)
            .FirstOrDefault(candidate =>
                IsUsableGrid(candidate)
                && (
                    string.Equals(
                        candidate.DisplayName,
                        gridName,
                        StringComparison.InvariantCultureIgnoreCase
                    )
                    || candidate.EntityId.ToString(CultureInfo.InvariantCulture) == gridName
                )
            );
        if (grid == null)
        {
            Context.Respond("Could not find that grid.");
            return;
        }

        if (
            !TryValidateGridAccess(
                Context.Caller.IdentityId,
                new[] { grid },
                out _,
                out _,
                out error
            )
        )
        {
            Context.Respond(error);
            return;
        }

        string oldName = grid.DisplayName;
        grid.ChangeDisplayNameRequest(newName);
        Context.Respond($"Renaming {oldName} to {newName}. You may need to relog to see changes.");
    }

    private static bool TryGetSingleMechanicalGroup(
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group> groups,
        out MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Group group,
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

    private static bool TryValidateGridAccess(
        long identityId,
        IEnumerable<MyCubeGrid> grids,
        out int accessibleBlocks,
        out int ownedBlocks,
        out string error
    )
    {
        accessibleBlocks = 0;
        ownedBlocks = 0;
        foreach (MyCubeGrid grid in grids)
        {
            if (!IsUsableGrid(grid))
                continue;

            if (!grid.Editable || !grid.DestructibleBlocks)
            {
                error = $"Grid '{grid.DisplayName}' is admin protected.";
                return false;
            }

            if (MySession.Static.Factions.GetStationByGridId(grid.EntityId) != null)
            {
                error = "Cannot use this command on economy stations.";
                return false;
            }

            foreach (MySlimBlock block in grid.GetBlocks())
            {
                MyCubeBlock fatBlock = block?.FatBlock;
                if (fatBlock == null || fatBlock.OwnerId <= 0 || !fatBlock.IsFunctional)
                    continue;

                ownedBlocks++;
                MyRelationsBetweenPlayerAndBlock relation = fatBlock.GetUserRelationToOwner(
                    identityId
                );
                if (
                    relation == MyRelationsBetweenPlayerAndBlock.Owner
                    || relation == MyRelationsBetweenPlayerAndBlock.FactionShare
                )
                {
                    accessibleBlocks++;
                    continue;
                }

                error =
                    $"Not enough shared ownership on '{grid.DisplayName}'. Accessible owned blocks: {accessibleBlocks:#,##0}/{ownedBlocks:#,##0}.";
                return false;
            }
        }

        if (ownedBlocks == 0)
        {
            error = "No owned functional blocks found.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryFindNearbySaleTarget(
        MyCharacter sellerCharacter,
        long sellerIdentityId,
        out MyIdentity buyer,
        out string error
    )
    {
        BoundingSphereD sphere = new BoundingSphereD(
            sellerCharacter.PositionComp.GetPosition(),
            10
        );
        List<MyIdentity> nearbyPlayers = MyEntities
            .GetEntitiesInSphere(ref sphere)
            .OfType<MyCharacter>()
            .Where(character => character != sellerCharacter && character.ControlSteamId != 0)
            .Select(character => character.GetPlayerIdentityId())
            .Where(identityId =>
                identityId != 0
                && identityId != sellerIdentityId
                && !MySession.Static.Players.IdentityIsNpc(identityId)
            )
            .Distinct()
            .Select(identityId => MySession.Static.Players.TryGetIdentity(identityId))
            .Where(identity => identity != null)
            .ToList();

        if (nearbyPlayers.Count > 1)
        {
            buyer = null;
            error = "Too many players within 10m.";
            return false;
        }

        if (nearbyPlayers.Count == 0)
        {
            buyer = null;
            error = "No player within 10m to sell grid to.";
            return false;
        }

        buyer = nearbyPlayers[0];
        error = null;
        return true;
    }

    private static bool TryTakeGridSaleOffer(long buyerIdentityId, out GridSaleOffer offer)
    {
        RemoveExpiredGridSaleOffers();
        if (!GridSaleOffers.TryGetValue(buyerIdentityId, out offer))
            return false;

        GridSaleOffers.Remove(buyerIdentityId);
        return true;
    }

    private static void RemoveExpiredGridSaleOffers()
    {
        DateTime now = DateTime.UtcNow;
        foreach (
            long buyerIdentityId in GridSaleOffers
                .Where(pair => pair.Value.ExpiresAt < now)
                .Select(pair => pair.Key)
                .ToList()
        )
            GridSaleOffers.Remove(buyerIdentityId);
    }

    private static bool TryResolveSaleGroups(
        GridSaleOffer offer,
        out List<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups,
        out string error
    )
    {
        groups = new List<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group>();
        HashSet<long> missing = new HashSet<long>(offer.GridEntityIds);

        foreach (
            MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group in MyCubeGridGroups
                .Static
                .Physical
                .Groups
        )
        {
            List<MyCubeGrid> groupGrids = group
                .Nodes.Select(node => node.NodeData)
                .Where(IsUsableGrid)
                .ToList();
            bool containsOfferGrid = groupGrids.Any(grid =>
                offer.GridEntityIds.Contains(grid.EntityId)
            );
            if (!containsOfferGrid)
                continue;

            if (groupGrids.Any(grid => !offer.GridEntityIds.Contains(grid.EntityId)))
            {
                error = "Grid group changed since the offer was created. Canceling purchase.";
                groups = null;
                return false;
            }

            groups.Add(group);
            foreach (MyCubeGrid grid in groupGrids)
                missing.Remove(grid.EntityId);
        }

        if (missing.Count > 0)
        {
            error = "One of the grids does not exist, canceling purchase.";
            groups = null;
            return false;
        }

        error = null;
        return true;
    }

    private static long CountProjectedPcu(MyCubeGrid grid)
    {
        long pcu = 0;
        foreach (MyProjectorBase projector in grid.GetFatBlocks().OfType<MyProjectorBase>())
        {
            if (!projector.Enabled || projector.Clipboard?.PreviewGrids == null)
                continue;

            foreach (MyCubeGrid previewGrid in projector.Clipboard.PreviewGrids)
                pcu += previewGrid.CubeBlocks.Count;
        }

        return pcu;
    }

    private static bool TryParseNonNegativeAmount(
        string inputAmount,
        out long amount,
        out string error
    )
    {
        amount = 0;
        string normalized = (inputAmount ?? string.Empty)
            .Replace(",", string.Empty)
            .Replace(".", string.Empty)
            .Replace(" ", string.Empty);

        if (
            !long.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out amount
            )
        )
        {
            error = "Error parsing into number.";
            return false;
        }

        if (amount < 0)
        {
            error = "No, the price cannot be below 0.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsUsableGrid(MyCubeGrid grid) =>
        grid != null
        && grid.Projector == null
        && !grid.MarkedForClose
        && !grid.MarkedAsTrash
        && grid.InScene;

    private static void SendPrivateChat(long identityId, string message, Color color)
    {
        if (identityId == 0)
            return;

        MyVisualScriptLogicProvider.SendChatMessageColored(
            message,
            color,
            "Essentials",
            identityId,
            MyFontEnum.White
        );
    }
}
