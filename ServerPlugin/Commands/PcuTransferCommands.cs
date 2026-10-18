using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PluginSdk.Commands;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using ServerPlugin.PcuTransfer;
using VRage.Game.ModAPI;
using VRage.Groups;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private static readonly Dictionary<string, DateTime> PcuTransferConfirmations =
        new Dictionary<string, DateTime>();
    private static readonly PcuTransferCore PcuTransferCoreInstance = new PcuTransferCore();
    private const int ConfirmationSeconds = 30;

    private void TransferToPlayer(
        string playerName,
        string gridName,
        bool pcu,
        bool ownership,
        bool force
    )
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            Context.Respond("Correct Usage is !ess transfer <playerName> [gridName]");
            return;
        }

        MyIdentity author = Utilities.GetIdentityByNameOrIds(playerName) as MyIdentity;
        if (author == null)
        {
            Context.Respond("Player not Found!");
            return;
        }

        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups;
        string confirmGridName = gridName;

        if (string.IsNullOrWhiteSpace(gridName))
        {
            MyCharacter character = GetCallerCharacter(
                "Console has no Character so cannot use this command. Use !ess transfer <playerName> <gridname> instead!"
            );
            if (character == null)
                return;

            groups = GridGroupFinder.FindLookAtGridGroup(character);
            confirmGridName = "nogrid_" + pcu + "_" + ownership;
        }
        else
        {
            groups = GridGroupFinder.FindGridGroup(gridName);
        }

        string confirmationKey =
            $"{PcuTransferPlayerKey()}:{confirmGridName}:{author.IdentityId}:{pcu}:{ownership}:{force}";
        if (!CheckConfirmation(confirmationKey, groups, author, pcu, force))
            return;

        try
        {
            if (
                !PcuTransferCoreInstance.TryGetTransferGroup(
                    groups,
                    author,
                    pcu,
                    force,
                    out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                    out string error
                )
            )
            {
                Context.Respond(error);
                return;
            }

            Context.Respond(StartMessage(author.DisplayName, pcu, ownership));
            Context.Respond(PcuTransferCoreInstance.Transfer(group, author, pcu, ownership));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Error on transferring ship");
            Context.Respond("Error Transferring Ship!");
        }
    }

    private void TransferToNobody(string gridName, bool pcu, bool ownership)
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups;
        string confirmGridName = gridName;

        if (string.IsNullOrWhiteSpace(gridName))
        {
            MyCharacter character = GetCallerCharacter(
                "Console has no Character so cannot use this command. Use !ess transfernobody <gridname> instead!"
            );
            if (character == null)
                return;

            groups = GridGroupFinder.FindLookAtGridGroup(character);
            confirmGridName = "nogrid_" + pcu + "_" + ownership;
        }
        else
        {
            groups = GridGroupFinder.FindGridGroup(gridName);
        }

        string confirmationKey =
            $"{PcuTransferPlayerKey()}:{confirmGridName}:0:{pcu}:{ownership}:false";
        if (!CheckConfirmationNobody(confirmationKey, groups))
            return;

        try
        {
            if (
                !PcuTransferCoreInstance.TryGetNobodyTransferGroup(
                    groups,
                    out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                    out string error
                )
            )
            {
                Context.Respond(error);
                return;
            }

            Context.Respond(StartNobodyMessage(pcu, ownership));
            Context.Respond(PcuTransferCoreInstance.TransferNobody(group, pcu, ownership));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Error on transferring ship");
            Context.Respond("Error Transferring Ship!");
        }
    }

    private bool CheckConfirmation(
        string confirmationKey,
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups,
        MyIdentity author,
        bool pcu,
        bool force
    )
    {
        DateTime now = DateTime.UtcNow;
        if (
            PcuTransferConfirmations.TryGetValue(confirmationKey, out DateTime expiresAt)
            && expiresAt >= now
        )
        {
            PcuTransferConfirmations.Remove(confirmationKey);
            return true;
        }

        if (
            !PcuTransferCoreInstance.TryGetTransferGroup(
                groups,
                author,
                pcu,
                force,
                out _,
                out string error
            )
        )
        {
            Context.Respond(error);
            return false;
        }

        PcuTransferConfirmations[confirmationKey] = now.AddSeconds(ConfirmationSeconds);
        Context.Respond(
            "Are you sure you want to continue? Enter the command again within 30 seconds to confirm."
        );
        return false;
    }

    private bool CheckConfirmationNobody(
        string confirmationKey,
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups
    )
    {
        DateTime now = DateTime.UtcNow;
        if (
            PcuTransferConfirmations.TryGetValue(confirmationKey, out DateTime expiresAt)
            && expiresAt >= now
        )
        {
            PcuTransferConfirmations.Remove(confirmationKey);
            return true;
        }

        if (!PcuTransferCoreInstance.TryGetNobodyTransferGroup(groups, out _, out string error))
        {
            Context.Respond(error);
            return false;
        }

        PcuTransferConfirmations[confirmationKey] = now.AddSeconds(ConfirmationSeconds);
        Context.Respond(
            "Are you sure you want to continue? Enter the command again within 30 seconds to confirm."
        );
        return false;
    }

    private MyCharacter GetCallerCharacter(string consoleMessage)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
        {
            Context.Respond(consoleMessage);
            return null;
        }

        MyPlayer player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
        if (player?.Character is MyCharacter character)
            return character;

        Context.Respond(
            "You have no Character currently. Make sure to spawn and be out of cockpit!"
        );
        return null;
    }

    private string PcuTransferPlayerKey()
    {
        if (Context.Caller.SteamId != 0)
            return Context.Caller.SteamId.ToString();

        if (Context.Caller.IdentityId != 0)
            return Context.Caller.IdentityId.ToString();

        return "console";
    }

    private static string StartMessage(string playerName, bool pcu, bool ownership)
    {
        if (pcu && ownership)
            return "Start transferring PCU and Ownership to " + playerName + "!";

        if (pcu)
            return "Start transferring PCU to " + playerName + "!";

        return "Start transferring Ownership to " + playerName + "!";
    }

    private static string StartNobodyMessage(bool pcu, bool ownership)
    {
        if (pcu && ownership)
            return "Start transferring PCU and Ownership to nobody!";

        if (pcu)
            return "Start transferring PCU to nobody!";

        return "Start transferring Ownership to nobody!";
    }
}

