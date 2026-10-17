"""Entity and grid administration: entities find/stop/delete/kill/power/eject/
refresh, grids list/export/import/ejectall/stopall/static large, admin
makeship/makestation/rename, protect/unprotect, convert, gridtype and !stone.

Commands that take the looked-at grid get it from the server's view of the
character's head, so the tests turn the character with Remote first."""

from __future__ import annotations

import re
import time

import pytest

import prefabs
import rig
import world
from conftest import confirm, wait_until
from session import FRONT, distance, offset, parse


def look_at_grid(player, g: prefabs.Grid, stand=None) -> None:
    """Stands 8 m in front of the grid (towards +z) and looks at its middle"""
    target = g.top(dy=-g.cell_m / 2)
    player.stand_and_look(stand or offset(target, dz=10, dy=1.5), target)


def station(
    name="Depot", owner=world.PLAYER_A, position=FRONT, **kwargs
) -> prefabs.Grid:
    g = prefabs.Grid(name, position, slab=(3, 1, 3), owner=owner, **kwargs)
    g.add("battery", (1, 1, 1))
    return g


# --- entities ----------------------------------------------------------------


def test_entities_find(game):
    g = station("Find Me")
    game.spawn(g)
    assert (
        game.a.ess('entities find "find me"')
        == f"Found 1 entities:\nFind Me ({g.entity_id})"
    )
    assert game.a.ess("entities find Zulu") == "Found 0 entities matching 'Zulu'."


def test_entities_find_voxels_by_storage_name(game):
    reply = parse(game.a.probe("asteroid", 3, 20, 0, 0, -3000))
    lines = game.a.ess("entities find ProcAsteroid").splitlines()
    assert f"{reply['name']} ({reply['id']})" in lines, lines


def test_entities_stop(game):
    g = prefabs.Grid("Drifter", FRONT, static=False, slab=(2, 1, 2))
    game.spawn(g)
    game.a.probe("velocity", g.entity_id, 0, 0, -20)
    time.sleep(1)
    assert game.a.ess("entities stop Drifter") == "Entity 'Drifter' stopped."
    time.sleep(1)
    assert max(abs(v) for v in game.grid(g)["vel"]) < 0.1


def test_entities_delete(game):
    g = station("Delete Me")
    game.spawn(g)
    prompt, result = confirm(game.a, 'entities delete "Delete Me"')
    assert prompt == (
        f"This will delete entity 'Delete Me' ({g.entity_id}). "
        "Run the same command again within 30 seconds to confirm."
    )
    assert result == "Entity 'Delete Me' deleted."
    assert not game.exists(g)


def test_entities_delete_refuses_characters(game):
    character = game.b.state()["character"]
    assert game.a.ess(f"entities delete {character}") == "You cannot delete characters."
    assert game.a.ess("entities delete Zulu") == "Entity 'Zulu' not found."


def test_entities_kill(game):
    a_before = game.a.mark()
    assert game.a.ess("entities kill Bravo") == "Killed Bravo."
    game.a.wait_line(
        a_before,
        lambda s, t: s == "Server" and t == "Bravo was killed by an admin",
        what="the kill notice",
    )
    wait_until(
        lambda: game.b.state()["dead"] == "1" or game.b.state()["character"] == "0",
        10,
        "B dead",
    )
    rig.ensure_character(game.b.api)
    rig.focus_gameplay(game.b.api)
    game.b.go_home()


def test_entities_kill_bad_input(game):
    g = station("Not Alive")
    game.spawn(g)
    assert game.a.ess("entities kill Zulu") == "Entity 'Zulu' not found."
    assert (
        game.a.ess('entities kill "Not Alive"')
        == "Entity 'Not Alive' is not a character."
    )


def test_entities_power(game):
    g = prefabs.Grid("Plant", FRONT, slab=(5, 1, 5))
    g.add("reactor", (0, 1, 0))
    g.add("battery", (2, 1, 0))
    g.add("cargo", (4, 1, 0))
    game.spawn(g)
    assert (
        game.a.ess("entities poweroff Plant")
        == "Disabled 2 of 2 power block(s) on 'Plant'."
    )
    assert game.block(g.blocks["reactor"])["enabled"] == "0"
    assert game.block(g.blocks["battery"])["enabled"] == "0"
    assert (
        game.a.ess("entities poweroff Plant")
        == "Disabled 0 of 2 power block(s) on 'Plant'."
    )
    assert (
        game.a.ess("entities poweron Plant")
        == "Enabled 2 of 2 power block(s) on 'Plant'."
    )
    assert game.block(g.blocks["reactor"])["enabled"] == "1"


def test_entities_power_bad_input(game):
    assert game.a.ess("entities poweroff Zulu") == "Entity 'Zulu' not found."
    character = game.a.state()["character"]
    assert re.fullmatch(
        r"Entity '.+' is not a grid\.", game.a.ess(f"entities poweron {character}")
    )


