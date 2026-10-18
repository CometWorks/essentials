using System;
using System.Collections.Generic;
using PluginSdk;
using PluginSdk.Commands;
using Sandbox.Game;
using ServerPlugin.AutoCommands;
using VRage.Game.ModAPI;
using VRageMath;

namespace ServerPlugin.Commands;

// Admin verbs the auto-command sequences (and admins in chat) rely on, plus the
// player-facing voting commands. These resolve under the !ess prefix like the rest
// of the plugin's commands.
public sealed partial class EssentialsModule
{
    [Command("say", "Broadcast a plain chat message to all players.")]
    [Permission(MyPromoteLevel.Admin)]
    public CommandReply Say(params string[] words)
    {
        string text = string.Join(" ", words);
        return string.IsNullOrWhiteSpace(text)
            ? CommandReply.Error("Usage: !ess say <message>")
            : CommandReply.Announce(text).WithAuthor("Server");
    }

    [Command("broadcast", "Broadcast a highlighted chat message to all players.")]
    [Permission(MyPromoteLevel.Admin)]
    public CommandReply Broadcast(params string[] words)
    {
        string text = string.Join(" ", words);
        return string.IsNullOrWhiteSpace(text)
            ? CommandReply.Error("Usage: !ess broadcast <message>")
            : CommandReply.Announce(text, Color.Yellow).WithAuthor("Server");
    }

    [Command("notify", "Show a HUD notification to all players.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Notify(string message, int durationMs = 5000, string font = "White")
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Usage: !ess notify <message> [durationMs] [font]";

        MyVisualScriptLogicProvider.ShowNotificationToAll(
            message,
            durationMs > 0 ? durationMs : 5000,
            font
        );
        return "Notification sent.";
    }

    [Command("save", "Save the world.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Save() => ServerControl.SaveWorld() ? "World saved." : "No world is loaded.";

    [Command("reload", "Save the world and reload the dedicated server config.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Reload() =>
        ServerControl.ReloadConfig() ? "Configuration reloaded." : "No world is loaded.";

    [Command("restart", "Run the configured restart sequence, or restart now with 'now'.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Restart(string mode = null)
    {
        AutoCommandExecutor executor = Plugin.Instance?.AutoCommands;
        bool immediate =
            Context.Caller.IsConsole
            || string.Equals(mode, "now", StringComparison.OrdinalIgnoreCase)
            || executor == null
            || string.IsNullOrWhiteSpace(executor.RestartSequenceName);

        if (!immediate && executor.RunByName(executor.RestartSequenceName))
            return $"Restart sequence '{executor.RestartSequenceName}' started.";

        ServerControl.SaveAndRestart();
        return "Restarting the server...";
    }

    [Command("stop", "Run the configured shutdown sequence, or stop now with 'now'.")]
    [Permission(MyPromoteLevel.Admin)]
    public string Stop(string mode = null)
    {
        AutoCommandExecutor executor = Plugin.Instance?.AutoCommands;
        bool immediate =
            Context.Caller.IsConsole
            || string.Equals(mode, "now", StringComparison.OrdinalIgnoreCase)
            || executor == null
            || string.IsNullOrWhiteSpace(executor.ShutdownSequenceName);

        if (!immediate && executor.RunByName(executor.ShutdownSequenceName))
            return $"Shutdown sequence '{executor.ShutdownSequenceName}' started.";

        ServerControl.SaveAndQuit();
        return "Stopping the server...";
    }

    [Command("runauto", "Run a configured auto command by name.")]
    [Permission(MyPromoteLevel.Admin)]
    public string RunAuto(params string[] name)
    {
        AutoCommandExecutor executor = Plugin.Instance?.AutoCommands;
        string target = string.Join(" ", name);
        if (executor == null || string.IsNullOrWhiteSpace(target))
            return "Usage: !ess runauto <name>";

        return executor.RunByName(target)
            ? $"Auto command '{target}' started."
            : $"No auto command named '{target}'.";
    }

    [Command("cancelauto", "Cancel a running auto command by name.")]
    [Permission(MyPromoteLevel.Admin)]
    public string CancelAuto(params string[] name)
    {
        AutoCommandExecutor executor = Plugin.Instance?.AutoCommands;
        string target = string.Join(" ", name);
        if (executor == null || string.IsNullOrWhiteSpace(target))
            return "Usage: !ess cancelauto <name>";

        int cancelled = executor.CancelByName(target);
        return cancelled > 0
            ? $"Cancelled {cancelled} instance(s) of '{target}'."
            : $"No running auto command named '{target}'.";
    }

    [Command("admin cancelautobyindex", "Cancel a running auto command by list index.")]
    [Permission(MyPromoteLevel.Admin)]
    public string AdminCancelAutoByIndex(int index = 0)
    {
        AutoCommandExecutor executor = Plugin.Instance?.AutoCommands;
        if (executor == null)
            return "Auto commands are not available.";

        if (index < 1)
            return "Usage: !ess admin cancelautobyindex <index>";

        return executor.CancelByIndex(index, out string name)
            ? $"Auto command '{name}' cancelled."
            : $"{index} is out of range.";
    }

    [Command("listauto", "List configured and running auto commands.")]
    [Permission(MyPromoteLevel.Admin)]
    public IEnumerable<string> ListAuto() =>
        Plugin.Instance?.AutoCommands?.Describe() ?? new[] { "Auto commands are not available." };

    [Command("vote yes", "Vote yes on the current vote.")]
    [Permission(MyPromoteLevel.None)]
    public string VoteYes() =>
        Plugin.Instance?.AutoCommands?.CastVote(Context.Caller.IdentityId, true)
        ?? "Voting is not available.";

    [Command("vote no", "Vote no on the current vote.")]
    [Permission(MyPromoteLevel.None)]
    public string VoteNo() =>
        Plugin.Instance?.AutoCommands?.CastVote(Context.Caller.IdentityId, false)
        ?? "Voting is not available.";

    [Command("vote list", "List the configured vote commands.")]
    [Permission(MyPromoteLevel.None)]
    public IEnumerable<string> VoteList() =>
        Plugin.Instance?.AutoCommands?.DescribeVotes() ?? new[] { "Voting is not available." };

    [Command("vote cancel", "Cancel the current vote.")]
    [Permission(MyPromoteLevel.Admin)]
    public string VoteCancel() =>
        Plugin.Instance?.AutoCommands?.CancelVote() ?? "Voting is not available.";

    [Command("vote debug", "Show vote state for admins.")]
    [Permission(MyPromoteLevel.Admin)]
    public IEnumerable<string> VoteDebug() =>
        Plugin.Instance?.AutoCommands?.DescribeVoteDebug() ?? new[] { "Voting is not available." };

    [Command("vote reset", "Reset current and previous vote state.")]
    [Permission(MyPromoteLevel.Admin)]
    public string VoteReset() =>
        Plugin.Instance?.AutoCommands?.ResetVoteState() ?? "Voting is not available.";

    [Command("vote", "Start a vote for a configured vote command.")]
    [Permission(MyPromoteLevel.None)]
    public string Vote(params string[] name)
    {
        AutoCommandExecutor executor = Plugin.Instance?.AutoCommands;
        if (executor == null)
            return "Voting is not available.";

        string target = string.Join(" ", name);
        return string.IsNullOrWhiteSpace(target)
            ? "Usage: !ess vote <name> | yes | no | list"
            : executor.StartVote(Context.Caller.IdentityId, target);
    }
}
