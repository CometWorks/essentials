# Chat Commands

Most Essentials commands live under the **`!ess`** prefix and are typed in the
global chat (or run from the server console). The player-only `!stone` command
keeps its legacy top-level name. The host suppresses recognised command lines
from normal chat.

- `!ess` — overview of available commands.
- `!ess help` — list commands; `!ess help <command>` shows usage for one.
- `!help` — list every server command across all plugins.

**Arguments:** `<required>`, `[optional]`. Quote `"multi word"` values. Booleans
accept `true/false`, `yes/no`, `on/off`, `1/0`.

**Permissions** use Space Engineers promote levels, lowest to highest:
`None` < `Scripter` < `Moderator` < `SpaceMaster` < `Admin` < `Owner`. A caller
below a command's level does not see it in the overview. Commands without an
explicit level require **Admin**.

## Auto commands & server control

Drive [auto command](AutoCommands.md) sequences and the server lifecycle.

| Command                                     | Permission | Description                                                               |
| ------------------------------------------- | ---------- | ------------------------------------------------------------------------- |
| `!ess say <message>`                        | Admin      | Broadcast a plain chat message to all players.                            |
| `!ess broadcast <message>`                  | Admin      | Broadcast a highlighted (yellow) chat message.                            |
| `!ess notify <message> [durationMs] [font]` | Admin      | HUD notification to all players (default 5000 ms, font `White`).          |
| `!ess save`                                 | Admin      | Save the world.                                                           |
| `!ess reload`                               | Admin      | Save, then reload the dedicated-server config (MOTD etc.).                |
| `!ess restart [now]`                        | Admin      | Run the configured restart sequence, or restart immediately with `now`.   |
| `!ess stop [now]`                           | Admin      | Run the configured shutdown sequence, or stop immediately with `now`.     |
| `!ess runauto <name>`                       | Admin      | Start an auto command now, regardless of its trigger.                     |
| `!ess cancelauto <name>`                    | Admin      | Cancel running instances of an auto command.                              |
| `!ess admin cancelautobyindex <index>`      | Admin      | Cancel one running auto command by the 1-based index shown in `listauto`. |
| `!ess listauto`                             | Admin      | List configured and running auto commands.                                |

### Voting

| Command            | Permission | Description                                                  |
| ------------------ | ---------- | ------------------------------------------------------------ |
| `!ess vote <name>` | None       | Start a vote for the `Vote`-triggered auto command `<name>`. |
| `!ess vote yes`    | None       | Vote yes on the current vote.                                |
| `!ess vote no`     | None       | Vote no on the current vote.                                 |
| `!ess vote list`   | None       | List the configured vote commands.                           |
| `!ess vote cancel` | Admin      | Cancel the current vote.                                     |
| `!ess vote debug`  | Admin      | Show current and previous vote state.                        |
| `!ess vote reset`  | Admin      | Clear current and previous vote state.                       |

## Player & admin QoL

MOTD opens through the Plugin SDK mission-screen API when MagnetarMod is
available; otherwise it falls back to private chat output.