@pytest.fixture
def seated_b(game):
    g = prefabs.Grid(
        "Bus",
        offset(FRONT, 0, -1.25, -20),
        static=False,
        slab=(3, 1, 3),
        owner=world.PLAYER_B,
    )
    seat = g.add("seat", (1, 1, 1))
    game.spawn(g)
    game.b.sit(g.world_of((1, 1, 1)), seat)
    return g


def test_entities_eject_player(game, seated_b):
    assert game.a.ess("entities eject Bravo") == "Player 'Bravo' ejected."
    wait_until(lambda: game.b.state()["seat"] == "0", 10, "B out of the seat")
    assert (
        game.a.ess("entities eject Bravo")
        == "Player 'Bravo' is not online/seated, or could not be found seated offline."
    )


@pytest.mark.xfail(
    strict=True,
    reason="eject all changes the entity list while enumerating it, SE1-0243",
)
def test_entities_eject_all(game, seated_b):
    assert game.a.ess("entities eject all") == "Ejected 1 player(s) from their seats."
    wait_until(lambda: game.b.state()["seat"] == "0", 10, "B out of the seat")


def test_grids_ejectall(game, seated_b):
    assert game.a.ess("grids ejectall Bus") == "Ejected 1 player(s) from their seats."
    wait_until(lambda: game.b.state()["seat"] == "0", 10, "B out of the seat")
    assert (
        game.a.ess("grids ejectall Nowhere")
        == "Grid with name 'Nowhere' was not found."
    )


def test_entities_refresh(game):
    reply = game.b.ess("entities refresh")
    assert re.fullmatch(r"Forced replication of \d+ entities\.", reply), reply
    assert re.fullmatch(
        r"Cooldown active\. You can use this command again in \d+ seconds\.",
        game.b.ess("entities refresh"),
    )
    # The client keeps working
    time.sleep(3)
    assert game.b.api.get_state().get("ready")
    assert game.b.ess("home list") == "You do not have any homes."


# --- grids list, export and import -------------------------------------------


def test_grids_list(game):
    assert game.b.ess("grids list") == "You own no grids."
    game.spawn(
        station("Bravo Base", owner=world.PLAYER_B),
        station("Alpha Base", position=offset(FRONT, 30)),
    )
    assert game.b.ess("grids list").splitlines() == [
        "Ships/stations owned by Bravo:",
        "Bravo Base - Large - 10 block(s) - Position Unknown",
    ]


def test_grids_list_positions_and_gps(game):
    game.configure(UtilityShowPosition=True, MarkerShowPosition=True)
    g = station("Bravo Base", owner=world.PLAYER_B)
    game.spawn(g)
    line = game.b.ess("grids list").splitlines()[1]
    assert re.fullmatch(
        r"Bravo Base - Large - 10 block\(s\) - Position X:.*Y:.*Z:.*", line
    ), line
    wait_until(
        lambda: "Bravo_Base=1" in game.a.probe("gps", world.PLAYER_B),
        10,
        "the GPS entry",
    )


def test_grids_export_and_import(game):
    g = station("Exported")
    game.spawn(g)
    path = game.server.exports / "depot.xml"
    assert game.a.ess("grids export Exported depot") == f"Grid saved to {path}"
    assert "<DisplayName>Exported</DisplayName>" in path.read_text(encoding="utf-8")
    assert (
        game.a.ess("grids export Exported depot.xml") == "Export file already exists."
    )

    game.a.probe("clear")
    assert game.a.ess("grids import depot") == f"Importing grid from {path}"
    wait_until(lambda: game.named("Exported"), 10, "the imported grid")
    imported = game.grid(game.named("Exported")[0])
    assert distance(imported["pos"], game.a.position()) < 100
    assert imported["blocks"] == 10


def test_grids_export_stays_in_its_folder(game):
    game.spawn(station("Sneaky"))
    reply = game.a.ess("grids export Sneaky ../../escape")
    assert reply == f"Grid saved to {game.server.exports / 'escape.xml'}"
    assert not (game.server.data / "escape.xml").exists()


def test_grids_import_bad_input(game):
    assert game.a.ess("grids import missing") == "File does not exist."
    assert game.a.ess("grids import missing Zulu") == "Target entity not found."
    assert game.a.ess("grids export Zulu file") == "Entity 'Zulu' not found."


# --- stopping and converting -------------------------------------------------


def test_grids_stopall(game):
    ships = [
        prefabs.Grid(f"Mover {i}", offset(FRONT, 30 * i), static=False, slab=(2, 1, 2))
        for i in range(2)
    ]
    game.spawn(*ships)
    for g in ships:
        game.a.probe("velocity", g.entity_id, 0, 5, 0)
    time.sleep(1)
    assert game.a.ess("grids stopall") == "Stopped 2 grid(s)."
    time.sleep(1)
    for g in ships:
        assert max(abs(v) for v in game.grid(g)["vel"]) < 0.1


