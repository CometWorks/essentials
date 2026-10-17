"""Safe zone access lists, GPS hiding and deleting, and NPC economy stations:
zone, ez hide/show/delete, place station, isecon, sywavefix, fixstation and
fixallstations."""

from __future__ import annotations

import re

import pytest

import prefabs
import world
from conftest import confirm, wait_until
from session import FRONT, HOME, offset, parse

# --- safe zones --------------------------------------------------------------


@pytest.fixture
def zone(game):
    return int(
        game.a.probe("safezone", *offset(HOME, dz=-30), 60).removeprefix(
            "Probe safezone: "
        )
    )


def zone_info(game, zone_id: int) -> dict:
    return parse(game.a.probe("zoneinfo", zone_id))


def test_zone_players_and_factions(game, zone):
    assert game.a.ess("zone add player Bravo") == "Added."
    wait_until(
        lambda: zone_info(game, zone)["players"] == str(world.PLAYER_B),
        10,
        "B on the list",
    )
    assert game.a.ess("zone add fac ETF") == "Added."
    wait_until(
        lambda: zone_info(game, zone)["factions"] == world.FACTION_TAG,
        10,
        "ETF on the list",
    )
    assert game.a.ess("zone remove player Bravo") == "Removed."
    assert game.a.ess("zone remove faction ETF") == "Removed."
    wait_until(
        lambda: zone_info(game, zone) == {"players": "", "factions": ""},
        10,
        "empty lists",
    )


def test_zone_bad_input(game, zone):
    assert (
        game.a.ess("zone grant player Bravo")
        == "Could not read input. Use add or remove."
    )
    assert (
        game.a.ess("zone add ship Bravo") == "Could not read input. Use player or fac."
    )
    assert game.a.ess("zone add player Zulu") == "Could not find that player."
    assert game.a.ess("zone add fac NOPE") == "Could not find that faction."
    game.a.teleport((0.0, 2000.0, 0.0))
    assert game.a.ess("zone add player Bravo") == "Cannot find a safe zone within 500m."


# --- GPS ---------------------------------------------------------------------


def gps(game) -> dict[str, str]:
    body = game.a.probe("gps", world.PLAYER_B).removeprefix("Probe gps:").strip()
    return dict(p.split("=") for p in body.split()) if body else {}


@pytest.fixture
def signals(game):
    for name in ("Mine Alpha", "Mine Beta", "Base"):
        game.a.probe("addgps", world.PLAYER_B, f'"{name}"', 0, 0, 100)
    wait_until(lambda: len(gps(game)) == 3, 10, "three GPS entries")


def test_ez_hide_show_delete(game, signals):
    assert game.b.ess("ez hide mine") == "Hiding 2 signal(s)."
    wait_until(
        lambda: gps(game) == {"Base": "1", "Mine_Alpha": "0", "Mine_Beta": "0"},
        10,
        "hidden mines",
    )
    assert game.b.ess("ez show all") == "Showing 3 signal(s)."
    wait_until(lambda: set(gps(game).values()) == {"1"}, 10, "all shown")
    assert game.b.ess("ez delete Beta") == "Deleting 1 signal(s)."
    wait_until(lambda: set(gps(game)) == {"Base", "Mine_Alpha"}, 10, "Beta gone")
    assert game.b.ess("ez delete ALL") == "Deleting 2 signal(s)."
    wait_until(lambda: gps(game) == {}, 10, "no signals")


def test_ez_only_touches_the_callers_signals(game, signals):
    assert game.a.ess("ez delete all") == "Deleting 0 signal(s)."
    assert len(gps(game)) == 3


def test_ez_needs_a_filter(game):
    assert game.b.ess("ez hide") == "Usage: !ess ez hide <name|all>"
    assert game.b.ess("ez show") == "Usage: !ess ez show <name|all>"
    assert game.b.ess("ez delete") == "Usage: !ess ez delete <name|all>"


# --- economy stations --------------------------------------------------------

PIRATES = "SPRT"


def store_grid(name: str, position) -> prefabs.Grid:
    """An NPC grid with a store block, the kind the station commands look for"""
    g = prefabs.Grid(name, position, slab=(3, 1, 3), owner=world.NPC)
    g.add("store", (1, 1, 1))
    return g


def stations(game, tag=PIRATES) -> int:
    return int(parse(game.a.probe("factioninfo", tag))["stations"])


def test_place_station(game):
    before = stations(game)
    reply = game.a.ess(f"place station {PIRATES} Outposts")
    assert re.fullmatch(
        rf"Registered Outpost station '.+' for {PIRATES} at .+\.", reply
    ), reply
    assert stations(game) == before + 1


def test_place_station_bad_input(game):
    assert (
        game.a.ess("place station NOPE SpaceStations") == "Could not find that faction."
    )
    assert game.a.ess(f"place station {PIRATES} Moonbase") == (
        "Cannot find that type. Use SpaceStations, OrbitalStations, Outposts or MiningStations."
    )


def test_isecon_and_sywavefix(game):
    assert (
        game.b.ess("isecon") == "No nearby NPC station grids with store blocks found."
    )
    trader = store_grid("Trader", FRONT)
    fake = store_grid("Fake Trader", offset(FRONT, 30))
    game.spawn(trader, fake)
    game.a.probe("station", PIRATES, trader.entity_id, *trader.position)
    assert game.b.ess("isecon").splitlines() == [
        "Fake Trader: not an economy station",
        "Trader: economy station",
    ]
    assert game.a.ess("sywavefix").splitlines() == [
        "Fake Trader: not an economy station",
        "Trader: economy station",
    ]


def test_fixstation_without_a_safe_zone(game):
    assert game.a.ess("fixstation") == "Cannot find nearby NPC station grids."
    game.spawn(store_grid("Stray Trader", FRONT))
    prompt, result = confirm(game.a, "fixstation")
    assert (
        prompt
        == "This will delete 1 nearby NPC station grid(s). Run the command again within 30 seconds to confirm."
    )
    assert result == "Deleted 1 nearby NPC station grid(s)."
    assert game.named("Stray Trader") == []


def test_fixstation_keeps_the_registered_grid(game):
    original = store_grid("Original", offset(FRONT, dz=-60))
    duplicate = store_grid("Duplicate", offset(FRONT, 40, 0, -60))
    game.spawn(original, duplicate)
    game.a.probe("safezone", *original.position, 50)
    assert game.a.ess("fixstation") == "Could not find the registered station grid."
    game.a.probe("station", PIRATES, original.entity_id, *original.position)
    prompt, result = confirm(game.a, "fixstation")
    assert prompt == (
        "Ensure no player grids are connected. This will delete 1 duplicate station grid(s). "
        "Run the command again within 30 seconds to confirm."
    )
    assert result == "Deleted 1 duplicate station grid(s)."
    assert game.exists(original)
    assert not game.exists(duplicate)


def test_fixallstations(game):
    assert game.a.ess("fixallstations") == "No duplicate station grids found."
    original = store_grid("Registered", (3000.0, 0.0, 0.0))
    duplicate = store_grid("Copy", (3040.0, 0.0, 0.0))
    far = store_grid("Far Away", (3500.0, 0.0, 0.0))
    game.spawn(original, duplicate, far)
    game.a.probe("station", PIRATES, original.entity_id, *original.position)
    prompt, result = confirm(game.a, "fixallstations")
    assert (
        prompt
        == "This will delete 1 duplicate station grid(s). Run the command again within 30 seconds to confirm."
    )
    assert result == "Deleted 1 duplicate station grid(s)."
    assert game.exists(original) and game.exists(far)
    assert not game.exists(duplicate)