| Command                                                       | Permission  | Description                                                                             |
| ------------------------------------------------------------- | ----------- | --------------------------------------------------------------------------------------- |
| `!ess motd`                                                   | None        | Show the configured MOTD and MOTD URL.                                                  |
| `!ess stats`                                                  | Admin       | Show server runtime stats, sim speed, entity counts, PCU and memory.                    |
| `!ess admin playercount [count]`                              | Admin       | Show or set the max player count.                                                       |
| `!ess playerlist`                                             | Admin       | List online players with identity id, Steam id and promote level.                       |
| `!ess getsteamid <name\|steamId\|identityId> [online]`        | None        | Look up Steam and identity ids for matching identities.                                 |
| `!ess listids`                                                | None        | List online players with Steam ids.                                                     |
| `!ess listnames`                                              | None        | List online players whose Steam and identity names differ.                              |
| `!ess updatename <player> <newName>`                          | Admin       | Update an identity display name.                                                        |
| `!ess lastlogin <player>`                                     | Admin       | Show an identity's last login time.                                                     |
| `!ess isnpc <player>`                                         | Admin       | Report whether an identity is an NPC.                                                   |
| `!ess getfacid <tag>`                                         | Admin       | Show a faction id.                                                                      |
| `!ess worldpcu`                                               | Admin       | Show total world PCU.                                                                   |
| `!ess admin setrank <player> <rank>`                          | Admin       | Set a player's promote level (`None`, `Scripter`, `Moderator`, `SpaceMaster`, `Admin`). |
| `!ess admin reserve <player>`                                 | Admin       | Add a player/Steam ID to reserved slots.                                                |
| `!ess admin unreserve <player>`                               | Admin       | Remove a player/Steam ID from reserved slots.                                           |
| `!ess admin give <player\|*> <itemType> <subtype> <quantity>` | Admin       | Add an item to one online player or all online players.                                 |
| `!ess kick <player>`                                          | Moderator   | Kick a player from the game.                                                            |
| `!ess ban <player\|steamId>`                                  | Moderator   | Ban a player/Steam ID.                                                                  |
| `!ess unban <player\|steamId>`                                | Moderator   | Remove a player/Steam ID ban.                                                           |
| `!ess tp [entityToMove] [destination]`                        | SpaceMaster | Teleport one entity to another; omitted values default to the caller.                   |
| `!ess tpto <destination> [entityToMove]`                      | SpaceMaster | Teleport yourself or another entity to a destination entity.                            |
| `!ess tphere <entityToMove>`                                  | SpaceMaster | Teleport an entity to the caller.                                                       |
| `!ess mute <player> [minutes]`                                | Admin       | Mute a player in chat; `0` or omitted means indefinite.                                 |
| `!ess unmute <player>`                                        | Admin       | Remove a chat mute.                                                                     |
| `!ess list mute`                                              | Admin       | List muted players and remaining time.                                                  |
| `!ess msg <player> <message>`                                 | None        | Send a private message to an online player. Quote names with spaces.                    |
| `!ess whis <player> <message>`                                | None        | Alias for `msg`.                                                                        |

## Homes & info

Homes are stored per Steam ID in `Essentials.Homes.xml` under the server user-data
folder. Limits and enablement are configured on the **Homes** tab.

| Command                 | Permission | Description                                     |
| ----------------------- | ---------- | ----------------------------------------------- |
| `!ess home add <name>`  | None       | Save your current character position as a home. |
| `!ess home del <name>`  | None       | Delete one saved home.                          |
| `!ess home list`        | None       | List your saved homes.                          |
| `!ess home goto <name>` | None       | Teleport to a saved home.                       |
| `!ess info list`        | None       | List configured info command names.             |

The **Info Commands** tab defines public, top-level commands such as `!rules`
or `!fixship`. Each row needs a **Command** name and at least one response:
**ChatResponse** sends private chat text, **NotificationResponse** shows a
center-screen notification only to the player using the command,
**DialogResponse** opens a mission screen (or falls back to private chat when
mission screens are unavailable), and **URL** sends an HTTP(S) link in chat and
opens it in the Steam overlay. **NotificationDurationMs** sets that row's
notification display time in milliseconds; `0` uses the default of `5000`.
Set multiple response fields to use them together. Empty rows are ignored.
Names are case-insensitive, and any words after the command name are ignored.
Registered commands from Magnetar or other plugins take precedence; `!ess`,
`!stone`, and `!help` cannot be used as custom info names.

## Blocks

Toggle or remove functional blocks across all (non-projected) grids. Block
**type** is the object-builder type without the `MyObjectBuilder_` prefix
(e.g. `Reactor`, `ShipWelder`); **subtype** is the block subtype id.

| Command                                | Permission | Description                                           |
| -------------------------------------- | ---------- | ----------------------------------------------------- |
| `!ess blocks on type <type>`           | Admin      | Enable all blocks of a type.                          |
| `!ess blocks off type <type>`          | Admin      | Disable all blocks of a type.                         |
| `!ess blocks on subtype <subtype>`     | Admin      | Enable all blocks of a subtype.                       |
| `!ess blocks off subtype <subtype>`    | Admin      | Disable all blocks of a subtype.                      |
| `!ess blocks remove type <type>`       | Admin      | Remove all blocks of a type.                          |
| `!ess blocks remove subtype <subtype>` | Admin      | Remove all blocks of a subtype.                       |
| `!ess blocks on general <category>`    | Admin      | Enable a category: `Power`, `Production`, `Weapons`.  |
| `!ess blocks off general <category>`   | Admin      | Disable a category: `Power`, `Production`, `Weapons`. |