def test_grids_static_large(game):
    large = prefabs.Grid("Large Ship", FRONT, static=False, slab=(2, 1, 2))
    small = prefabs.Grid(
        "Small Ship", offset(FRONT, 30), static=False, size="Small", slab=(2, 1, 2)
    )
    game.spawn(large, small)
    prompt, result = confirm(game.a, "grids static large")
    assert (
        prompt
        == "This will convert 1 large ship grid(s) to stations. Run the same command again within 30 seconds to confirm."
    )
    assert result == "Converted 1 large grid(s) to stations."
    wait_until(lambda: game.grid(large)["static"], 10, "the station")
    assert not game.grid(small)["static"]
    assert game.a.ess("grids static large") == "No large ship grids found."


def test_admin_makeship_and_makestation(game):
    g = station("Flip")
    game.spawn(g)
    assert game.a.ess("admin makeship Flip") == "Converting 1 grid(s) to ships."
    wait_until(lambda: not game.grid(g)["static"], 10, "a ship")
    assert (
        game.a.ess("admin makeship Flip")
        == "No station grids found in the target group."
    )
    assert game.a.ess("admin makestation Flip") == "Converting 1 grid(s) to stations."
    wait_until(lambda: game.grid(g)["static"], 10, "a station")
    assert (
        game.a.ess("admin makestation Flip")
        == "No ship grids found in the target group."
    )
    assert game.a.ess("admin makeship Zulu") == "Could not find the Grid."


def test_admin_makeship_by_look(game):
    g = station("Looked At")
    game.spawn(g)
    look_at_grid(game.a, g)
    assert game.a.ess("admin makeship") == "Converting 1 grid(s) to ships."


def test_admin_rename(game):
    g = station("Old Name")
    game.spawn(g)
    assert (
        game.a.ess('admin rename "Old Name" New Name')
        == "Renaming Old Name to New Name. You may need to relog to see changes."
    )
    wait_until(lambda: game.grid(g)["name"] == "New_Name", 10, "the new name")
    assert (
        game.a.ess('admin rename "New Name"')
        == "Usage: !ess admin rename <gridName> <newName>"
    )
    assert game.a.ess("admin rename Zulu Yankee") == "Could not find the Grid."


def test_gridtype(game):
    game.spawn(
        station("Fixed"),
        prefabs.Grid("Floating", offset(FRONT, 30), static=False, slab=(2, 1, 2)),
    )
    assert game.b.ess("gridtype Fixed") == "Fixed: STATION"
    assert game.b.ess("gridtype Floating") == "Floating: SHIP"
    assert game.b.ess("gridtype Zulu") == "Could not find the Grid."


def test_duplicate_names_are_refused(game):
    game.spawn(station("Twin"), station("Twin", position=offset(FRONT, 30)))
    assert (
        game.a.ess("gridtype Twin")
        == "Found multiple Grids with same Name. Make sure the name is unique."
    )


# --- protection --------------------------------------------------------------


def protection(game, g) -> tuple[bool, bool]:
    state = game.grid(g)
    return state["destructible"], state["editable"]


def test_protect_and_unprotect(game):
    g = station("Shielded")
    game.spawn(g)
    assert (
        game.a.ess("protect Shielded")
        == "Protected 1 grid(s): damage blocked, editing blocked."
    )
    assert protection(game, g) == (False, False)
    assert (
        game.a.ess("protect Shielded -allowDamage")
        == "Protected 1 grid(s): damage allowed, editing blocked."
    )
    assert protection(game, g) == (True, False)
    assert (
        game.a.ess("protect Shielded -allowEdit")
        == "Protected 1 grid(s): damage blocked, editing allowed."
    )
    assert protection(game, g) == (False, True)
    assert (
        game.a.ess("unprotect Shielded")
        == "Unprotected 1 grid(s): damage and editing allowed."
    )
    assert protection(game, g) == (True, True)


def test_protect_by_look(game):
    g = station("Watched")
    game.spawn(g)
    look_at_grid(game.a, g)
    assert (
        game.a.ess("protect") == "Protected 1 grid(s): damage blocked, editing blocked."
    )
    assert protection(game, g) == (False, False)


def test_protect_bad_input(game):
    game.spawn(station("Shielded"))
    assert (
        game.a.ess("protect Shielded -forever")
        == "Unknown option '-forever'. Usage: !ess protect [gridName] [-allowDamage] [-allowEdit]"
    )
    assert game.a.ess("protect Zulu") == "Could not find the Grid."
    assert game.a.ess("unprotect Zulu") == "Could not find the Grid."


# --- convert -----------------------------------------------------------------


