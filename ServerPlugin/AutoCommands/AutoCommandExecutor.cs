using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PluginSdk;
using PluginSdk.Commands;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using ServerPlugin.Commands;
using Shared.Config;
using Shared.Logging;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace ServerPlugin.AutoCommands;

/// <summary>
/// Reads <see cref="PluginConfig.AutoCommands"/> and runs the configured command
/// sequences on schedule. Driven every frame from the plugin's update loop on the
/// game thread; trigger evaluation is throttled to roughly once per second. Holds
/// its own command pipeline so steps can dispatch the plugin's <c>!ess</c> commands
/// as the server, and a small vote manager for the Vote trigger.
/// </summary>
public sealed class AutoCommandExecutor
{
    private readonly PluginConfig config;
    private readonly IPluginLogger log;

    private readonly CommandDispatcher dispatcher;
    private readonly AutoCommandResponder responder;
    private readonly CommandCaller serverCaller =
        new CommandCaller(0UL, 0L, "Server", MyPromoteLevel.Owner, isConsole: true);

    private readonly List<RunningSequence> running = new();
    private readonly Dictionary<string, ScheduleState> schedules = new();
    private DateTime lastEvaluation = DateTime.MinValue;

    // Active vote (Vote trigger). Null name means no vote in progress.
    private string voteName;
    private DateTime voteEndsAt;
    private readonly HashSet<long> voteYes = new();
    private readonly HashSet<long> voteNo = new();
    private string lastVoteName;
    private string lastVoteResult = "none";
    private float lastVotePercentage;

    internal IPluginLogger Log => log;

    public string RestartSequenceName => config.OnRestartSequence ?? "";
    public string ShutdownSequenceName => config.OnShutdownSequence ?? "";

    public AutoCommandExecutor(PluginConfig config, IPluginLogger log)
    {
        this.config = config;
        this.log = log;

        responder = new AutoCommandResponder(log);

        var registry = new CommandRegistry();
        registry.RegisterModule(typeof(EssentialsModule), "Essentials");
        dispatcher = new CommandDispatcher(registry, (message, ex) => log.Error(ex, message));
    }

    /// <summary>Called every simulation frame from the plugin update loop.</summary>
    public void Update()
    {
        if (config == null || !config.Enabled)
            return;

        DateTime now = DateTime.Now;

        // Advance running sequences every frame so shell polling and step delays stay responsive.
        for (int i = running.Count - 1; i >= 0; i--)
        {
            RunningSequence sequence = running[i];
            try
            {
                sequence.Tick(now);
            }
            catch (Exception e)
            {
                log.Error(e, "Auto command '{0}' aborted", sequence.Name);
                sequence.ForceComplete();
            }

            if (sequence.Completed)
                running.RemoveAt(i);
        }

        // Evaluate triggers roughly once per second, only while a session is live.
        if ((now - lastEvaluation).TotalSeconds < 1.0)
            return;
        lastEvaluation = now;

        if (MySession.Static?.Ready != true)
            return;

        EvaluateTriggers(now);
        EvaluateVote(now);
    }

    // ----- Triggers -------------------------------------------------------

    private void EvaluateTriggers(DateTime now)
    {
        IReadOnlyList<AutoCommand> commands = config.AutoCommands;
        if (commands == null)
            return;

        for (int i = 0; i < commands.Count; i++)
        {
            AutoCommand command = commands[i];
            if (command.Steps == null || command.Steps.Count == 0)
                continue;

            ScheduleState state = GetSchedule(command, i);

            try
            {
                EvaluateTrigger(command, state, now);
            }
            catch (Exception e)
            {
                log.Error(e, "Auto command '{0}' trigger evaluation failed", command.Name);
            }
        }
    }

