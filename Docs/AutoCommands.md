# Auto Commands

Auto commands are named sequences of server actions that run on a schedule or in
response to a condition — timed restarts, cleanup passes, MOTD reminders,
warning countdowns before a shutdown, player votes, and so on.

Everything is configured from the **Auto Commands** tab in the Quasar web UI
(rendered from the `PluginConfig` schema). There are **no auto commands by
default** — a fresh install runs nothing until you add some.

## Concepts

An **auto command** has a name, a **trigger** that decides when it starts, and an
ordered list of **steps**. When the trigger fires, the steps run top to bottom.

A **step** does one thing — announce a message, run a server command, save,
restart, etc. — and may run an optional **shell script** first. Between steps the
sequence waits the step's **delay**.

```
AutoCommand "Restart"           ← name (used by !ess runauto and RunAuto steps)
  Trigger   = Scheduled          ← when it starts
  Interval  = 04:00:00           ← 4 AM (time of day for Scheduled)
  DayOfWeek = All
  Steps:
    1. Announce "Restart in 5 minutes"   delay 04:00
    2. Announce "Restart in 1 minute"    delay 00:50
    3. Notify   "Saving and restarting"  delay 00:10
    4. Restart                            (structured action — restarts now)
```

## Triggers

Set the `Trigger` field on each auto command.

