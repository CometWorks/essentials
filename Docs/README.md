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
| **General** | Enable the plugin, matchmaking tags, grid-list output, stop-on-start, voxel deformation controls, and the economy stations' refill. |
| **MOTD** | Connect messages and the Steam-overlay URL, with new-user variants. |
| **Cleanup** | Empty-backpack limit and grid look notifications. |
| **PCU Tools** | PCU transfer limit checking (BlockLimits integration). |
| **Ship Fixer** | `fixship` cooldown, confirmation window, projector/eject behaviour. |
| **Auto Commands** | The auto-command list plus the restart/shutdown sequences and vote duration — see [Auto Commands](AutoCommands.md). |
| **Homes** | Player homes and limits. |
| **Info Commands** | Custom player commands with chat, center-screen notification, dialog and URL responses — see [Chat Commands](Commands.md#homes--info). |

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

### Economy stations

Every economy tick (`EconomyTickInSeconds`, 600 s by default) the game refills
each NPC trade station grid. It fills reactors with fuel, charges batteries,
fills gas tanks, adds 10,000 ice to every gas generator, and fills turrets with
ammo. It also puts the station type's loot (tools, datapads, Space Credits) into
every empty cargo container and cryo chamber, and hides them all from inventory
screens.

The **General → Economy Stations** section takes this over when
**StationRefillEnabled** is on. Each part has its own switch, and loot goes only
into the cargo containers and cryo chambers named **StationLootContainerName**
(case-insensitive). Only those are hidden; other containers keep their setting.
With the name empty, nothing gets loot or is hidden. Off, the game's own refill
runs unchanged.

This matters for stations whose cargo players can use: with the vanilla refill,
anyone can empty a shared container and find new loot after the next tick.
