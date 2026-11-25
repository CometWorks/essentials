"""The ship fixer: !ess fixship and !fixship for players (look target or name,
confirmation, cooldown, ownership, faction access, the off switch, occupied
seats, projectors) and the moderator variants fixshipmod and fixshipmodid.

A fixed grid is cut and pasted, so it comes back under a new entity id; the
tests find it by name."""

from __future__ import annotations

import re
import time

import pytest

import prefabs
import world
from conftest import wait_until
from session import FRONT, distance, offset, parse

PROMPT = re.compile(
    r"Are you sure you want to continue\? Enter the command again within (\d+) seconds to confirm fixship on (.+)\."
)


def wreck(owner=world.PLAYER_B, name="Wreck", **kwargs) -> prefabs.Grid:
    g = prefabs.Grid(name, FRONT, static=False, slab=(3, 1, 3), owner=owner, **kwargs)
    g.add("reactor", (0, 1, 0))
    return g


def look(player, g: prefabs.Grid) -> None:
    target = g.top(dy=-g.cell_m / 2)
    player.stand_and_look(offset(target, dz=10, dy=1.5), target)


def fixed(game, g: prefabs.Grid, blocks: int = 10) -> int:
    """Waits until the grid was replaced by a pasted copy; returns its id"""

    def copy():
        ids = game.named(g.name)
        return ids and ids != [g.entity_id] and ids[0]

    new_id = wait_until(copy, 15, f"{g.name} pasted again")
    assert not game.exists(g)
    assert game.grid(new_id)["blocks"] == blocks
    return new_id


def test_fixship_by_look(game):
    g = wreck()
    game.spawn(g)
    look(game.b, g)
    match = PROMPT.fullmatch(game.b.ess("fixship"))
    assert match and match.groups() == ("30", "Wreck")
    assert game.exists(g)
    assert game.b.ess("fixship") == "Ship was fixed!"
    new_id = fixed(game, g)
    assert distance(game.grid(new_id)["pos"], g.position) < 3


def test_fixship_by_name_and_the_short_form(game):
    g = wreck()
    game.spawn(g)
    assert PROMPT.fullmatch(game.b.ess("fixship Wreck"))
    # The short form shares the confirmation
    assert game.b.command("!fixship Wreck") == "Ship was fixed!"
    fixed(game, g)


def test_fixship_cooldown(game):
    game.configure(ShipFixerCooldownInSeconds=600)
    g = wreck()
    game.spawn(g)
    game.b.ess("fixship Wreck")
    game.b.ess("fixship Wreck")
    fixed(game, g)
    reply = game.b.ess("fixship Wreck")
    assert re.fullmatch(
        r"Command is still on cooldown for 59\d seconds\.|Command is still on cooldown for 600 seconds\.",
        reply,
    ), reply


def test_fixship_without_cooldown(game):
    game.configure(ShipFixerCooldownInSeconds=0)
    g = wreck()
    game.spawn(g)
    for _ in range(2):
        game.b.ess("fixship Wreck")
        assert game.b.ess("fixship Wreck") == "Ship was fixed!"


def test_fixship_confirmation_window(game):
    game.configure(ShipFixerConfirmationInSeconds=2)
    g = wreck()
    game.spawn(g)
    assert PROMPT.fullmatch(game.b.ess("fixship Wreck")).group(1) == "2"
    time.sleep(3)
    assert PROMPT.fullmatch(game.b.ess("fixship Wreck"))
    assert game.exists(g)


def test_fixship_ownership(game):
    game.spawn(wreck(owner=world.PLAYER_A, name="Alpha Wreck"))
    assert (
        game.b.ess('fixship "Alpha Wreck"')
        == "Grid seems to be owned by a different player."
    )


def test_fixship_on_a_grid_of_nobody(game):
    g = wreck(owner=0, name="Derelict")
    game.spawn(g)
    game.b.ess("fixship Derelict")
    assert game.b.ess("fixship Derelict") == "Ship was fixed!"


def test_fixship_faction_access(game):
    game.spawn(wreck(owner=world.PLAYER_C, name="Charlie Wreck"))
    assert (
        game.b.ess('fixship "Charlie Wreck"')
        == "Grid seems to be owned by a different player."
    )
    game.configure(ShipFixerFactionEnabled=True)
    assert PROMPT.fullmatch(game.b.ess('fixship "Charlie Wreck"'))


def test_fixship_switched_off(game):
    game.configure(ShipFixerPlayerCommandEnabled=False)
    game.spawn(wreck())
    assert game.b.ess("fixship Wreck") == "This command was disabled for players use!"
    assert (
        game.b.command("!fixship Wreck") == "This command was disabled for players use!"
    )


def test_fixship_not_found(game):
    assert game.b.ess("fixship Nothing") == "Grid not found"
    game.b.look_at(offset(game.b.position(), dy=100))
    assert game.b.ess("fixship") == "Grid not found"


@pytest.fixture
def occupied(game):
    g = prefabs.Grid(
        "Bus",
        offset(FRONT, 0, -1.25, 0),
        static=False,
        slab=(3, 1, 3),
        owner=world.PLAYER_B,
    )
    # Shared with all, so A may sit in B's seat
    seat = g.add("seat", (1, 1, 1), share="All")
    game.spawn(g)
    game.a.sit(g.world_of((1, 1, 1)), seat)
    return g


def test_fixship_refuses_occupied_grids(game, occupied):
    assert (
        game.b.ess("fixship Bus")
        == "Cockpits or seats are still occupied! Clear them first and try again."
    )


