# Essentials

Essentials is a Space Engineers dedicated server plugin for Magnetar. It bundles
common quality-of-life server behaviour, configured through the Magnetar
`PluginSdk` with the UI rendered by Quasar from the generated schema.

## Features

- **Auto commands** — timed, scheduled and triggered server command sequences:
  restart/shutdown countdowns, cleanup passes, MOTD reminders, player votes, and
  shell-script hooks. See [Docs/AutoCommands.md](Docs/AutoCommands.md).
- **Server control** — save, reload config, restart and stop, with `!ess` chat
  commands and warning sequences.
- **MOTD** — connect messages and a Steam-overlay URL, with first-time variants.
- **Ship fixer** — cut/paste a grid to clear physics issues, with cooldowns.
- **Voxel deformation controls** — independent switches for missile, meteor and
  grid collision voxel cutouts.
- **Blocks, economy, PCU/ownership transfer, Stone cleanup, grid conversion,
  safezone, GPS and station maintenance** utilities.

See [Docs/](Docs/README.md) for the full documentation and the
[chat command reference](Docs/Commands.md).

## Command Coverage

Essentials currently exposes **160** `!ess` chat commands plus the legacy
top-level `!stone` command and the short forms `!fixship`, `!fixshipmod`,
`!fixshipmodid` and `!reload`. The complete command reference lives in
[Docs/Commands.md](Docs/Commands.md); the main groups are:

- **Admin/player QoL** — MOTD, stats, player count, player list, promote level,
  reserved slots, item give, kick/ban/unban, teleport, mute/unmute and private
  messages.
- **Lookup/admin info** — `getsteamid`, `listids`, `listnames`, `updatename`,
  `lastlogin`, `isnpc`, `getfacid` and `worldpcu`.
- **Homes & info** — `home add`, `home del`, `home list`, `home goto` and
  `info list`.
- **Auto commands & voting** — `runauto`, `cancelauto`,
  `admin cancelautobyindex`, `listauto`, `vote`, `vote yes`, `vote no`,
  `vote list`, `vote cancel`, `vote debug` and `vote reset`.
- **Cleanup & world maintenance** — cleanup scan/list/delete, floating object
  cleanup, identity clean/purge/clear, reputation wipe, faction clean/remove/info
  and sandbox cleanup.
- **Voxels** — reset all, cleanup asteroids/distant asteroids, reset planets,
  reset one planet, reset area and reset GPS area.
- **Entities & grids** — entity find/refresh/stop/delete/kill/power/eject, grid
  list/export/import/ejectall/stopall/static-large, plus `admin makeship`,
  `admin makestation`, `admin rename`, `protect`, `unprotect`, `convert` and
  `gridtype`.
- **Safe zones, GPS & NPC stations** — `zone`, `ez hide`, `ez show`,
  `ez delete`, `place station`, `fixallstations`, `fixstation`, `isecon` and
  `sywavefix`. Destructive station cleanup commands require confirmation.
- **Economy** — native `econ give/take/set/reset/top/check/pay`, `eco`
  player/faction aliases, faction wallet aliases (`eco givefac`, `eco takefac`),
  account reset commands (`eco resetbalances`, `eco resetplayers`,
  `eco resetfactions`, `eco resetplayer`, `eco resetfac`), physical
  `SpaceCredit` conversion (`eco deposit`, `eco withdraw`) and local account
  refresh/debug commands (`TestEconSync`, `FullEconSync`, `SingleEconSync`,
  `econ sync all`, `econ sync player`, `econ debug account`). `eco withdrawall`
  is present as a disabled compatibility command; use `eco withdraw <amount>`.
- **Ship fixer, PCU & ownership** — `fixship`, moderator fix commands,
  PCU/author/owner checks, claim/sell/accept/deny grid workflows, rename and
  PCU/ownership transfer commands.

## Prerequisites