def test_convert_own_grid(game):
    g = station("Bravo Rig", owner=world.PLAYER_B)
    game.spawn(g)
    look_at_grid(game.b, g)
    assert game.b.ess("convert") == "Converting 1 grid(s) to ships."
    wait_until(lambda: not game.grid(g)["static"], 10, "a ship")
    look_at_grid(game.b, g)
    assert game.b.ess("convert") == "Converting 1 grid(s) to stations."
    wait_until(lambda: game.grid(g)["static"], 10, "a station")


def test_convert_refusals(game):
    others = station("Alpha Rig")
    small = prefabs.Grid(
        "Bravo Skiff",
        offset(FRONT, 40),
        static=False,
        size="Small",
        slab=(3, 1, 3),
        owner=world.PLAYER_B,
    )
    small.add("small_reactor", (1, 1, 1))
    game.spawn(others, small)
    look_at_grid(game.b, others)
    assert (
        game.b.ess("convert")
        == "Not enough shared ownership on 'Alpha Rig'. Accessible owned blocks: 0/1."
    )
    look_at_grid(game.b, small)
    assert game.b.ess("convert") == "Small grids cannot be converted to stations."
    game.b.look_at(offset(game.b.position(), dy=100))
    assert game.b.ess("convert") == "Could not find the Grid."


def test_convert_refuses_protected_grids(game):
    g = station("Locked Rig", owner=world.PLAYER_B)
    game.spawn(g)
    game.a.ess('protect "Locked Rig"')
    look_at_grid(game.b, g)
    assert game.b.ess("convert") == "Grid 'Locked Rig' is admin protected."


# --- !stone ------------------------------------------------------------------


@pytest.fixture
def quarry(game):
    """A grid of B with stone and iron in a cargo container"""
    g = station("Quarry", owner=world.PLAYER_B)
    cargo = g.add("cargo", (0, 1, 0))
    game.spawn(g)
    game.a.probe("additem", cargo, "Ore", "Stone", 500)
    game.a.probe("additem", cargo, "Ore", "Iron", 100)
    return g, cargo


def test_stone_deletes_stone_only(game, quarry):
    g, cargo = quarry
    look_at_grid(game.b, g)
    assert game.b.command("!stone") == "500 Stone Deleted."
    assert game.inventory(cargo) == {"Ore/Iron": 100.0}


def test_stone_cooldown(game, quarry):
    g, cargo = quarry
    look_at_grid(game.b, g)
    game.b.command("!stone")
    game.a.probe("additem", cargo, "Ore", "Stone", 50)
    assert re.fullmatch(
        r"Command is still on cooldown for \d+ seconds\.", game.b.command("!stone")
    )
    assert game.inventory(cargo)["Ore/Stone"] == 50.0


def test_stone_without_cooldown(game, quarry):
    game.configure(StoneCooldownInSeconds=0)
    g, cargo = quarry
    look_at_grid(game.b, g)
    game.b.command("!stone")
    game.a.probe("additem", cargo, "Ore", "Stone", 50)
    # The total counts every !stone since the server started
    lines = game.b.command("!stone true").splitlines()
    assert lines[0] == "50 Stone Deleted."
    assert re.fullmatch(
        r"\d+ Stone Deleted in total on this instance\.", lines[1]
    ), lines


def test_stone_skips_grids_of_others(game):
    g = station("Alpha Quarry")
    cargo = g.add("cargo", (0, 1, 0))
    game.spawn(g)
    game.a.probe("additem", cargo, "Ore", "Stone", 10)
    look_at_grid(game.b, g)
    assert game.b.command("!stone") == "0 Stone Deleted; skipped 1 unowned grid(s)."
    assert game.inventory(cargo) == {"Ore/Stone": 10.0}


def test_stone_on_a_faction_grid(game):
    g = station("Charlie Quarry", owner=world.PLAYER_C)
    cargo = g.add("cargo", (0, 1, 0))
    game.spawn(g)
    game.a.probe("additem", cargo, "Ore", "Stone", 10)
    look_at_grid(game.b, g)
    assert game.b.command("!stone") == "10 Stone Deleted."


def test_stone_from_a_seat(game):
    g = prefabs.Grid(
        "Hauler",
        offset(FRONT, 0, -1.25, -20),
        static=False,
        slab=(3, 1, 3),
        owner=world.PLAYER_B,
    )
    seat = g.add("seat", (1, 1, 1))
    cargo = g.add("cargo", (0, 1, 0))
    game.spawn(g)
    game.a.probe("additem", cargo, "Ore", "Stone", 25)
    game.b.sit(g.world_of((1, 1, 1)), seat)
    assert game.b.command("!stone") == "25 Stone Deleted."


def test_stone_with_nothing_in_sight(game):
    game.b.look_at(offset(game.b.position(), dy=100))
    assert game.b.command("!stone") == "No grid found in your line of sight."
