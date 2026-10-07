using System.Collections.Generic;
using PluginSdk.Config;

namespace Shared.Config;

// Configuration model for timed/triggered server command sequences ("auto commands").
//
// The model keeps scheduled command sequences compact while fitting the Magnetar
// PluginSdk: a flat List<AutoCommand> is rendered by Quasar, each command owning
// an ordered List<CommandStep>. The whole feature defaults to empty, so a fresh
// install runs no auto commands.

/// <summary>How an <see cref="AutoCommand"/> decides when to start.</summary>
public enum AutoTrigger
{
    [EnumCaption("Disabled (only via runauto)")]
    Disabled,

    [EnumCaption("On server start")]
    OnStart,

    [EnumCaption("Timed (every interval)")]
    Timed,

    [EnumCaption("Scheduled (time of day)")]
    Scheduled,

    [EnumCaption("Player vote")]
    Vote,

    [EnumCaption("Online player count")]
    PlayerCount,

    [EnumCaption("Grid count")]
    GridCount,

    [EnumCaption("Simulation speed")]
    SimSpeed,
}

/// <summary>Comparison operator for the count/ratio triggers.</summary>
public enum TriggerCompare
{
    [EnumCaption("Less than")]
    LessThan,

    [EnumCaption("Greater than")]
    GreaterThan,

    [EnumCaption("Equal to")]
    Equal,
}

/// <summary>Day filter for the <see cref="AutoTrigger.Scheduled"/> trigger.</summary>
public enum AutoDayOfWeek
{
    [EnumCaption("Every day")]
    All,
    Sunday,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
}

/// <summary>What a single <see cref="CommandStep"/> does when it runs.</summary>
public enum StepAction
{
    [EnumCaption("Run command line")]
    Command,

    [EnumCaption("Announce (chat to all)")]
    Announce,

    [EnumCaption("Notify (HUD to all)")]
    Notify,

    [EnumCaption("Save world")]
    Save,

    [EnumCaption("Reload dedicated config")]
    ReloadConfig,

    [EnumCaption("Save and restart server")]
    Restart,

    [EnumCaption("Save and stop server")]
    Stop,

    [EnumCaption("Run another auto command")]
    RunAuto,

    [EnumCaption("Nothing (delay / shell only)")]
    None,
}

/// <summary>Fonts supported by the game's HUD notifications.</summary>
public enum NotificationFont
{
    [EnumCaption("Default (use legacy Color setting)")]
    Default,
    White,
    Red,
    Green,
    Blue,
    DarkBlue,
    Monospace,
}

/// <summary>
/// One step of an <see cref="AutoCommand"/>. An optional shell script runs first
/// (the sequence waits for it to exit, without blocking the server), then the
/// chosen <see cref="Action"/> runs, then the sequence waits <see cref="Delay"/>
/// before the next step.
/// </summary>
public struct CommandStep
{
    [StructMember("Delay after this step before the next one (HH:MM:SS).")]
    public string Delay { get; set; }

    [StructMember("What this step does. Use 'Run command line' to run the line in Command.")]
    public StepAction Action { get; set; }

    [StructMember("Command line for 'Run command line' (e.g. !ess broadcast Hi), " +
                  "the message for Announce/Notify, or the auto command name for 'Run another auto command'."), StructCaption]
    public string Command { get; set; }

    [StructMember("Optional colour for Announce: 'R G B' (0-255) or a name like Red. Also used by legacy Notify steps when NotifyFont is Default.")]
    public string Color { get; set; }

    [StructMember("HUD font for Notify. Default uses the legacy Color setting, or White when Color is empty.")]
    public NotificationFont NotifyFont { get; set; }

    [StructMember("Notify display time in milliseconds (0 = default 5000).")]
    public int NotifyDurationMs { get; set; }

    [StructMember("Optional shell script/command run before the action. The sequence waits for it to finish.")]
    public string ShellScript { get; set; }

    [StructMember("Maximum seconds to wait for the shell script (0 = wait indefinitely).")]
    public int ShellTimeoutSeconds { get; set; }
}

/// <summary>
/// A named sequence of <see cref="CommandStep"/>s plus the trigger that starts it.
/// Trigger <see cref="AutoTrigger.Disabled"/> never starts on its own but can still
/// be invoked with <c>!ess runauto &lt;name&gt;</c> or chained from another command's
/// <see cref="StepAction.RunAuto"/> step.
/// </summary>
public struct AutoCommand
{
    [StructMember("Unique name. Use it with !ess runauto <name> and the RunAuto step action."), StructCaption]
    public string Name { get; set; }

    [StructMember("When this command starts on its own.")]
    public AutoTrigger Trigger { get; set; }

    [StructMember("Comparison used by the Player count / Grid count / Simulation speed triggers.")]
    public TriggerCompare Compare { get; set; }

    [StructMember("Timed: repeat interval (HH:MM:SS). Scheduled: time of day (HH:MM:SS). " +
                  "Count/SimSpeed triggers: re-check cooldown.")]
    public string Interval { get; set; }

    [StructMember("Scheduled trigger: which day(s) to run.")]
    public AutoDayOfWeek DayOfWeek { get; set; }

    [StructMember("SimSpeed/Vote triggers: ratio 0..1 (0.5 = 50%).")]
    public float TriggerRatio { get; set; }

    [StructMember("Player count / Grid count triggers: the count to compare against. " +
                  "SimSpeed: seconds the condition must hold before firing.")]
    public double TriggerCount { get; set; }

    [StructMember("Steps run in order when the command starts.")]
    public List<CommandStep> Steps { get; set; }
}