## Cleanup & world maintenance

Destructive cleanup and world-maintenance commands require running the same
command again within 30 seconds to confirm. Cleanup grid commands evaluate
logical grid groups and skip piloted grids unless `haspilot` is included.

Cleanup conditions include: `name <regex>`, `blockslessthan <count>`,
`blocksgreaterthan <count>`, `pcugreaterthan <pcu>`, `pculessthan <pcu>`,
`hasgridtype <large|small|ship|static>`, `hasownertype <npc|player|nobody>`,
`haspower` / `nopower`, `insideplanet`, `playerdistancelessthan <meters>` /
`playerdistancegreaterthan <meters>`, `poweredgriddistancegreaterthan <meters>`,
`centerdistancelessthan <meters>` / `centerdistancegreaterthan <meters>`,
`ownedby <player|identityId|steamId|nobody|npc|pirates>`, `hastype <type>` /
`notype <type>`, `hastype-fast <type[,type...]>` /
`notype-fast <type[,type...]>`, `hassubtype <subtype>` /
`nosubtype <subtype>`, `hassubtype-fast <subtype[,subtype...]>` /
`nosubtype-fast <subtype[,subtype...]>`, and `haspilot`.

The Cleanup tab also has **Grid Look Notifications**. Add a row with cleanup
condition arguments (for example, `blockslessthan 20 hasownertype player`) and
a message such as `Please repair or move {GridName}; it matches a cleanup rule.`
Only a player listed in the grid's BigOwners sees the HUD notice, when looking
directly at that grid from their character within 500 m. Rules use the same
logical-group matching and default pilot exclusion as `!ess cleanup scan`.
The first matching row is shown. Set **CleanupLookReminderMinutes** to choose
how often the player is reminded (1–1440 minutes, default 1). After the first
notice, reminders continue while the player is online, owns the grid, remains
within 500 m of its position, and its rule still matches, even after they look
away. A reminder that no longer qualifies is cleared; looking at a matching
grid again starts a new notice. Between reminders the plugin
does not raycast or evaluate the rule for that player. At a reminder time, a
different matching grid the player is looking at takes priority. Initial look
checks run at most once every three seconds. Empty rows have no effect.

| Command                                                          | Permission  | Description                                                                                    |
| ---------------------------------------------------------------- | ----------- | ---------------------------------------------------------------------------------------------- |
| `!ess cleanup scan [conditions...]`                              | Admin       | Count grids matching cleanup conditions.                                                       |
| `!ess cleanup list [conditions...]`                              | Admin       | List matching grids with block count, PCU and entity id.                                       |
| `!ess cleanup delete [conditions...]`                            | Admin       | Delete matching grids after confirmation.                                                      |
| `!ess cleanup delete floatingobjects`                            | Admin       | Delete all floating objects after confirmation.                                                |
| `!ess cleanup help`                                              | Admin       | List cleanup conditions.                                                                       |
| `!ess identity clean <days> [includeNpcs]`                       | SpaceMaster | Remove old identities and preserve/reassign their grids. `includeNpcs` defaults to `false`.    |
| `!ess identity purge <days> [includeNpcs]`                       | SpaceMaster | Remove old identities and close grids solely owned by them. `includeNpcs` defaults to `false`. |
| `!ess identity clear <player>`                                   | Admin       | Remove one identity and close grids solely owned by it.                                        |
| `!ess rep wipe [removePlayerToFaction] [removeFactionToFaction]` | Admin       | Wipe reputation relation data after confirmation. Both flags default to `true`.                |
| `!ess faction clean [memberCount]`                               | Admin       | Remove factions with fewer than `memberCount` valid members (default `1`).                     |
| `!ess faction remove <tag>`                                      | Admin       | Remove a faction by tag.                                                                       |
| `!ess faction info [tag]`                                        | Admin       | List factions and their members.                                                               |
| `!ess sandbox clean`                                             | SpaceMaster | Clean stale identities, factions, GPS/camera/procedural data and block ownership.              |

## Voxels

Voxel reset commands restore storage from the original data provider. Destructive
commands require running the same command again within 30 seconds to confirm.
Protection commands change the corresponding Quasar settings immediately and save
them to the Essentials configuration. Use `on` or `off` to set protection.