def test_fixship_ejects_when_allowed(game, occupied):
    game.configure(ShipFixerEjectPlayers=True)
    game.b.ess("fixship Bus")
    assert game.b.ess("fixship Bus") == "Ship was fixed!"
    wait_until(lambda: game.a.state()["seat"] == "0", 10, "A out of the seat")
    assert game.a.state()["dead"] == "0"


@pytest.mark.parametrize("remove", [False, True])
def test_fixship_and_projectors(game, remove):
    from test_blocks import projector_xml

    game.configure(ShipFixerRemoveBlueprintsFromProjectors=remove)
    g = wreck()
    blueprint = prefabs.Grid("Blueprint", (0.0, 0.0, 0.0), slab=(1, 1, 1))
    g.add("projector", (2, 1, 2), extra=projector_xml(blueprint))
    game.spawn(g)
    assert projector_of(game, g.entity_id) == {"enabled": "1", "blueprints": "1"}
    game.b.ess("fixship Wreck")
    assert game.b.ess("fixship Wreck") == "Ship was fixed!"
    new_id = fixed(game, g, blocks=11)
    # The projector is switched off; its blueprint goes only when asked to
    assert projector_of(game, new_id) == {
        "enabled": "0",
        "blueprints": "0" if remove else "1",
    }


def projector_of(game, grid_id: int) -> dict:
    return parse(game.a.probe("gridprojector", grid_id))


def test_fixshipmod_needs_no_ownership_or_cooldown(game):
    game.spawn(wreck(owner=world.PLAYER_B, name="Any Wreck"))
    for _ in range(2):
        assert PROMPT.fullmatch(game.a.ess('fixshipmod "Any Wreck"'))
        assert game.a.ess('fixshipmod "Any Wreck"') == "Ship was fixed!"


def test_fixshipmod_by_look(game):
    g = wreck()
    game.spawn(g)
    look(game.a, g)
    game.a.ess("fixshipmod")
    assert game.a.ess("fixshipmod") == "Ship was fixed!"
    fixed(game, g)


def test_fixshipmodid(game):
    g = wreck()
    game.spawn(g)
    assert PROMPT.fullmatch(game.a.command(f"!fixshipmodid {g.entity_id}"))
    assert game.a.ess(f"fixshipmodid {g.entity_id}") == "Ship was fixed!"
    fixed(game, g)
    assert game.a.ess("fixshipmodid 0") == "Correct Usage is !ess fixshipmodid EntityID"
    assert game.a.ess("fixshipmodid 12345") == "Grid not found"


@pytest.mark.parametrize("parallel", [False, True])
@pytest.mark.parametrize("state", ["active", "warming", "zone_off", "block_off"])
def test_fixship_preserves_safe_zone(game, state, parallel):
    game.configure(ShipFixerInParallel=parallel)
    station = prefabs.Grid(
        "Station", (200, 0, -100), slab=(6, 1, 4), owner=world.PLAYER_B
    )
    station.add("battery", (3, 1, 0))
    has_zone = state in ("active", "warming")
    warming = state == "warming"
    # The zone lives in the world separately from its generator, as in a save.
    zone_id = 0
    if has_zone:
        reply = game.a.probe(
            "safezone",
            *station.world_of((0, 1, 0)),
            37,
            station.blocks["battery"] + 1,
            str(not warming).lower(),
            station.entity_id,
        )
        zone_id = int(reply.split(": ")[1])
    station.add(
        "safezone",
        (0, 1, 0),
        extra=f"""
        <Enabled>{str(state != "block_off").lower()}</Enabled><SafeZoneId>{zone_id}</SafeZoneId>
        <ComponentContainer><Components><ComponentData><TypeId>MySafeZoneComponent</TypeId>
        <Component xsi:type="MyObjectBuilder_SafeZoneComponent">
        <UpkeepTime>60000</UpkeepTime>
        <SafeZoneOb xsi:type="MyObjectBuilder_SafeZone"><Radius>37</Radius>
        <Entities><long>{station.entity_id}</long></Entities></SafeZoneOb>
        <Activating>{str(warming).lower()}</Activating><ActivationTime>12000</ActivationTime>
        </Component></ComponentData></Components></ComponentContainer>""",
    )
    game.spawn(station)
    assert PROMPT.fullmatch(game.b.command("!fixship Station"))
    before = parse(game.a.probe("blockzone", station.entity_id))
    assert game.b.command("!fixship Station") == "Ship was fixed!"
    new_id = fixed(game, station, blocks=26)
    after = parse(game.a.probe("blockzone", new_id))
    for snapshot in (before, after):
        assert snapshot["blockEnabled"] == str(state != "block_off")
        assert snapshot["cachedListed"] == "True"
        assert snapshot["linked"] == str(has_zone)
        assert snapshot["enabled"] == str(state == "active")
        assert snapshot["protected"] == str(state == "active")
        assert snapshot["warming"] == str(warming)
        assert snapshot["listed"] == str(has_zone)
        assert snapshot["radius"] == ("37" if has_zone else "0")
    assert before["credit"] == after["credit"]
    if has_zone:
        assert int(after["id"]) != zone_id
        assert game.a.probe("zoneinfo", zone_id) == "Probe zoneinfo: none"
        assert abs(float(after["upkeep"]) - float(before["upkeep"])) < 3000
    else:
        assert after["id"] == "0"
    if warming:
        assert 0 < int(after["remaining"]) <= int(before["remaining"])
        assert int(before["remaining"]) - int(after["remaining"]) < 3000
        wait_until(
            lambda: parse(game.a.probe("blockzone", new_id))["protected"] == "True",
            15,
            "restored warm-up to finish",
        )