| Trigger | When it runs | Relevant fields |
|---|---|---|
| **Disabled** | Never on its own. Still runnable via `!ess runauto <name>` or a `RunAuto` step. | — |
| **OnStart** | Once, when the world session becomes ready after server start. | — |
| **Timed** | Repeatedly, every `Interval`. | `Interval` (HH:MM:SS, must be > 0) |
| **Scheduled** | At a time of day, optionally on specific days. | `Interval` (time of day), `DayOfWeek` |
| **Vote** | When a player vote passes (see [Voting](#voting)). Not started automatically. | `TriggerRatio` (pass ratio) |
| **PlayerCount** | While the online player count satisfies `Compare TriggerCount`. | `Compare`, `TriggerCount`, `Interval` (cooldown) |
| **GridCount** | While the grid count satisfies `Compare TriggerCount`. | `Compare`, `TriggerCount`, `Interval` (cooldown) |
| **SimSpeed** | While simulation speed satisfies `Compare TriggerRatio` for `TriggerCount` seconds. | `Compare`, `TriggerRatio`, `TriggerCount`, `Interval` (cooldown) |

Notes:

- **Compare** is `LessThan`, `GreaterThan` or `Equal`. For `PlayerCount`/`GridCount`
  it compares the live count to `TriggerCount`; for `SimSpeed` it compares the
  simulation ratio (capped at 1.0) to `TriggerRatio`.
- For the count/sim triggers, `Interval` acts as a **re-check cooldown** after a
  run (default 60 s when left blank), so a satisfied condition does not restart
  the sequence every second.
- For `SimSpeed`, `TriggerCount` is the number of seconds the condition must hold
  continuously before the command fires (a debounce against momentary lag).
- A command will not start a second instance of **itself** while one is already
  running. Different commands (including ones started via `RunAuto`) run
  concurrently.
- Triggers are evaluated about once per second; running sequences advance every
  frame.

## Steps and actions

Each step has these fields:

| Field | Meaning |
|---|---|
| **Action** | What the step does (see table below). |
| **Command** | The command line (for `Command`), the message text (for `Announce`/`Notify`), or the auto-command name (for `RunAuto`). |
| **Color** | Optional colour for `Announce` — `R G B` (0–255) or a name like `Red`, `Cyan`, `Yellow`. Existing `Notify` steps use `Red`, `Green`, or `Blue` from this field when `NotifyFont` is `Default`. |
| **NotifyFont** | HUD font for `Notify`: `White`, `Red`, `Green`, `Blue`, or `DarkBlue`. `Default` keeps existing Color behavior, falling back to White. |
| **NotifyDurationMs** | `Notify` on-screen time in milliseconds (0 = 5000). |
| **ShellScript** | Optional shell command/script run **before** the action. |
| **ShellTimeoutSeconds** | Max seconds to wait for the shell script (0 = wait indefinitely). |
| **Delay** | Time to wait **after** this step before the next one (HH:MM:SS). |

| Action | Effect |
|---|---|
| **Command** | Runs the `Command` line as a server `!ess …` command (see the caveat below). |
| **Announce** | Sends `Command` to every player's chat, in `Color` if set. |
| **Notify** | Shows `Command` as a HUD notification to all players for `NotifyDurationMs`, using `NotifyFont`. |
| **Save** | Saves the world (no restart). |
| **ReloadConfig** | Saves, then reloads the dedicated-server config (MOTD etc.). |
| **Restart** | Saves and restarts the server process **immediately**. |
| **Stop** | Saves and stops the server process **immediately**. |
| **RunAuto** | Starts another auto command by name (`Command` holds the name). Chains sequences. |
| **None** | Does nothing — useful for a pure delay or a shell-only step. |

### Command-line steps run only this plugin's commands

The `Command` action dispatches the line through Essentials' own command pipeline
as the server (full permissions). **Only `!ess …` commands resolve** — the plugin
cannot inject lines into other plugins or the host. So `!ess broadcast Hi` works,
but a bare `!cleanup` from another plugin does not. Use the structured actions
(`Announce`, `Notify`, `Save`, `Restart`, …) for the common cases, and the
[`!ess` admin verbs](Commands.md#auto-commands--server-control) for the rest. A
leading `!` is optional in the `Command` field.

### Shell scripts

If `ShellScript` is set, it runs before the step's action. The sequence waits for
the process to exit before running the action and moving on — **without blocking
the server** (it is polled across frames). On Linux it runs via `/bin/sh -c`, on
Windows via `cmd.exe /c`. Set `ShellTimeoutSeconds` to bound a hung script; `0`
waits indefinitely. This is the extensibility escape hatch: back up a world,
ping an external API, post to Discord, etc., before continuing.

### Delays and timing

`Delay` is the wait **after** a step, before the next one, so the **first step
runs immediately**. A typical countdown
puts the announcement on the step and the gap on its delay:

```
Announce "Restart in 5 minutes"   Delay 00:04:00
Announce "Restart in 1 minute"    Delay 00:01:00
Restart
```

## Restart and shutdown sequences

The plugin can run a warning/countdown sequence when an admin asks the server to
restart or stop, instead of going down instantly.

1. Create an auto command (any name, trigger `Disabled` is fine) whose steps warn
   players and **end with a `Restart` (or `Stop`) action**.
2. Set **OnRestartSequence** / **OnShutdownSequence** (in the *Restart / Shutdown
   Sequences* section of the Auto Commands tab) to that command's name.

Now `!ess restart` runs the named countdown, and its final `Restart` step takes
the server down. `!ess stop` works the same way with `OnShutdownSequence`.

- `!ess restart now` / `!ess stop now` skips the sequence and acts immediately.
- When **no** sequence is configured, both commands act immediately.
- To avoid a loop, the final step uses the structured `Restart`/`Stop` action (or
  `!ess restart now`). A plain `!ess restart` issued **from inside a sequence**
  always restarts immediately, so it is also safe.

## Voting

`Vote`-triggered commands let players collectively trigger an auto command.

- `!ess vote <name>` starts a vote for the `Vote` command called `<name>` (and
  counts the starter as a *yes*).
- `!ess vote yes` / `!ess vote no` cast a vote; `!ess vote list` lists the
  configured vote commands.
- After **VoteDurationSeconds** (configurable, default 60) the vote tallies. It
  passes when `yes / (yes + no) ≥ TriggerRatio`, and the named command then runs.

Only one vote runs at a time.

## Admin commands

These `!ess` commands (also usable as `Command` steps) drive auto commands and
server lifecycle. See [Commands.md](Commands.md) for the full reference.

| Command | Purpose |
|---|---|
| `!ess runauto <name>` | Start an auto command now, regardless of its trigger. |
| `!ess cancelauto <name>` | Cancel running instances of an auto command. |
| `!ess listauto` | List configured and running auto commands. |
| `!ess say <message>` | Broadcast a plain chat message to all players. |
| `!ess broadcast <message>` | Broadcast a highlighted chat message. |
| `!ess notify <message> [ms] [font]` | Show a HUD notification to all players. |
| `!ess save` / `!ess reload` | Save the world / save and reload the dedicated config. |
| `!ess restart [now]` / `!ess stop [now]` | Restart / stop, via the configured sequence or immediately. |
| `!ess vote <name>` / `yes` / `no` / `list` | Player voting. |

## Examples

### Hourly information reminder (Timed)

```
Name     = Info Timer
Trigger  = Timed
Interval = 01:00:00
Steps:
  Action=Announce  Command="Type !ess help to see available commands"  Delay=00:00:00
```

### Nightly restart with a countdown (Scheduled)

```
Name      = Nightly Restart
Trigger   = Scheduled
Interval  = 04:00:00
DayOfWeek = All
Steps:
  Action=Announce Command="Server restarts in 5 minutes" Color="255 200 0" Delay=00:04:00
  Action=Announce Command="Server restarts in 1 minute"  Color="255 120 0" Delay=00:00:50
  Action=Notify   Command="Saving and restarting now"    NotifyFont=Red NotifyDurationMs=10000 Delay=00:00:10
  Action=Restart
```

Point **OnRestartSequence** at `Nightly Restart` to reuse it for `!ess restart`.

### Cleanup when simulation drops (SimSpeed → RunAuto)

```
Name         = LowSimCleanup
Trigger      = SimSpeed
Compare      = LessThan
TriggerRatio = 0.6          # fire when sim speed < 60%
TriggerCount = 30           # ...sustained for 30 seconds
Interval     = 00:10:00     # then wait 10 min before re-checking
Steps:
  Action=Announce Command="Low performance detected, running cleanup" Delay=00:00:05
  Action=RunAuto  Command="Cleanup"      # a separate Disabled command with the cleanup steps
```

### Backup before restart (shell script)

```
Steps:
  Action=Announce Command="Backing up the world" ShellScript="/opt/se/backup.sh" ShellTimeoutSeconds=300 Delay=00:00:02
  Action=Restart
```

## Converting Existing Sequences

The model maps almost one-to-one. The main differences:

- **Command lines must target `!ess …`.** Sequences that called other plugins'
  commands (`!cleanup`, `!voxels`, and similar) won't resolve. Replace common
  ones with structured actions (`Announce`, `Notify`, `Save`, `Restart`, `Stop`)
  or this plugin's `!ess` commands.
- `!admin runauto "X"` → `!ess runauto X`, or a `RunAuto` step with `Command=X`.
- `!say` / `!broadcast` / `!notify` → the `Announce` / `Notify` actions, or
  `!ess say` / `!ess broadcast` / `!ess notify`.
- `!restart` / `!stop` → the `Restart` / `Stop` actions, or `!ess restart` / `!ess
  stop` (configured as the restart/shutdown sequence).
- Older XML config files do **not** auto-import when their root model differs;
  re-create the sequences in the Auto Commands tab.

## Limits

- Auto commands act on the **global** chat channel and on all players.
- Command-line steps reach only this plugin's `!ess` commands.
- A 20-minute "delay" is a real 20-minute wait inside the sequence — keep an eye
  on overlapping schedules.