| Command                                      | Permission | Description                                                                                    |
| -------------------------------------------- | ---------- | ---------------------------------------------------------------------------------------------- |
| `!ess voxels protect status`                 | Admin      | Show missile, meteor and grid collision voxel protection settings.                             |
| `!ess voxels protect missiles <enabled>`     | Admin      | Enable or disable voxel protection from missile explosions.                                    |
| `!ess voxels protect meteors <enabled>`      | Admin      | Enable or disable voxel protection from meteor impacts on grids.                               |
| `!ess voxels protect collisions <enabled>`   | Admin      | Enable or disable voxel protection from grid collisions.                                       |
| `!ess voxels reset all`                      | Admin      | Reset all voxel maps and planets.                                                              |
| `!ess voxels cleanup asteroids`              | Admin      | Reset asteroid voxel maps with no grid or character nearby.                                    |
| `!ess voxels cleanup distant [distance]`     | Admin      | Reset asteroid voxel maps with no grid or character within `distance` meters (default `1000`). |
| `!ess voxels reset planets`                  | Admin      | Reset all planets.                                                                             |
| `!ess voxels reset planet <planetName>`      | Admin      | Reset one planet matched by storage name.                                                      |
| `!ess voxels reset area <radius>`            | Admin      | Reset voxel damage within `radius` meters of the caller.                                       |
| `!ess voxels reset gps <x> <y> <z> <radius>` | Admin      | Reset voxel damage within `radius` meters of a GPS point.                                      |

## Entities & grids

Find, stop, delete and power-toggle entities, plus common grid administration.
`entities delete` and `grids static large` require running the same command again
within 30 seconds to confirm. `[gridName]` defaults to the grid the caller is
looking at where supported. For `protect`, `-allowDamage` leaves block damage
enabled and `-allowEdit` leaves building/removal enabled; using both is equivalent
to `unprotect`.

| Command                                     | Permission  | Description                                                                                                |
| ------------------------------------------- | ----------- | ---------------------------------------------------------------------------------------------------------- |
| `!ess entities find <name>`                 | SpaceMaster | Find entities by display name or voxel storage name.                                                       |
| `!ess entities refresh`                     | None        | Force a replication refresh for the caller.                                                                |
| `!ess entities stop <entityName>`           | SpaceMaster | Clear an entity's linear/angular speed.                                                                    |
| `!ess entities delete <entityName>`         | SpaceMaster | Delete a non-character entity after confirmation.                                                          |
| `!ess entities kill <playerOrCharacter>`    | SpaceMaster | Kill an online player or character entity.                                                                 |
| `!ess entities poweroff <gridName>`         | SpaceMaster | Disable reactors, batteries, solar panels and hydrogen engines on a grid.                                  |
| `!ess entities poweron <gridName>`          | SpaceMaster | Enable reactors, batteries, solar panels and hydrogen engines on a grid.                                   |
| `!ess entities eject <playerName\|all>`     | Admin       | Eject one seated player, or every seated player with `all`.                                                |
| `!ess grids list`                           | None        | List grids owned by the caller; position/GPS output follows config.                                        |
| `!ess grids export <gridName> <fileName>`   | SpaceMaster | Export a grid XML file under `Essentials/ExportedGrids`.                                                   |
| `!ess grids import <fileName> [targetName]` | SpaceMaster | Import a grid XML file near the caller or target entity.                                                   |
| `!ess grids ejectall [gridName]`            | SpaceMaster | Eject all pilots from a named or targeted mechanical grid group.                                           |
| `!ess grids stopall`                        | SpaceMaster | Clear speed on all non-projected grids.                                                                    |
| `!ess grids static large`                   | SpaceMaster | Convert all large ship grids to stations after confirmation.                                               |
| `!ess admin makeship [gridName]`            | Admin       | Convert a named or looked-at physical grid group to ships.                                                 |
| `!ess admin makestation [gridName]`         | Admin       | Convert a named or looked-at physical grid group to stations.                                              |
| `!ess admin rename <gridName> <newName>`    | Admin       | Rename a grid without ownership checks.                                                                    |
| `!ess protect [gridName] [-allowDamage] [-allowEdit]` | SpaceMaster | Protect a named or looked-at mechanical grid group. By default, damage and editing are blocked.            |
| `!ess unprotect [gridName]`                 | SpaceMaster | Allow damage and editing again on a named or looked-at mechanical grid group.                              |
| `!ess convert`                              | None        | Toggle the looked-at owned/faction-accessible grid group between ship and station outside natural gravity. |
| `!ess gridtype [gridName]`                  | None        | Report whether target grids are ships or stations.                                                         |
| `!stone [outputCount]`                      | None        | Delete Stone ore from inventories on the controlled grid or looked-at owned/faction grid group.            |

