# Essentials Documentation

Documentation for the Essentials Magnetar server plugin for Space Engineers.

## Contents

- **[Auto Commands](AutoCommands.md)** — timed, scheduled and triggered server
  command sequences: restart countdowns, cleanup passes, MOTD reminders, player
  votes and shell-script hooks.
- **[Chat Commands](Commands.md)** — reference for every `!ess …` command:
  auto-command/server control, voting, blocks, economy, ship fixer, and
  PCU/ownership.

## Configuration

All settings are edited in the Quasar web UI, generated from the plugin's
`PluginConfig` schema (`Shared/Config`). The tabs are:

| Tab | What it covers |
|---|---|
| **General** | Enable the plugin, matchmaking tags, grid-list output, stop-on-start, and voxel deformation controls. |
| **MOTD** | Connect messages and the Steam-overlay URL, with new-user variants. |
| **Cleanup** | Empty-backpack limit and grid look notifications. |
| **PCU Tools** | PCU transfer limit checking (BlockLimits integration). |
| **Ship Fixer** | `fixship` cooldown, confirmation window, projector/eject behaviour. |
| **Auto Commands** | The auto-command list plus the restart/shutdown sequences and vote duration — see [Auto Commands](AutoCommands.md). |
| **Homes** | Player homes and limits. |
| **Info Commands** | Custom player commands with chat, dialog and URL responses — see [Chat Commands](Commands.md#homes--info). |

![Config dialog example](ConfigDialogExample.png)

### Voxel deformation

The **General → Voxel Deformation** section has independent switches for missile
explosions, meteor impacts on grids, and grid collisions. All are off by default,
so vanilla deformation remains enabled. The switches suppress voxel cutouts while
leaving explosion damage, grid damage, and collision physics to the game. A hard
impact can therefore still damage or embed a grid; test collision protection with
your server's usual ships and speeds before enabling it broadly. Admins can also
change the switches in game, for example with `!ess voxels protect missiles on`,
and inspect them with `!ess voxels protect status`.
