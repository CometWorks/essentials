using PluginSdk;
using PluginSdk.Commands;
using VRage.Game.ModAPI;

namespace ServerPlugin.Commands;

// Top-level forms of Essentials commands that players and admins know without the
// !ess prefix from Torch: !fixship and its moderator variants (ALE ShipFixer) and
// !reload. Each runs the same code as its !ess form. Magnetar itself provides !save,
// !restart, !stop and !quit.

[CommandRoot("fixship", "Essentials", "Fix the ship you look at, or one by name")]
public sealed class FixShipCommand : CommandModule
{
    [Command("", "Cuts and pastes a ship you are looking at or with the given name.")]
    [Permission(MyPromoteLevel.None)]
    public void FixShip(string gridName = null) =>
        ShipFixerCommands.FixShipPlayer(Context, gridName);
}

[CommandRoot("fixshipmod", "Essentials", "Fix a ship by look target or name (moderator)")]
public sealed class FixShipModCommand : CommandModule
{
    [Command("", "Cuts and pastes a ship by look target or grid name.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void FixShipMod(string gridName = null) =>
        ShipFixerCommands.FixShipModeratorByLookOrName(Context, gridName);
}

[CommandRoot("fixshipmodid", "Essentials", "Fix a ship by entity id (moderator)")]
public sealed class FixShipModIdCommand : CommandModule
{
    [Command("", "Cuts and pastes a ship by entity id.")]
    [Permission(MyPromoteLevel.Moderator)]
    public void FixShipModId(long gridId = 0) =>
        ShipFixerCommands.FixShipModeratorById(Context, gridId);
}

[CommandRoot("reload", "Essentials", "Save the world and reload the dedicated server config")]
public sealed class ReloadCommand : CommandModule
{
    [Command("", "Save the world and reload the dedicated server config.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Reload() =>
        ServerControl.ReloadConfig() ? "Configuration reloaded." : "No world is loaded.";
}