## Safe zones, GPS & economy stations

| Command                                             | Permission | Description                                                                                                                      |
| --------------------------------------------------- | ---------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `!ess zone <add\|remove> <player\|fac> <name\|tag>` | Admin      | Add or remove a player/faction from a safe zone within 500m.                                                                     |
| `!ess ez hide <name\|all>`                          | None       | Hide matching GPS signals.                                                                                                       |
| `!ess ez show <name\|all>`                          | None       | Show matching GPS signals.                                                                                                       |
| `!ess ez delete <name\|all>`                        | None       | Delete matching GPS signals.                                                                                                     |
| `!ess place station <npcTag> <type>`                | Admin      | Register an NPC economy station at the caller position. Types: `SpaceStations`, `OrbitalStations`, `Outposts`, `MiningStations`. |
| `!ess fixallstations`                               | Admin      | Delete duplicate NPC station grids around registered stations after confirmation.                                                |
| `!ess fixstation`                                   | Admin      | Delete duplicate NPC station grids near the caller after confirmation.                                                           |
| `!ess isecon`                                       | None       | Report whether nearby NPC station grids are registered economy stations.                                                         |
| `!ess sywavefix`                                    | Admin      | Admin alias for the nearby economy-station report.                                                                               |

## Economy

`<player>` is a player name or identity id. `*` affects all players. `onlyOnline`
limits to online players; `excludeNpcs` (default `true`) skips NPC identities.
`eco deposit` and `eco withdraw` convert physical `SpaceCredit` items between
accessible inventories on the looked-at grid group and the caller's account.

| Command                                                         | Permission | Description                                                                                     |
| --------------------------------------------------------------- | ---------- | ----------------------------------------------------------------------------------------------- |
| `!ess econ give <player> <amount> [onlyOnline] [excludeNpcs]`   | Admin      | Add credits.                                                                                    |
| `!ess econ take <player> <amount> [onlyOnline] [excludeNpcs]`   | Admin      | Remove credits.                                                                                 |
| `!ess econ set <player> <amount> [onlyOnline] [excludeNpcs]`    | Admin      | Set the balance.                                                                                |
| `!ess econ reset <player> [onlyOnline] [excludeNpcs]`           | Admin      | Reset the balance to 10,000.                                                                    |
| `!ess econ top [onlyOnline] [excludeNpcs]`                      | None       | List balances, highest first.                                                                   |
| `!ess econ check <player>`                                      | None       | Show a player's balance.                                                                        |
| `!ess econ pay <player> <amount>`                               | None       | Pay another online player from your account.                                                    |
| `!ess econ sync all`                                            | Admin      | Refresh all local player economy accounts at their current balances.                            |
| `!ess econ sync player <player>`                                | Admin      | Refresh one local player economy account at its current balance.                                |
| `!ess econ debug account <player\|tag>`                         | Admin      | Show local account state for a player or faction.                                               |
| `!ess TestEconSync`                                             | Admin      | Refresh the caller's local account at its current balance.                                      |
| `!ess FullEconSync` / `!ess fulleconsync`                       | Admin      | Refresh all local player accounts at current balances.                                          |
| `!ess SingleEconSync <player>` / `!ess singleeconsync <player>` | Admin      | Refresh one local player account at current balance.                                            |
| `!ess eco`                                                      | None       | List economy aliases.                                                                           |
| `!ess eco balance <player\|faction> <name\|tag>`                | Admin      | Show a player or faction balance.                                                               |
| `!ess eco top [limit] [factions]`                               | Admin      | List top player or faction balances.                                                            |
| `!ess eco give <player\|faction> <name\|tag\|*> <amount>`       | Admin      | Add credits through the economy alias.                                                          |
| `!ess eco take <player\|faction> <name\|tag\|*> <amount>`       | Admin      | Remove credits through the economy alias.                                                       |
| `!ess eco pay <player\|faction> <name\|tag> <amount>`           | None       | Pay a player or faction through the economy alias.                                              |
| `!ess eco giveplayer <player> <amount>`                         | Admin      | Alias for `econ give`.                                                                          |
| `!ess eco takeplayer <player> <amount>`                         | Admin      | Alias for `econ take`.                                                                          |
| `!ess eco givefac <tag> <amount>`                               | Admin      | Alias for `eco give faction`.                                                                   |
| `!ess eco takefac <tag> <amount>`                               | Admin      | Alias for `eco take faction`.                                                                   |
| `!ess eco resetbalances`                                        | Admin      | Reset player balances to the starting balance and faction balances to zero.                     |
| `!ess eco resetplayers`                                         | Admin      | Reset player balances to the starting balance.                                                  |
| `!ess eco resetfactions`                                        | Admin      | Reset faction balances to zero.                                                                 |
| `!ess eco resetplayer <player>`                                 | Admin      | Set one player's balance to zero.                                                               |
| `!ess eco resetfac <tag>`                                       | Admin      | Set one faction's balance to zero.                                                              |
| `!ess eco deposit [playerOwnedOnly]`                            | None       | Deposit physical `SpaceCredit` items from the caller and accessible looked-at grid inventories. |
| `!ess eco withdraw <amount>`                                    | None       | Withdraw credits as physical `SpaceCredit` items into an accessible cargo container.            |
| `!ess eco withdrawall`                                          | Admin      | Disabled compatibility command; use `eco withdraw <amount>`.                                    |

