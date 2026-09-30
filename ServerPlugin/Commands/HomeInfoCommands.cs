using System.Collections.Generic;
using PluginSdk.Commands;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.ModAPI;
using VRageMath;

namespace ServerPlugin.Commands;

public sealed partial class EssentialsModule
{
    [Command("home add", "Save your current position as a home.")]
    [Permission(MyPromoteLevel.None)]
    public string HomeAdd(string homeName)
    {
        if (!HomesEnabled())
            return "Homes are not enabled for this server.";

        if (!TryGetCallerPlayerForHomes(out MyPlayer player, out string error))
            return error;

        int maxHomes = Plugin.Instance?.PluginConfig?.MaxHomesPerPlayer ?? 3;
        return HomeStore.TryAdd(Context.Caller.SteamId, homeName, player.GetPosition(), maxHomes, out error)
            ? "Home successfully added."
            : error;
    }

    [Command("home del", "Delete a saved home.")]
    [Permission(MyPromoteLevel.None)]
    public string HomeDel(string homeName)
    {
        if (!HomesEnabled())
            return "Homes are not enabled for this server.";

        if (Context.Caller.IsConsole || Context.Caller.SteamId == 0)
            return "Only players can use homes.";

        return HomeStore.TryRemove(Context.Caller.SteamId, homeName)
            ? "Home successfully removed."
            : "The stated home does not exist.";
    }

    [Command("home list", "List your saved homes.")]
    [Permission(MyPromoteLevel.None)]
    public string HomeList()
    {
        if (!HomesEnabled())
            return "Homes are not enabled for this server.";

        if (Context.Caller.IsConsole || Context.Caller.SteamId == 0)
            return "Only players can use homes.";

        IReadOnlyList<string> homes = HomeStore.List(Context.Caller.SteamId);
        return homes.Count == 0 ? "You do not have any homes." : "List of homes: " + string.Join(", ", homes);
    }

    [Command("home goto", "Teleport to a saved home.")]
    [Permission(MyPromoteLevel.None)]
    public string HomeGoto(string homeName)
    {
        if (!HomesEnabled())
            return "Homes are not enabled for this server.";

        if (!TryGetCallerPlayerForHomes(out MyPlayer player, out string error))
            return error;

        if (player.Controller?.ControlledEntity is MyCockpit || player.Character?.UsingEntity is MyCockpit)
            return "You cannot use !ess home while in control of a grid.";

        if (!HomeStore.TryGet(Context.Caller.SteamId, homeName, out Vector3D targetPosition))
            return "The stated home does not exist.";

        MyVisualScriptLogicProvider.SetPlayersPosition(Context.Caller.IdentityId, targetPosition);
        player.Character?.Physics?.ClearSpeed();
        return $"Teleported to '{homeName}'.";
    }

    [Command("info list", "List configured info commands.")]
    [Permission(MyPromoteLevel.None)]
    public string InfoList()
    {
        List<string> commands = InfoCommandHandler.ConfiguredNames(Plugin.Instance?.PluginConfig);

        return commands.Count == 0 ? "No info commands are configured." : string.Join(", ", commands);
    }

    private static bool HomesEnabled()
        => Plugin.Instance?.PluginConfig?.HomesEnabled == true;

    private bool TryGetCallerPlayerForHomes(out MyPlayer player, out string error)
    {
        player = null;
        if (Context.Caller.IsConsole || Context.Caller.SteamId == 0 || Context.Caller.IdentityId == 0)
        {
            error = "Only players can use homes.";
            return false;
        }

        player = Utilities.GetPlayerByIdentityId(Context.Caller.IdentityId);
        if (player?.Character == null)
        {
            error = "You need to spawn into a character first.";
            return false;
        }

        error = null;
        return true;
    }
}
