using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginSdk.Commands;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Groups;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    private static readonly MyDefinitionId SpaceCreditDefinitionId = new MyDefinitionId(
        typeof(MyObjectBuilder_PhysicalObject),
        "SpaceCredit"
    );

    [Command("TestEconSync", "Refresh the caller's local economy account.")]
    [Permission(MyPromoteLevel.Admin)]
    public string TestEconSync()
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Only players can use this command.";

        return RefreshEconomyAccount(Context.Caller.IdentityId)
            ? "Refreshed your balance account."
            : "Account cannot be found.";
    }

    [Command("fulleconsync", "Refresh all local player economy accounts.")]
    [Permission(MyPromoteLevel.Admin)]
    public string FullEconSyncLower() => FullEconSync();

    [Command("FullEconSync", "Refresh all local player economy accounts.")]
    [Permission(MyPromoteLevel.Admin)]
    public string FullEconSync()
    {
        int count = 0;
        foreach (long identityId in GetPlayerAccountIdentityIds())
        {
            if (RefreshEconomyAccount(identityId))
                count++;
        }

        return $"Refreshed {count:#,##0} local balance account(s).";
    }

    [Command("SingleEconSync", "Refresh one local player economy account.")]
    [Permission(MyPromoteLevel.Admin)]
    public string SingleEconSync(string playerNameOrSteamId) =>
        SingleEconSyncLower(playerNameOrSteamId);

    [Command("singleeconsync", "Refresh one local player economy account.")]
    [Permission(MyPromoteLevel.Admin)]
    public string SingleEconSyncLower(string playerNameOrSteamId)
    {
        IMyIdentity identity = Utilities.GetIdentityByNameOrIds(playerNameOrSteamId);
        if (identity == null)
            return "Could not find that player.";

        return RefreshEconomyAccount(identity.IdentityId)
            ? $"Refreshed balance account for {identity.DisplayName}."
            : "Account cannot be found.";
    }

    [Command("econ debug account", "Show account state for a player or faction tag.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EconDebugAccount(string playerOrFaction)
    {
        if (MySession.Static.Factions.TryGetFactionByTag(playerOrFaction) is MyFaction faction)
            return BuildAccountDebug(faction.Name, faction.FactionId);

        IMyIdentity identity = Utilities.GetIdentityByNameOrIds(playerOrFaction);
        return identity == null
            ? "Could not find that player or faction."
            : BuildAccountDebug(identity.DisplayName, identity.IdentityId);
    }

    [Command("econ sync all", "Refresh all local player economy accounts.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EconSyncAll() => FullEconSync();

    [Command("econ sync player", "Refresh one local player economy account.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EconSyncPlayer(string playerNameOrSteamId) =>
        SingleEconSyncLower(playerNameOrSteamId);

    [Command(
        "eco resetbalances",
        "Reset player balances to the starting balance and faction balances to zero."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoResetBalances()
    {
        int count = ResetPlayerAccountsToStartingBalance();
        count += ResetFactionAccountsToZero();
        return $"{count:#,##0} balance(s) reset.";
    }

    [Command("eco resetplayers", "Reset player balances to the starting balance.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoResetPlayers() =>
        $"{ResetPlayerAccountsToStartingBalance():#,##0} player balance(s) reset.";

    [Command("eco resetfactions", "Reset faction balances to zero.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoResetFactions() =>
        $"{ResetFactionAccountsToZero():#,##0} faction balance(s) reset.";

    [Command("eco resetplayer", "Set a player's balance to zero.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoResetPlayer(string playerNameOrId)
    {
        IMyIdentity identity = Utilities.GetIdentityByNameOrIds(playerNameOrId);
        if (identity == null)
            return "Could not find that player.";

        long before = MyBankingSystem.GetBalance(identity.IdentityId);
        if (before < 0)
            return "Account cannot be found.";

        SetBankAccountBalance(identity.IdentityId, 0);
        long after = MyBankingSystem.GetBalance(identity.IdentityId);
        return $"{identity.DisplayName} balance before change: {FormatCredits(before)}\n{identity.DisplayName} balance after change: {FormatCredits(after)}";
    }

    [Command("eco resetfac", "Set a faction balance to zero.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoResetFaction(string tag)
    {
        MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
        if (faction == null)
            return "Error faction not found.";

        long before = MyBankingSystem.GetBalance(faction.FactionId);
        if (before < 0)
            return "Faction account cannot be found.";

        SetBankAccountBalance(faction.FactionId, 0);
        long after = MyBankingSystem.GetBalance(faction.FactionId);
        return $"{faction.Name} faction balance before change: {FormatCredits(before)}\n{faction.Name} faction balance after change: {FormatCredits(after)}";
    }

    [Command("eco givefac", "Add credits to a faction account.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EcoGiveFactionLegacy(string tag, string inputAmount)
    {
        if (!TryParsePositiveAmount(inputAmount, out long amount, out string error))
        {
            Context.Respond(error);
            return;
        }

        ChangeFactionBalance(tag, amount);
    }

    [Command("eco takefac", "Remove credits from a faction account.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EcoTakeFactionLegacy(string tag, string inputAmount)
    {
        if (!TryParsePositiveAmount(inputAmount, out long amount, out string error))
        {
            Context.Respond(error);
            return;
        }

        ChangeFactionBalance(tag, -amount);
    }

    [Command("eco deposit", "Deposit physical SpaceCredit items into your account.")]
    [Permission(MyPromoteLevel.None)]
    public string EcoDeposit(bool playerOwnedOnly = false)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Console cannot deposit money.";

        if (MyBankingSystem.GetBalance(Context.Caller.IdentityId) < 0)
            return "Account cannot be found.";

        MyPlayer player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
        if (player?.Character is not MyCharacter character)
            return "You have no character.";

        if (
            !TryGetLookAtEconomyGridGroup(
                character,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        long deposited = 0;
        if (character.GetInventory() is MyInventory characterInventory)
            deposited += DepositCreditsFromInventory(characterInventory);

        foreach (MyCubeGrid grid in GetConversionGrids(group))
        {
            if (!GridIsOwnerOrFactionOwned(grid, Context.Caller.IdentityId))
                continue;

            foreach (
                MyCubeBlock block in grid.GetFatBlocks().Where(block => block?.HasInventory == true)
            )
            {
                if (!CanUseCreditInventory(block, Context.Caller.IdentityId, playerOwnedOnly))
                    continue;

                if (block.GetInventory() is MyInventory inventory)
                    deposited += DepositCreditsFromInventory(inventory);
            }
        }

        if (deposited <= 0)
            return "No SpaceCredit items found.";

        MyBankingSystem.ChangeBalance(Context.Caller.IdentityId, deposited);
        Plugin.Instance?.Log.Info(
            "{0} deposited {1} SpaceCredit items",
            Context.Caller.SteamId,
            deposited
        );
        return $"Deposited: {FormatCredits(deposited)}";
    }

    [Command(
        "eco withdraw",
        "Withdraw credits as physical SpaceCredit items into a cargo container."
    )]
    [Permission(MyPromoteLevel.None)]
    public string EcoWithdraw(long amount)
    {
        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Console cannot withdraw money.";

        if (amount <= 0)
            return "Amount must be positive.";

        if (amount >= int.MaxValue)
            return "Keen code does not allow stacks over 2.147 billion; try a smaller number.";

        long balance = MyBankingSystem.GetBalance(Context.Caller.IdentityId);
        if (balance < 0)
            return "Account cannot be found.";

        if (balance < amount)
            return "You do not have that much money.";

        MyPlayer player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
        if (player?.Character is not MyCharacter character)
            return "You have no character.";

        if (
            !TryGetLookAtEconomyGridGroup(
                character,
                out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
                out string error
            )
        )
            return error;

        MyFixedPoint fixedAmount = (MyFixedPoint)(int)amount;
        foreach (MyCubeGrid grid in GetConversionGrids(group))
        {
            if (!GridIsOwnerOrFactionOwned(grid, Context.Caller.IdentityId))
                continue;

            foreach (
                MyCargoContainer cargo in grid.GetFatBlocks()
                    .OfType<MyCargoContainer>()
                    .Where(cargo => cargo.IsFunctional)
            )
            {
                if (
                    !CanUseCreditInventory(cargo, Context.Caller.IdentityId, playerOwnedOnly: false)
                )
                    continue;

                MyInventory inventory = cargo.GetInventory() as MyInventory;
                if (
                    inventory == null
                    || !inventory.CanItemsBeAdded(fixedAmount, SpaceCreditDefinitionId)
                )
                    continue;

                inventory.AddItems(fixedAmount, CreateSpaceCreditObjectBuilder());
                inventory.Refresh();
                MyBankingSystem.ChangeBalance(Context.Caller.IdentityId, -amount);
                Plugin.Instance?.Log.Info(
                    "{0} withdrew {1} SpaceCredit items to {2}",
                    Context.Caller.SteamId,
                    amount,
                    cargo.CubeGrid?.DisplayName
                );
                return $"Added credits to {cargo.DisplayNameText} in grid {cargo.CubeGrid?.DisplayName}.";
            }
        }

        return "No accessible cargo container has free space for that many credits.";
    }

    [Command("eco withdrawall", "Disabled legacy full withdrawal command.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoWithdrawAll() => "Command disabled. Withdraw with an amount.";

    private static bool RefreshEconomyAccount(long ownerId)
    {
        long balance = MyBankingSystem.GetBalance(ownerId);
        if (balance < 0)
            return false;

        SetBankAccountBalance(ownerId, balance);
        return true;
    }

    private static string BuildAccountDebug(string displayName, long accountId)
    {
        if (
            MyBankingSystem.Static == null
            || !MyBankingSystem.Static.TryGetAccountInfo(accountId, out MyAccountInfo account)
        )
            return $"{displayName} ({accountId}) account not found.";

        return $"{displayName} ({accountId}) balance: {FormatCredits(account.Balance)}";
    }

    private static IEnumerable<long> GetPlayerAccountIdentityIds() =>
        MySession
            .Static.Players.GetAllIdentities()
            .Where(identity => !MySession.Static.Players.IdentityIsNpc(identity.IdentityId))
            .Select(identity => identity.IdentityId)
            .Where(identityId => MyBankingSystem.GetBalance(identityId) >= 0);

    private static int ResetPlayerAccountsToStartingBalance()
    {
        int count = 0;
        foreach (long identityId in GetPlayerAccountIdentityIds().ToList())
        {
            SetBankAccountBalance(
                identityId,
                MyBankingSystem.BankingSystemDefinition.StartingBalance
            );
            count++;
        }

        return count;
    }

    private static int ResetFactionAccountsToZero()
    {
        int count = 0;
        foreach (MyFaction faction in MySession.Static.Factions.Select(pair => pair.Value))
        {
            SetBankAccountBalance(faction.FactionId, 0);
            count++;
        }

        return count;
    }

    private static void SetBankAccountBalance(long ownerId, long balance)
    {
        if (MyBankingSystem.Static == null)
            return;

        MyBankingSystem.Static.RemoveAccount(ownerId);
        MyBankingSystem.Static.CreateAccount(ownerId, balance);
    }

    private bool TryGetLookAtEconomyGridGroup(
        MyCharacter character,
        out MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group group,
        out string error
    )
    {
        ConcurrentBag<MyGroups<MyCubeGrid, MyGridPhysicalGroupData>.Group> groups =
            GridGroupFinder.FindLookAtGridGroup(character);
        if (!TryGetSingleGroup(groups, out group, out error))
            return false;

        return true;
    }

    private static bool GridIsOwnerOrFactionOwned(MyCubeGrid grid, long identityId)
    {
        if (grid.BigOwners.Contains(identityId))
            return true;

        long ownerId = GetGridPrimaryOwner(grid);
        if (ownerId == 0)
            return false;

        MyFaction ownerFaction =
            MySession.Static.Factions.TryGetPlayerFaction(ownerId) as MyFaction;
        MyFaction playerFaction =
            MySession.Static.Factions.TryGetPlayerFaction(identityId) as MyFaction;
        return ownerFaction != null
            && playerFaction != null
            && ownerFaction.FactionId == playerFaction.FactionId;
    }

    private static bool CanUseCreditInventory(
        MyCubeBlock block,
        long identityId,
        bool playerOwnedOnly
    )
    {
        MyRelationsBetweenPlayerAndBlock relation = block.GetUserRelationToOwner(identityId);
        if (relation == MyRelationsBetweenPlayerAndBlock.Owner)
            return true;

        return !playerOwnedOnly && relation == MyRelationsBetweenPlayerAndBlock.FactionShare;
    }

    private static long DepositCreditsFromInventory(MyInventory inventory)
    {
        long deposited = 0;
        List<MyPhysicalInventoryItem> items = inventory.GetItems();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            MyPhysicalInventoryItem item = items[i];
            if (!IsSpaceCreditItem(item))
                continue;

            long amount = FixedPointToWholeLong(item.Amount);
            if (amount <= 0)
                continue;

            inventory.RemoveItemsAt(i, item.Amount, sendEvent: true, spawn: false);
            deposited += amount;
        }

        if (deposited > 0)
            inventory.Refresh();

        return deposited;
    }

    private static bool IsSpaceCreditItem(MyPhysicalInventoryItem item) =>
        item.Content != null
        && string.Equals(
            item.Content.TypeId.ToString(),
            "MyObjectBuilder_PhysicalObject",
            StringComparison.InvariantCulture
        )
        && string.Equals(
            item.Content.SubtypeName,
            "SpaceCredit",
            StringComparison.InvariantCulture
        );

    private static long FixedPointToWholeLong(MyFixedPoint amount)
    {
        decimal value = decimal.Truncate((decimal)amount);
        if (value <= 0)
            return 0;

        if (value > long.MaxValue)
            return long.MaxValue;

        return (long)value;
    }

    private static MyObjectBuilder_PhysicalObject CreateSpaceCreditObjectBuilder() =>
        new MyObjectBuilder_PhysicalObject { SubtypeName = "SpaceCredit" };
}