## Ship fixer

Cuts and re-pastes a grid to clear physics/clang issues. Behaviour (cooldown,
confirmation window, projector handling, ejecting players) is governed by the
**Ship Fixer** config tab; the player `!ess fixship` command can be disabled
there.

| Command                      | Permission | Description                                               |
| ---------------------------- | ---------- | --------------------------------------------------------- |
| `!ess fixship [gridName]`    | None       | Fix the grid you are looking at, or one by name.          |
| `!ess fixshipmod [gridName]` | Moderator  | Fix by look target or grid name (moderator, no cooldown). |
| `!ess fixshipmodid [gridId]` | Moderator  | Fix a grid by entity id.                                  |

## PCU & ownership

Inspect ownership/authorship and transfer PCU/ownership of a grid. `[gridName]`
defaults to the grid you are looking at. Transfers respect block limits unless a
`force…` variant is used. Player-facing claim/sale/rename workflows require the
caller to already own or have faction-share access to all owned functional blocks
in the target grid group.

| Command                                         | Permission  | Description                                                                            |
| ----------------------------------------------- | ----------- | -------------------------------------------------------------------------------------- |
| `!ess pcu checkowner [gridName]`                | Moderator   | Report block ownership on a grid.                                                      |
| `!ess pcu checkauthor [gridName]`               | Moderator   | Report PCU authorship on a grid.                                                       |
| `!ess pcucount`                                 | None        | Count PCU on the looked-at connected grid group, including projected PCU when present. |
| `!ess claim [shareWithFaction]`                 | None        | Claim the looked-at faction-shared mechanical grid group.                              |
| `!ess sellgrid <amount>`                        | None        | Offer the looked-at mechanical grid group to one nearby player for credits.            |
| `!ess acceptgrid`                               | None        | Accept a pending grid sale offer.                                                      |
| `!ess denygrid`                                 | None        | Deny a pending grid sale offer.                                                        |
| `!ess rename <gridName> <newName>`              | None        | Rename a grid you own or have faction-share access to.                                 |
| `!ess transfer [playerName] [gridName]`         | SpaceMaster | Transfer PCU and ownership to a player.                                                |
| `!ess forcetransfer [playerName] [gridName]`    | SpaceMaster | Same, ignoring limits.                                                                 |
| `!ess transferpcu [playerName] [gridName]`      | SpaceMaster | Transfer PCU only.                                                                     |
| `!ess forcetransferpcu [playerName] [gridName]` | SpaceMaster | Transfer PCU only, ignoring limits.                                                    |
| `!ess transferowner [playerName] [gridName]`    | SpaceMaster | Transfer ownership only.                                                               |
| `!ess transfernobody [gridName]`                | SpaceMaster | Remove PCU and ownership.                                                              |
| `!ess transferpcunobody [gridName]`             | SpaceMaster | Remove PCU.                                                                            |
| `!ess transferownernobody [gridName]`           | SpaceMaster | Remove ownership.                                                                      |