- [Space Engineers Dedicated Server](https://store.steampowered.com/app/298740/Space_Engineers_Dedicated_Server/)
- [Magnetar](https://magnetar.se) - the Space Engineers server with plugin support
- [.NET Framework 4.8.1 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481)
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Python 3.12 or newer, for `setup.py`

## Setup

1. Clone the repository.
2. Build `Essentials.sln`. The Dedicated Server and the Magnetar installation are found
   automatically. If that fails, run `setup.py` or set `Dedicated64` and `Magnetar` in
   `Directory.Build.props.user`.

### Plugin version

The plugin version lives in `Version.Build.props`, which **is** committed and imported by
`Directory.Build.props`. Keeping the version separate from the local path overrides means it
is shared by all contributors and stays under version control. Bump the version there.

### Folder path overrides

`Directory.Build.props` declares the overridable folders with empty defaults: `Dedicated64`
(the Dedicated Server's `DedicatedServer64` folder), `Magnetar` (the Magnetar installation
folder holding `PluginSdk.dll`) and `MagnetarData` (the Magnetar config folder to deploy into).
Empty `Dedicated64` and `Magnetar` values are auto-detected on Windows and Linux.

To override them, put the first `PropertyGroup` of `Directory.Build.props` into
`Directory.Build.props.user`, wrapped into a `<Project>` element. That file is not committed.
`setup.py` writes it with the auto-detected Dedicated Server folder.

### Development and deployment

Load the working copy through a Magnetar development folder: start Magnetar with `-sources`
and add the repository with the Sources button. Magnetar then compiles the plugin from source.

Builds deploy nothing by default. To copy the build into `<MagnetarData>/Local`, set
`MagnetarData` in `Directory.Build.props.user` or pass `-p:MagnetarData=...` to the build.

## Project Layout

- `ServerPlugin` contains the Magnetar plugin entry point.
- `Shared` contains common plugin code, configuration, logging, and Harmony patches.
- `Essentials.xml` is the MagnetarHub plugin registration template.

## Documentation

- [Documentation index](Docs/README.md)
- [Auto Commands](Docs/AutoCommands.md) — scheduled/triggered command sequences
- [Chat Commands](Docs/Commands.md) — the full `!ess` command reference

## Configuration

Server configuration lives in `Shared/Config` and uses Magnetar `PluginSdk`.
The `PluginConfig` defaults are applied by the dedicated server plugin, and the
editor UI is rendered by Quasar from the generated schema. See the
[documentation index](Docs/README.md) for a tab-by-tab overview.

## Compatibility

Use the `EnsureCode` attribute on Harmony patch methods to safely skip loading the plugin when patched game code changes after a Space Engineers update.
The logged hash can be copied back into the attribute after validating a new game version.

## Tests

`tests/` holds an in-game test suite. It starts a Magnetar server with this working copy
of the plugin, joins it with two headless game clients and drives them through the
[Remote plugin](https://github.com/CometWorks/remote). Player A is an administrator.
Player B has no rights and shares a faction with player C, who never joins. The tests
type the chat commands, read what each client gets back (replies, broadcasts, HUD
notifications) and check the result on the server: grids, blocks, inventories, balances,
voxels, identities, factions, and the files the plugin writes. It runs on Linux only.

What it covers:

- Every command against its permission level, read from the source: B is refused
  everything above None and the refusal reaches only B; B promoted to Moderator,
  SpaceMaster and Admin (`test_permissions.py`)
- say, broadcast, notify, save, reload and `!reload` (`test_server_control.py`)
- Auto commands: runauto, listauto, cancelauto, cancel by index, every step action with
  delays and shell scripts, the Timed, Scheduled, PlayerCount, GridCount and SimSpeed
  triggers, and votes (`test_auto_commands.py`)
- Restart and shutdown sequences, restart and stop at once, the OnStart trigger, config,
  homes and grids across a restart, and a broken config file (`test_lifecycle.py`)
- Kick, ban, unban and the MOTD on joining (`test_joining.py`)
- MOTD, stats, player count and list, the id and name lookups, promote levels, reserved
  slots, item gifts, chat mutes, private messages and teleports (`test_players.py`)
- Homes and info commands (`test_homes_info.py`)
- Every cleanup condition, confirmations, piloted grids, logical groups and floating
  objects (`test_cleanup.py`)
- Identity clean, purge and clear, rep wipe, faction clean, remove and info, sandbox
  clean (`test_world_maintenance.py`)
- Voxel resets on a procedural asteroid and the missile, meteor and grid collision
  protection switches (`test_voxels.py`)
- Blocks on, off and remove by type, subtype and category (`test_blocks.py`)
- Entities and grids commands, grid conversion, protection, convert, gridtype and
  `!stone` (`test_grids.py`)
- Safe zones, GPS signals and NPC economy stations (`test_zones_stations.py`)
- The economy commands and SpaceCredit items (`test_economy.py`)
- The ship fixer and its options (`test_shipfixer.py`)
- PCU and ownership reports, claim, grid sales, rename and transfers (`test_pcu.py`)

Behavior the plugin documents but doesn't have yet, and bugs found by the suite, are
strict xfails that name their ticket, so a fix shows up as an unexpected pass.

Not covered: mission screens (the MOTD, `!ess help` and dialogs fall back to chat because
the clients run without MagnetarMod), the Steam overlay URL, planet resets (the test
world has no planets), PCU transfer limits (block limits are off in the test world), the
Block Limits plugin bridge and the Quasar config UI.

The test world is the game's Empty World with the test players and the faction, and no
grids. `tests/probe` is a test-only server plugin the server loads next to Essentials: it
spawns the grids each test needs (`tests/prefabs.py`), makes identities, factions,
asteroids and safe zones, reports the state of the world on the server, puts a new config
into the running plugin and clears the plugin's cooldowns, confirmations, mutes, homes and
votes between tests. So the server restarts only in `test_lifecycle.py`.

### Running

You need the Space Engineers client and Dedicated Server from Steam, Magnetar and Pulsar
installed, [uv](https://docs.astral.sh/uv/), and working copies of
[Remote](https://github.com/CometWorks/remote) and
[DirectTransport](https://github.com/CometWorks/direct-transport). The rig takes the
ports, client ids and folders from a test slot of the `se` workspace
(`notes/parallel-test-work`), and waits for free memory before each process it starts.

```bash
uv run pytest -n 6
```

`-n` is the number of rigs that run side by side, each a server and two clients on a
slot of its own, with about 5 GB of RAM per rig. The memory guard starts the processes
20 seconds apart, so the last rig is up after about 6 minutes; the first ones start on
the tests meanwhile. A full run with `-n 6` takes about 15 minutes on a 32-core machine with
128 GB of RAM. Without `-n`, one rig runs all the tests in turn.

Where GitHub's API rate limits Magnetar, export `PULSAR_GITHUB_TOKEN` for it to download
its hub plugins with. `!fixship` with a grid name needs a Magnetar newer than 2.4.3.4.

`tests/rig.py` lists the environment variables that point the rig at other installations
and working copies. To keep the server and clients running between runs while you work
on a test:

```bash
uv run python tests/session.py start
ES_ATTACH=1 ES_KEEP=1 uv run pytest tests/test_cleanup.py
uv run python tests/session.py stop
```

## Publishing

Register server plugins in [MagnetarHub](https://github.com/viktor-ferenczi/MagnetarHub), so they become available in Magnetar.
