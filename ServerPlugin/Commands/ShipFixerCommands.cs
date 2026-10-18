using System;
using System.Collections.Generic;
using PluginSdk.Commands;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using ServerPlugin.ShipFixer;
using VRage.Game.ModAPI;

namespace ServerPlugin.Commands;

// The ship fixer behind !ess fixship and its top-level forms (ShortCommands.cs). Cooldowns and
// pending confirmations are shared, so !fixship and !ess fixship confirm each other.
internal static class ShipFixerCommands
{
    private static readonly Dictionary<string, DateTime> CommandCooldowns =
        new Dictionary<string, DateTime>();
    private static readonly Dictionary<string, DateTime> ShipFixerConfirmations =
        new Dictionary<string, DateTime>();
    private static readonly ShipFixerCore ShipFixerCoreInstance = new ShipFixerCore();

    public static void FixShipPlayer(CommandContext context, string gridName = null)
    {
        if (!Plugin.Instance.Config.ShipFixerPlayerCommandEnabled)
        {
            context.Respond("This command was disabled for players use!");
            return;
        }

        if (context.Caller.IsConsole || context.Caller.IdentityId == 0)
        {
            context.Respond(
                "Console has no Grids so cannot use this command. Use !ess fixshipmod <Gridname> instead!"
            );
            return;
        }

        MyPlayer player = Utilities.GetPlayerByIdentityId(context.Caller.IdentityId);
        if (player == null)
        {
            context.Respond("Player not found.");
            return;
        }

        if (!CheckCommandCooldown(ShipFixerPlayerKey(context), out int remainingSeconds))
        {
            Plugin.Instance.Log.Info(
                $"Cooldown for Player {player.DisplayName} still running! {remainingSeconds} seconds remaining!"
            );
            context.Respond($"Command is still on cooldown for {remainingSeconds} seconds.");
            return;
        }

        long playerId = context.Caller.IdentityId;
        MyCharacter character = null;
        if (string.IsNullOrWhiteSpace(gridName))
        {
            if (player.Character is not MyCharacter myCharacter)
            {
                context.Respond(
                    "You have no Character currently. Make sure to spawn and be out of cockpit!"
                );
                return;
            }

            character = myCharacter;
            gridName = "nogrid";
        }

        if (
            !CheckConfirmation(
                context,
                ShipFixerPlayerKey(context),
                playerId,
                gridName,
                character,
                0
            )
        )
            return;

        try
        {
            ShipFixerResult result =
                character != null
                    ? ShipFixerCoreInstance.FixShip(character, playerId)
                    : ShipFixerCoreInstance.FixShip(playerId, gridName);

            WriteResponse(context, result);

            if (result == ShipFixerResult.ShipFixed)
            {
                Plugin.Instance.Log.Info($"Cooldown for Player {player.DisplayName} started!");
                StartCommandCooldown(ShipFixerPlayerKey(context));
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Error on fixing ship");
            context.Respond("Error on fixing ship.");
        }
    }

    public static void FixShipModeratorByLookOrName(CommandContext context, string gridName = null)
    {
        if (!string.IsNullOrWhiteSpace(gridName))
        {
            FixShipModeratorByName(context, gridName);
            return;
        }

        if (context.Caller.IsConsole)
        {
            context.Respond(
                "Console has no Character so cannot use this command. Use !ess fixshipmod <Gridname> instead!"
            );
            return;
        }

        MyPlayer player = Utilities.GetPlayerByIdentityId(context.Caller.IdentityId);
        if (player?.Character is not MyCharacter character)
        {
            context.Respond(
                "You have no Character currently. Make sure to spawn and be out of cockpit!"
            );
            return;
        }

        if (!CheckConfirmation(context, ShipFixerPlayerKey(context), 0, "nogrid", character, 0))
            return;

        try
        {
            WriteResponse(context, ShipFixerCoreInstance.FixShip(character, 0));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Error on fixing ship");
            context.Respond("Error on fixing ship.");
        }
    }

    public static void FixShipModeratorById(CommandContext context, long gridId)
    {
        if (gridId == 0)
        {
            context.Respond("Correct Usage is !ess fixshipmodid EntityID");
            return;
        }

        if (!CheckConfirmation(context, ShipFixerPlayerKey(context), 0, "nogrid", null, gridId))
            return;

        try
        {
            WriteResponse(context, ShipFixerCoreInstance.FixShip(0, gridId));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Error on fixing ship");
            context.Respond("Error on fixing ship.");
        }
    }

    private static void FixShipModeratorByName(CommandContext context, string gridName)
    {
        if (!CheckConfirmation(context, ShipFixerPlayerKey(context), 0, gridName, null, 0))
            return;

        try
        {
            WriteResponse(context, ShipFixerCoreInstance.FixShip(0, gridName));
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.Error(ex, "Error on fixing ship");
            context.Respond("Error on fixing ship.");
        }
    }

    private static bool CheckConfirmation(
        CommandContext context,
        string callerKey,
        long playerId,
        string gridName,
        MyCharacter character,
        long id
    )
    {
        string confirmationKey = $"{callerKey}:{gridName}:{id}:{playerId}";
        DateTime now = DateTime.UtcNow;

        if (
            ShipFixerConfirmations.TryGetValue(confirmationKey, out DateTime expiresAt)
            && expiresAt >= now
        )
        {
            ShipFixerConfirmations.Remove(confirmationKey);
            return true;
        }

        List<MyCubeGrid> groups =
            id != 0
                ? ShipFixerCore.FindGridGroupsForPlayer(
                    gridName,
                    playerId,
                    out ShipFixerResult searchResult,
                    id
                )
            : character == null
                ? ShipFixerCore.FindGridGroupsForPlayer(gridName, playerId, out searchResult)
            : ShipFixerCore.FindLookAtGridGroup(character, playerId, out searchResult);

        if (groups == null || groups.Count == 0 || searchResult != ShipFixerResult.OK)
        {
            WriteResponse(context, searchResult);
            return false;
        }

        ShipFixerResult checkResult = ShipFixerCore.CheckGroups(
            groups,
            out _,
            playerId,
            ejectPlayers: false
        );
        if (checkResult != ShipFixerResult.OK)
        {
            WriteResponse(context, checkResult);
            return false;
        }

        ShipFixerConfirmations[confirmationKey] = now.AddSeconds(
            Plugin.Instance.Config.ShipFixerConfirmationInSeconds
        );
        context.Respond(
            $"Are you sure you want to continue? Enter the command again within {Plugin.Instance.Config.ShipFixerConfirmationInSeconds} seconds to confirm fixship on {groups[0].DisplayName}."
        );
        return false;
    }

    private static bool CheckCommandCooldown(string callerKey, out int remainingSeconds)
    {
        DateTime now = DateTime.UtcNow;
        if (!CommandCooldowns.TryGetValue(callerKey, out DateTime expiresAt) || expiresAt <= now)
        {
            remainingSeconds = 0;
            return true;
        }

        remainingSeconds = (int)Math.Ceiling((expiresAt - now).TotalSeconds);
        return false;
    }

    private static void StartCommandCooldown(string callerKey) =>
        CommandCooldowns[callerKey] = DateTime.UtcNow.AddSeconds(
            Plugin.Instance.Config.ShipFixerCooldownInSeconds
        );

    private static string ShipFixerPlayerKey(CommandContext context)
    {
        if (context.Caller.SteamId != 0)
            return context.Caller.SteamId.ToString();

        if (context.Caller.IdentityId != 0)
            return context.Caller.IdentityId.ToString();

        return "console";
    }

    private static void WriteResponse(CommandContext context, ShipFixerResult result)
    {
        switch (result)
        {
            case ShipFixerResult.TooFewGrids:
                context.Respond("Could not find your Grid, Or Check if ownership is correct");
                break;
            case ShipFixerResult.TooManyGrids:
                context.Respond(
                    "Found multiple Grids with same Name. Rename your grid first to something unique."
                );
                break;
            case ShipFixerResult.UnknownProblem:
                context.Respond("Could not work with found grid for unknown reason.");
                break;
            case ShipFixerResult.OwnedByDifferentPlayer:
                context.Respond("Grid seems to be owned by a different player.");
                break;
            case ShipFixerResult.DifferentOwnerOnConnectedGrid:
                context.Respond("One of the connected grids is owned by a different player.");
                break;
            case ShipFixerResult.GridOccupied:
                context.Respond(
                    "Cockpits or seats are still occupied! Clear them first and try again."
                );
                break;
            case ShipFixerResult.ShipFixed:
                context.Respond("Ship was fixed!");
                break;
            case ShipFixerResult.GridNotFound:
                context.Respond("Grid not found");
                break;
        }
    }
}

public sealed partial class EssentialsModule
{
    [Command("fixship", "Cuts and pastes a ship you are looking at or with the given name.")]
    [Permission(MyPromoteLevel.None)]
    public void FixShip(string gridName = null) =>
        ShipFixerCommands.FixShipPlayer(Context, gridName);

    [Command("fixshipmod", "Cuts and pastes a ship by look target or grid name.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void FixShipMod(string gridName = null) =>
        ShipFixerCommands.FixShipModeratorByLookOrName(Context, gridName);

    [Command("fixshipmodid", "Cuts and pastes a ship by entity id.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void FixShipModId(long gridId = 0) =>
        ShipFixerCommands.FixShipModeratorById(Context, gridId);
}
