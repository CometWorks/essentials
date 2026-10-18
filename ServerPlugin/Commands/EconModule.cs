using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PluginSdk.Commands;
using Sandbox.Game.GameSystems.BankingAndCurrency;
using Sandbox.Game.World;
using VRage.Game.ModAPI;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("econ give", "Add credits to a player's account. Use '*' to affect all players.")]
    [Permission(MyPromoteLevel.Admin)]
    public void Give(string player, long amount, bool onlyOnline = false, bool excludeNpcs = true)
    {
        if (
            !TryFindPlayerIdentities(
                player,
                onlyOnline,
                excludeNpcs,
                out List<long> foundIdentities
            )
        )
        {
            Context.Respond("Player cannot be found!");
            return;
        }

        int changedIdentities = 0;
        foreach (long identityId in foundIdentities)
        {
            ChangeBalance(identityId, amount);
            changedIdentities++;
        }

        Context.Respond($"{amount:#,##0} credits given to {changedIdentities:#,##0} account(s)");
    }

    [Command("econ take", "Take credits from a player's account. Use '*' to affect all players.")]
    [Permission(MyPromoteLevel.Admin)]
    public void Take(string player, long amount, bool onlyOnline = false, bool excludeNpcs = true)
    {
        if (
            !TryFindPlayerIdentities(
                player,
                onlyOnline,
                excludeNpcs,
                out List<long> foundIdentities
            )
        )
        {
            Context.Respond("Player cannot be found!");
            return;
        }

        int changedIdentities = 0;
        foreach (long identityId in foundIdentities)
        {
            ChangeBalance(identityId, -amount);
            changedIdentities++;
        }

        Context.Respond($"{amount:#,##0} credits taken from {changedIdentities:#,##0} account(s)");
    }

    [Command("econ set", "Set a player's account balance. Use '*' to affect all players.")]
    [Permission(MyPromoteLevel.Admin)]
    public void Set(string player, long amount, bool onlyOnline = false, bool excludeNpcs = true)
    {
        if (
            !TryFindPlayerIdentities(
                player,
                onlyOnline,
                excludeNpcs,
                out List<long> foundIdentities
            )
        )
        {
            Context.Respond("Player cannot be found!");
            return;
        }

        int changedIdentities = 0;
        foreach (long identityId in foundIdentities)
        {
            long balance = MyBankingSystem.GetBalance(identityId);
            ChangeBalance(identityId, amount - balance);
            changedIdentities++;
        }

        Context.Respond($"Balance(s) set to {amount:#,##0} on {changedIdentities:#,##0} accounts");
    }

    [Command(
        "econ reset",
        "Reset credits in a player's account to 10,000. Use '*' to affect all players."
    )]
    [Permission(MyPromoteLevel.Admin)]
    public void Reset(string player, bool onlyOnline = false, bool excludeNpcs = true) =>
        Set(player, 10_000, onlyOnline, excludeNpcs);

    [Command("econ top", "Return player balances sorted highest to lowest.")]
    [Permission(MyPromoteLevel.None)]
    public string Top(bool onlyOnline = false, bool excludeNpcs = true)
    {
        return BuildPlayerBalanceReport(
            "Summary of balances across the server",
            null,
            onlyOnline,
            excludeNpcs
        );
    }

    [Command("econ check", "Check a player's balance.")]
    [Permission(MyPromoteLevel.None)]
    public string Check(string player)
    {
        IMyIdentity identity = Utilities.GetIdentityByNameOrIds(player);
        if (identity == null)
            return "Player cannot be found!";

        long balance = MyBankingSystem.GetBalance(identity.IdentityId);
        return $"{identity.DisplayName}'s balance is {balance:#,##0} credits";
    }

    [Command("econ pay", "Pay another online player from your account.")]
    [Permission(MyPromoteLevel.None)]
    public string Pay(string player, long amount)
    {
        if (amount <= 0)
            return "Amount cannot be negative";

        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Console cannot execute this command";

        IMyPlayer target = Utilities.GetPlayerByNameOrId(player);
        if (target == null)
            return "Player is not online or cannot be found!";

        long fromIdentityId = Context.Caller.IdentityId;
        long toIdentityId = target.Identity.IdentityId;

        if (fromIdentityId == toIdentityId)
            return "You cannot pay yourself!";

        if (!TryTransferCredits(fromIdentityId, toIdentityId, amount, out string error))
            return error;

        return $"Sent {amount:#,##0} credits to {target.DisplayName}.";
    }

    [Command("eco", "List economy alias commands.")]
    [Permission(MyPromoteLevel.None)]
    public string EcoHelp()
    {
        return string.Join(
            "\n",
            "Economy commands:",
            "!ess eco balance player <player>",
            "!ess eco balance faction <tag>",
            "!ess eco top [limit] [factions]",
            "!ess eco give player <player|*> <amount>",
            "!ess eco give faction <tag> <amount>",
            "!ess eco take player <player|*> <amount>",
            "!ess eco take faction <tag> <amount>",
            "!ess eco pay player <player> <amount>",
            "!ess eco pay faction <tag> <amount>",
            "!ess eco deposit [playerOwnedOnly]",
            "!ess eco withdraw <amount>",
            "!ess eco resetplayer <player>",
            "!ess eco resetfac <tag>",
            "Native aliases: !ess econ give/take/set/reset/top/check/pay"
        );
    }

    [Command("eco balance", "Show a player or faction balance.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoBalance(string type, string target)
    {
        switch (NormalizeEcoType(type))
        {
            case "player":
                return Check(target);
            case "faction":
                MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(target);
                return faction == null
                    ? "Cant find that faction"
                    : $"{faction.Name} Faction Balance : {FormatCredits(MyBankingSystem.GetBalance(faction.FactionId))}";
            default:
                return "Incorrect usage, example: !ess eco balance player <player> or !ess eco balance faction <tag>";
        }
    }

    [Command("eco top", "Return top player or faction balances.")]
    [Permission(MyPromoteLevel.Admin)]
    public string EcoTop(int limit = 30, bool factions = false)
    {
        if (limit <= 0)
            return "Limit must be greater than 0.";

        return factions
            ? BuildFactionBalanceReport($"Top {limit:#,##0} faction balances", limit)
            : BuildPlayerBalanceReport(
                $"Top {limit:#,##0} player balances",
                limit,
                onlyOnline: false,
                excludeNpcs: true
            );
    }

    [Command("eco give", "Economy alias for econ give, with faction support.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EcoGive(string type, string recipient, string inputAmount)
    {
        if (!TryParsePositiveAmount(inputAmount, out long amount, out string error))
        {
            Context.Respond(error);
            return;
        }

        switch (NormalizeEcoType(type))
        {
            case "player":
                Give(recipient, amount);
                return;
            case "faction":
                ChangeFactionBalance(recipient, amount);
                return;
            default:
                Context.Respond(
                    "Incorrect usage, example: !ess eco give player <player|*> <amount> or !ess eco give faction <tag> <amount>"
                );
                return;
        }
    }

    [Command("eco take", "Economy alias for econ take, with faction support.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EcoTake(string type, string recipient, string inputAmount)
    {
        if (!TryParsePositiveAmount(inputAmount, out long amount, out string error))
        {
            Context.Respond(error);
            return;
        }

        switch (NormalizeEcoType(type))
        {
            case "player":
                Take(recipient, amount);
                return;
            case "faction":
                ChangeFactionBalance(recipient, -amount);
                return;
            default:
                Context.Respond(
                    "Incorrect usage, example: !ess eco take player <player|*> <amount> or !ess eco take faction <tag> <amount>"
                );
                return;
        }
    }

    [Command("eco pay", "Economy alias for econ pay, with faction support.")]
    [Permission(MyPromoteLevel.None)]
    public string EcoPay(string type, string recipient, string inputAmount)
    {
        if (!TryParsePositiveAmount(inputAmount, out long amount, out string error))
            return error;

        if (Context.Caller.IsConsole || Context.Caller.IdentityId == 0)
            return "Only players can use this command";

        long targetId;
        string targetName;
        switch (NormalizeEcoType(type))
        {
            case "player":
                IMyIdentity identity = Utilities.GetIdentityByNameOrIds(recipient);
                if (identity == null)
                    return "Cant find that player";

                targetId = identity.IdentityId;
                targetName = identity.DisplayName;
                break;
            case "faction":
                MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(recipient);
                if (faction == null)
                    return "Cant find that faction";

                targetId = faction.FactionId;
                targetName = faction.Name;
                break;
            default:
                return "Incorrect usage, example: !ess eco pay player <player> <amount> or !ess eco pay faction <tag> <amount>";
        }

        if (targetId == Context.Caller.IdentityId)
            return "You cannot pay yourself!";

        if (!TryTransferCredits(Context.Caller.IdentityId, targetId, amount, out error))
            return error;

        return $"Sent {FormatCredits(amount)} credits to {targetName}.";
    }

    [Command("eco giveplayer", "Economy alias for econ give.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EcoGivePlayer(string playerNameOrId, long amount)
    {
        if (amount <= 0)
        {
            Context.Respond("Amount must be positive.");
            return;
        }

        Give(playerNameOrId, amount);
    }

    [Command("eco takeplayer", "Economy alias for econ take.")]
    [Permission(MyPromoteLevel.Admin)]
    public void EcoTakePlayer(string playerNameOrId, long amount)
    {
        if (amount <= 0)
        {
            Context.Respond("Amount must be positive.");
            return;
        }

        Take(playerNameOrId, amount);
    }

    private void ChangeFactionBalance(string tag, long amount)
    {
        MyFaction faction = MySession.Static.Factions.TryGetFactionByTag(tag);
        if (faction == null)
        {
            Context.Respond("Cant find that faction");
            return;
        }

        long before = MyBankingSystem.GetBalance(faction.FactionId);
        if (before < 0)
        {
            Context.Respond("Faction account cannot be found.");
            return;
        }

        if (before + amount < 0)
        {
            Context.Respond("They cant afford that.");
            Context.Respond($"{faction.Name} Current Balance : {FormatCredits(before)}");
            return;
        }

        MyBankingSystem.ChangeBalance(faction.FactionId, amount);
        long after = MyBankingSystem.GetBalance(faction.FactionId);
        Context.Respond($"{faction.Name} FACTION Balance Before Change : {FormatCredits(before)}");
        Context.Respond($"{faction.Name} FACTION Balance After Change : {FormatCredits(after)}");
    }

    private static void ChangeBalance(long identityId, long amount)
    {
        long balance = MyBankingSystem.GetBalance(identityId);
        if (balance + amount < 0)
            amount = -balance;

        MyBankingSystem.ChangeBalance(identityId, amount);
    }

    private static bool TryTransferCredits(
        long fromIdentityId,
        long toIdentityId,
        long amount,
        out string error
    )
    {
        if (amount <= 0)
        {
            error = "Amount must be positive.";
            return false;
        }

        long fromBalance = MyBankingSystem.GetBalance(fromIdentityId);
        long toBalance = MyBankingSystem.GetBalance(toIdentityId);
        if (fromBalance < 0 || toBalance < 0)
        {
            error = "Account cannot be found.";
            return false;
        }

        long finalFromBalance = fromBalance - amount;
        if (finalFromBalance < 0)
        {
            error = $"Sorry, but you are short {-finalFromBalance:#,##0} credits!";
            return false;
        }

        long finalToBalance = toBalance + amount;
        MyBankingSystem.RequestTransfer_BroadcastToClients(
            fromIdentityId,
            toIdentityId,
            amount,
            finalFromBalance,
            finalToBalance
        );
        error = null;
        return true;
    }

    private static bool TryParsePositiveAmount(
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
            error = "Error parsing into number";
            return false;
        }

        if (amount <= 0)
        {
            error = "Amount must be positive.";
            return false;
        }

        error = null;
        return true;
    }

    private static string BuildPlayerBalanceReport(
        string header,
        int? limit,
        bool onlyOnline,
        bool excludeNpcs
    )
    {
        TryFindPlayerIdentities("*", onlyOnline, excludeNpcs, out List<long> foundIdentities);

        Dictionary<IMyIdentity, long> balances = new Dictionary<IMyIdentity, long>();
        foreach (long identityId in foundIdentities)
        {
            IMyIdentity identity = MySession.Static.Players.TryGetIdentity(identityId);
            if (identity != null)
                balances[identity] = MyBankingSystem.GetBalance(identityId);
        }

        IEnumerable<KeyValuePair<IMyIdentity, long>> sorted = balances
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key.DisplayName);
        if (limit.HasValue)
            sorted = sorted.Take(limit.Value);

        StringBuilder data = new StringBuilder();
        data.AppendLine(header);
        foreach (KeyValuePair<IMyIdentity, long> value in sorted)
            data.AppendLine(
                $"Player: {value.Key.DisplayName} - Balance: {FormatCredits(value.Value)}"
            );

        return data.ToString();
    }

    private static string BuildFactionBalanceReport(string header, int limit)
    {
        StringBuilder data = new StringBuilder();
        data.AppendLine(header);

        foreach (
            MyFaction faction in MySession
                .Static.Factions.Select(pair => pair.Value)
                .OrderByDescending(faction => MyBankingSystem.GetBalance(faction.FactionId))
                .ThenBy(faction => faction.Tag)
                .Take(limit)
        )
        {
            data.AppendLine(
                $"{faction.Name} - {faction.Tag} - Balance: {FormatCredits(MyBankingSystem.GetBalance(faction.FactionId))}"
            );
        }

        return data.ToString();
    }

    private static string NormalizeEcoType(string type) =>
        (type ?? string.Empty).Trim().ToLowerInvariant();

    private static string FormatCredits(long amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture);

    private static bool TryFindPlayerIdentities(
        string playerName,
        bool onlyOnline,
        bool excludeNpcs,
        out List<long> foundIdentities
    )
    {
        List<long> relevantIdentities = new List<long>();
        var players = MySession.Static.Players;

        if (playerName != "*")
        {
            IMyIdentity identity = Utilities.GetIdentityByNameOrIds(playerName);
            if (identity == null)
            {
                foundIdentities = relevantIdentities;
                return false;
            }

            relevantIdentities.Add(identity.IdentityId);
        }
        else
        {
            relevantIdentities.AddRange(
                players.GetAllIdentities().Select(identity => identity.IdentityId)
            );
        }

        IEnumerable<long> identitiesToCheck = relevantIdentities;

        if (onlyOnline)
            identitiesToCheck = identitiesToCheck.Where(identityId =>
                players.IsPlayerOnline(identityId)
            );

        if (excludeNpcs)
            identitiesToCheck = identitiesToCheck.Where(identityId =>
                !players.IdentityIsNpc(identityId)
            );

        foundIdentities = identitiesToCheck.ToList();
        return true;
    }
}
