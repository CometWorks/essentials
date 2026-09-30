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
top-level `!stone` command. The complete command reference lives in
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
2. Run `setup.py` to rename the template artifacts and auto-detect local reference paths.
3. If auto-detection fails, set `Magnetar` and `Dedicated64` in `Directory.Build.props`.
4. Build `Essentials.sln` in `Release`.
5. Deploy the server plugin through Magnetar.

### Plugin version

The plugin version lives in `Version.Build.props`, which **is** committed and imported by
`Directory.Build.props`. Keeping the version separate from the local path overrides means it
is shared by all contributors and stays under version control. Bump the version there.

### Folder path overrides

`Directory.Build.props.template` is a template for `Directory.Build.props`. The latter is a
local config file you can use to override the reference folder paths (`Magnetar` for the
plugin loader and `Dedicated64` for the Dedicated Server). It is **not committed** to the
repository, so each contributor keeps their own local paths.

`setup.py` copies `Directory.Build.props.template` to `Directory.Build.props` if the latter
does not exist yet, then fills in the auto-detected paths. Because the override is not
committed, anyone else who clones the repo and runs `setup.py` gets their own
`Directory.Build.props` with paths properly auto-detected for their machine. Leaving a path
empty in `Directory.Build.props` falls back to the platform-specific auto-detection further
down in the same file (Windows and Linux), so the build works on both operating systems.

## Project Layout

- `ServerPlugin` contains the Magnetar plugin entry point and deployment scripts.
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

## Publishing

Register server plugins in [MagnetarHub](https://github.com/viktor-ferenczi/MagnetarHub), so they become available in Magnetar.