    private void EvaluateTrigger(AutoCommand command, ScheduleState state, DateTime now)
    {
        switch (command.Trigger)
        {
            case AutoTrigger.Disabled:
            case AutoTrigger.Vote:
                return;

            case AutoTrigger.OnStart:
                if (!state.OnStartFired)
                {
                    state.OnStartFired = true;
                    Start(command, "OnStart");
                }

                return;

            case AutoTrigger.Timed:
            {
                if (!TryParseSpan(command.Interval, out TimeSpan interval) || interval <= TimeSpan.Zero)
                    return;

                if (!state.Initialized)
                {
                    state.NextRun = now + interval;
                    state.Initialized = true;
                    return;
                }

                if (now >= state.NextRun)
                {
                    Start(command, "Timed");
                    state.NextRun = now + interval;
                }

                return;
            }

            case AutoTrigger.Scheduled:
            {
                if (!TryParseSpan(command.Interval, out TimeSpan timeOfDay))
                    return;

                if (!state.Initialized)
                {
                    state.NextRun = NextDailyTime(now, timeOfDay);
                    state.Initialized = true;
                }

                if (now >= state.NextRun)
                {
                    if (DayMatches(command.DayOfWeek, state.NextRun))
                        Start(command, "Scheduled");
                    state.NextRun = state.NextRun.AddDays(1);
                }

                return;
            }

            case AutoTrigger.PlayerCount:
                EvaluateCount(command, state, now, GetOnlinePlayerCount());
                return;

            case AutoTrigger.GridCount:
                EvaluateCount(command, state, now, GetGridCount());
                return;

            case AutoTrigger.SimSpeed:
                EvaluateSimSpeed(command, state, now);
                return;
        }
    }

    private void EvaluateCount(AutoCommand command, ScheduleState state, DateTime now, double current)
    {
        if (!Compare(current, command.TriggerCount, command.Compare))
            return;

        if (now < state.NextRun)
            return;

        Start(command, command.Trigger.ToString());
        state.NextRun = now + Cooldown(command.Interval);
    }

    private void EvaluateSimSpeed(AutoCommand command, ScheduleState state, DateTime now)
    {
        double ratio = Math.Min(Sync.ServerSimulationRatio, 1.0);
        if (!Compare(ratio, command.TriggerRatio, command.Compare))
        {
            state.SimDwellSince = null;
            return;
        }

        if (state.SimDwellSince == null)
        {
            state.SimDwellSince = now;
            return;
        }

        if ((now - state.SimDwellSince.Value).TotalSeconds < command.TriggerCount)
            return;

        if (now < state.NextRun)
            return;

        Start(command, "SimSpeed");
        state.SimDwellSince = null;
        state.NextRun = now + Cooldown(command.Interval);
    }

    // ----- Starting / running --------------------------------------------

    private void Start(AutoCommand command, string reason)
    {
        if (command.Steps == null || command.Steps.Count == 0)
            return;

        if (!string.IsNullOrEmpty(command.Name) &&
            running.Any(r => string.Equals(r.Name, command.Name, StringComparison.OrdinalIgnoreCase)))
        {
            log.Info("Auto command '{0}' is already running; skipping ({1})", command.Name, reason);
            return;
        }

        running.Add(new RunningSequence(this, command, DateTime.Now));
        log.Info("Auto command '{0}' started ({1})", string.IsNullOrEmpty(command.Name) ? "(unnamed)" : command.Name, reason);
    }