public sealed partial class EssentialsModule
{
    [Command("transfer", "Transfers PCU and ownership to a player.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void Transfer(string playerName = null, string gridName = null) =>
        TransferToPlayer(playerName, gridName, pcu: true, ownership: true, force: false);

    [Command("forcetransfer", "Transfers PCU and ownership to a player, ignoring limits.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void ForceTransfer(string playerName = null, string gridName = null) =>
        TransferToPlayer(playerName, gridName, pcu: true, ownership: true, force: true);

    [Command("transferpcu", "Transfers PCU to a player.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TransferPcu(string playerName = null, string gridName = null) =>
        TransferToPlayer(playerName, gridName, pcu: true, ownership: false, force: false);

    [Command("forcetransferpcu", "Transfers PCU to a player, ignoring limits.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void ForceTransferPcu(string playerName = null, string gridName = null) =>
        TransferToPlayer(playerName, gridName, pcu: true, ownership: false, force: true);

    [Command("transferowner", "Transfers ownership to a player.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TransferOwner(string playerName = null, string gridName = null) =>
        TransferToPlayer(playerName, gridName, pcu: false, ownership: true, force: false);

    [Command("transfernobody", "Removes PCU and ownership.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TransferNobody(string gridName = null) =>
        TransferToNobody(gridName, pcu: true, ownership: true);

    [Command("transferpcunobody", "Removes PCU.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TransferPcuNobody(string gridName = null) =>
        TransferToNobody(gridName, pcu: true, ownership: false);

    [Command("transferownernobody", "Removes ownership.")]
    [Permission(MyPromoteLevel.SpaceMaster)]
    public void TransferOwnerNobody(string gridName = null) =>
        TransferToNobody(gridName, pcu: false, ownership: true);
}