    /// <summary>Starts the auto command with the given name, regardless of trigger.</summary>
    public bool RunByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || config.AutoCommands == null)
            return false;

        foreach (AutoCommand command in config.AutoCommands)
        {
            if (string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase) &&
                command.Steps != null && command.Steps.Count > 0)
            {
                Start(command, "runauto");
                return true;
            }
        }

        return false;
    }

    /// <summary>Cancels running instances of the named auto command. Returns how many were cancelled.</summary>
    public int CancelByName(string name)
    {
        int cancelled = 0;
        for (int i = running.Count - 1; i >= 0; i--)
        {
            if (string.Equals(running[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                running[i].ForceComplete();
                running.RemoveAt(i);
                cancelled++;
            }
        }

        return cancelled;
    }

    /// <summary>Cancels one running auto command by 1-based list index.</summary>
    public bool CancelByIndex(int index, out string name)
    {
        name = null;
        if (index < 1 || index > running.Count)
            return false;

        RunningSequence sequence = running[index - 1];
        name = string.IsNullOrEmpty(sequence.Name) ? "(unnamed)" : sequence.Name;
        sequence.ForceComplete();
        running.RemoveAt(index - 1);
        return true;
    }

    /// <summary>Human-readable listing of configured and running auto commands.</summary>
    public IEnumerable<string> Describe()
    {
        List<AutoCommand> commands = config.AutoCommands?.ToList() ?? new List<AutoCommand>();
        var lines = new List<string> { $"Auto commands: {commands.Count} configured, {running.Count} running" };

        foreach (AutoCommand command in commands)
        {
            string name = string.IsNullOrEmpty(command.Name) ? "(unnamed)" : command.Name;
            int steps = command.Steps?.Count ?? 0;
            lines.Add($"- {name} [{command.Trigger}] {steps} step(s)");
        }

        if (running.Count > 0)
        {
            lines.Add("Running auto commands:");
            for (int i = 0; i < running.Count; i++)
            {
                string name = string.IsNullOrEmpty(running[i].Name) ? "(unnamed)" : running[i].Name;
                lines.Add($"{i + 1}. {name}");
            }
        }

        return lines;
    }

    // ----- Step actions ---------------------------------------------------

    /// <summary>Runs a single step's action on the game thread.</summary>
    internal void RunStepAction(in CommandStep step)
    {
        switch (step.Action)
        {
            case StepAction.None:
                return;

            case StepAction.Command:
                Dispatch(step.Command);
                return;

            case StepAction.Announce:
                Announce(step.Command, step.Color);
                return;

            case StepAction.Notify:
                Notify(step.Command, step.NotifyDurationMs, step.NotifyFont, step.Color);
                return;

            case StepAction.Save:
                ServerControl.SaveWorld();
                return;

            case StepAction.ReloadConfig:
                ServerControl.ReloadConfig();
                return;

            case StepAction.Restart:
                ServerControl.SaveAndRestart();
                return;

            case StepAction.Stop:
                ServerControl.SaveAndQuit();
                return;

            case StepAction.RunAuto:
                if (!RunByName(step.Command))
                    log.Warning("Auto command step RunAuto: no auto command named '{0}'", step.Command);
                return;
        }
    }

    private void Dispatch(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        line = line.Trim();
        if (line[0] != '!')
            line = "!" + line;

        if (!dispatcher.Handle(line, serverCaller, responder))
            log.Warning("Auto command step: unrecognised command line '{0}'", line);
    }

    private void Announce(string text, string colorText)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Color? color = ParseColor(colorText);
        if (color.HasValue)
            MyVisualScriptLogicProvider.SendChatMessageColored(text, color.Value, "Server", 0L, MyFontEnum.White);
        else
            MyVisualScriptLogicProvider.SendChatMessage(text, "Server", 0L, MyFontEnum.White);
    }

    private static void Notify(string text, int durationMs, NotificationFont font, string legacyColorText)
    {
        if (string.IsNullOrEmpty(text))
            return;

        int duration = durationMs > 0 ? durationMs : 5000;
        MyVisualScriptLogicProvider.ShowNotificationToAll(text, duration, FontFor(font, legacyColorText));
    }

    // ----- Vote -----------------------------------------------------------

    public string StartVote(long voterIdentityId, string name)
    {
        if (voteName != null)
            return "A vote is already in progress.";

        AutoCommand? command = FindVoteCommand(name);
        if (command == null)
            return $"No vote command named '{name}'.";

        voteName = command.Value.Name;
        voteYes.Clear();
        voteNo.Clear();
        voteYes.Add(voterIdentityId);
        voteEndsAt = DateTime.Now + TimeSpan.FromSeconds(Math.Max(5, config.VoteDurationSeconds));
        lastVoteName = voteName;
        lastVoteResult = "in progress";
        lastVotePercentage = 0f;

        Announce($"Vote started: {voteName}. Type !ess vote yes or !ess vote no ({config.VoteDurationSeconds}s).", null);
        return $"Vote '{voteName}' started.";
    }

    public string CastVote(long voterIdentityId, bool yes)
    {
        if (voteName == null)
            return "No vote is in progress.";

        if (yes)
        {
            voteNo.Remove(voterIdentityId);
            voteYes.Add(voterIdentityId);
            return "Vote registered: yes.";
        }

        voteYes.Remove(voterIdentityId);
        voteNo.Add(voterIdentityId);
        return "Vote registered: no.";
    }

    public IEnumerable<string> DescribeVotes()
    {
        List<string> names = (config.AutoCommands ?? new List<AutoCommand>())
            .Where(c => c.Trigger == AutoTrigger.Vote && !string.IsNullOrEmpty(c.Name))
            .Select(c => $"- {c.Name}")
            .ToList();

        if (names.Count == 0)
            return new[] { "No vote commands are configured." };

        names.Insert(0, "Vote commands (start with !ess vote <name>):");
        return names;
    }

    public string CancelVote()
    {
        if (voteName == null)
            return "No vote is in progress.";

        string cancelled = voteName;
        EndVote("cancelled", 0f);
        Announce($"Vote '{cancelled}' cancelled.", null);
        return $"Vote '{cancelled}' cancelled.";
    }

    public IEnumerable<string> DescribeVoteDebug()
    {
        int yes = voteYes.Count;
        int no = voteNo.Count;
        int total = yes + no;
        float percentage = total > 0 ? (float)yes / total * 100f : 0f;

        return new[]
        {
            "Voting state:",
            $"Current vote: {voteName ?? "(none)"}",
            $"Ends at: {(voteName == null ? "(none)" : voteEndsAt.ToString("O", CultureInfo.InvariantCulture))}",
            $"Votes: {yes} yes, {no} no ({percentage:0.##}% yes)",
            $"Last vote: {lastVoteName ?? "(none)"}",
            $"Last result: {lastVoteResult}",
            $"Last yes percent: {lastVotePercentage:0.##}%"
        };
    }

    public string ResetVoteState()
    {
        EndVote("reset", 0f);
        lastVoteName = null;
        lastVoteResult = "none";
        lastVotePercentage = 0f;
        return "Vote state reset.";
    }

    private void EvaluateVote(DateTime now)
    {
        if (voteName == null || now < voteEndsAt)
            return;

        string finished = voteName;
        voteName = null;

        int yes = voteYes.Count;
        int total = yes + voteNo.Count;
        float ratio = total > 0 ? (float)yes / total : 0f;

        AutoCommand? command = FindVoteCommand(finished);
        float required = command?.TriggerRatio ?? 0.5f;

        if (command != null && ratio >= required)
        {
            Announce($"Vote '{finished}' passed ({yes}/{total}).", null);
            lastVoteName = finished;
            lastVoteResult = "passed";
            lastVotePercentage = ratio * 100f;
            RunByName(finished);
        }
        else
        {
            Announce($"Vote '{finished}' failed ({yes}/{total}).", null);
            lastVoteName = finished;
            lastVoteResult = "failed";
            lastVotePercentage = ratio * 100f;
        }

        voteYes.Clear();
        voteNo.Clear();
    }

    private AutoCommand? FindVoteCommand(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || config.AutoCommands == null)
            return null;

        foreach (AutoCommand command in config.AutoCommands)
        {
            if (command.Trigger == AutoTrigger.Vote &&
                string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase))
                return command;
        }

        return null;
    }

    private void EndVote(string result, float percentage)
    {
        if (voteName != null)
            lastVoteName = voteName;

        voteName = null;
        voteEndsAt = default;
        voteYes.Clear();
        voteNo.Clear();
        lastVoteResult = result;
        lastVotePercentage = percentage;
    }

    // ----- Helpers --------------------------------------------------------

    private ScheduleState GetSchedule(AutoCommand command, int index)
    {
        string key = string.IsNullOrEmpty(command.Name)
            ? "idx:" + index
            : "name:" + command.Name.ToLowerInvariant();

        if (!schedules.TryGetValue(key, out ScheduleState state))
        {
            state = new ScheduleState();
            schedules[key] = state;
        }

        return state;
    }

    private static int GetOnlinePlayerCount()
        => Utilities.GetOnlinePlayerCount();

    private static int GetGridCount()
        => MyEntities.GetEntities().OfType<MyCubeGrid>().Count();

    private static bool Compare(double value, double target, TriggerCompare compare)
    {
        switch (compare)
        {
            case TriggerCompare.LessThan:
                return value < target;
            case TriggerCompare.GreaterThan:
                return value > target;
            case TriggerCompare.Equal:
                return Math.Abs(value - target) < 1.0;
            default:
                return false;
        }
    }

    private static TimeSpan Cooldown(string interval)
        => TryParseSpan(interval, out TimeSpan span) && span > TimeSpan.Zero
            ? span
            : TimeSpan.FromSeconds(60);

    private static DateTime NextDailyTime(DateTime now, TimeSpan timeOfDay)
    {
        DateTime candidate = now.Date + timeOfDay;
        return candidate <= now ? candidate.AddDays(1) : candidate;
    }

    private static bool DayMatches(AutoDayOfWeek day, DateTime date)
        => day == AutoDayOfWeek.All || (int)day - 1 == (int)date.DayOfWeek;

    private static bool TryParseSpan(string text, out TimeSpan span)
        => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out span);

    private static string FontFor(NotificationFont font, string legacyColorText)
    {
        switch (font)
        {
            case NotificationFont.White: return MyFontEnum.White;
            case NotificationFont.Red: return MyFontEnum.Red;
            case NotificationFont.Green: return MyFontEnum.Green;
            case NotificationFont.Blue: return MyFontEnum.Blue;
            case NotificationFont.DarkBlue: return MyFontEnum.DarkBlue;
        }

        if (string.IsNullOrWhiteSpace(legacyColorText))
            return MyFontEnum.White;

        switch (legacyColorText.Trim().ToLowerInvariant())
        {
            case "red": return MyFontEnum.Red;
            case "green": return MyFontEnum.Green;
            case "blue": return MyFontEnum.Blue;
            default: return MyFontEnum.White;
        }
    }

    private static Color? ParseColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        string[] parts = text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int r) &&
            int.TryParse(parts[1], out int g) &&
            int.TryParse(parts[2], out int b))
            return new Color(Clamp(r), Clamp(g), Clamp(b));

        switch (text.ToLowerInvariant())
        {
            case "white": return Color.White;
            case "black": return Color.Black;
            case "red": return Color.Red;
            case "green": return Color.Green;
            case "blue": return Color.Blue;
            case "yellow": return Color.Yellow;
            case "cyan": return Color.Cyan;
            case "magenta": return Color.Magenta;
            case "orange": return Color.Orange;
            case "purple": return Color.Purple;
            case "pink": return Color.Pink;
            case "gray":
            case "grey": return Color.Gray;
            default: return null;
        }
    }

    private static int Clamp(int value)
        => value < 0 ? 0 : value > 255 ? 255 : value;

    private sealed class ScheduleState
    {
        public bool Initialized;
        public bool OnStartFired;
        public DateTime NextRun;
        public DateTime? SimDwellSince;
    }
}
