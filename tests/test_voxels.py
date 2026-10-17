"""Voxel commands and the voxel deformation switches: protection status and
its three switches (saved to the config), resetting an area, a GPS point, all
voxels, unused and distant asteroids, planets; and missiles and grid
collisions that do or don't cut voxels depending on the switches.

The test asteroid is procedural, made by the probe, so it has a data
provider to reset from. The world has no planets."""

from __future__ import annotations

import time

import pytest

import prefabs
from conftest import confirm, wait_until
from session import offset, parse

# The asteroid sits far from the players' home
CENTER = (0.0, 0.0, -600.0)
RADIUS = 40.0
# A point on its +z face, towards the players
SURFACE = (0.0, 0.0, -600.0 + RADIUS * 0.6)


@pytest.fixture
def rock(game):
    reply = parse(game.a.probe("asteroid", 7, RADIUS, *CENTER))
    rock_id = int(reply["id"])
    wait_until(
        lambda: content(game, rock_id, CENTER, RADIUS * 2) > 0, 20, "the asteroid"
    )
    return rock_id, reply["name"]


def content(game, rock_id: int, center, radius: float) -> float:
    reply = game.a.probe("content", rock_id, *center, radius)
    return float(reply.removeprefix("Probe content: "))


def surface_point(game, rock_id: int):
    """The first point with voxel content from SURFACE towards the centre"""
    x, y, z = SURFACE
    while z > CENTER[2]:
        if content(game, rock_id, (x, y, z), 1) > 0:
            return (x, y, z)
        z -= 1.0
    raise AssertionError("No surface found")


def cut(game, rock_id: int, point, radius: float = 6.0) -> None:
    game.a.probe("cut", rock_id, *point, radius)


# --- protection switches -----------------------------------------------------


def test_protection_status_and_switches(game):
    assert (
        game.a.ess("voxels protect status")
        == "Voxel protection: missiles off, meteors off, grid collisions off."
    )
    assert game.a.ess("voxels protect missiles on") == "Missile voxel protection on."
    assert game.a.ess("voxels protect meteors yes") == "Meteor voxel protection on."
    assert (
        game.a.ess("voxels protect collisions 1")
        == "Grid collision voxel protection on."
    )
    assert (
        game.a.ess("voxels protect status")
        == "Voxel protection: missiles on, meteors on, grid collisions on."
    )
    saved = game.saved_cfg()
    for name in ("Missiles", "Meteors", "GridCollisions"):
        assert f"<ProtectVoxelsFrom{name}>true</ProtectVoxelsFrom{name}>" in saved, name
    assert (
        game.a.ess("voxels protect collisions off")
        == "Grid collision voxel protection off."
    )
    assert (
        "<ProtectVoxelsFromGridCollisions>false</ProtectVoxelsFromGridCollisions>"
        in game.saved_cfg()
    )


def test_protection_bad_input(game):
    reply = game.a.ess("voxels protect missiles maybe")
    assert "Usage: !ess voxels protect missiles" in reply, reply
    assert game.cfg("ProtectVoxelsFromMissiles") == "False"


# --- resets ------------------------------------------------------------------


@pytest.mark.xfail(
    strict=True, reason="RevertShape leaves the voxels as they are, SE1-0242"
)
def test_reset_area_around_the_caller(game, rock):
    rock_id, _ = rock
    point = surface_point(game, rock_id)
    full = content(game, rock_id, point, 8)
    cut(game, rock_id, point)
    wait_until(lambda: content(game, rock_id, point, 8) < full - 10, 10, "the cut")
    game.a.teleport(offset(point, dz=15))
    prompt, result = confirm(game.a, "voxels reset area 30")
    assert (
        prompt
        == "This will reset voxel damage on 1 voxel map(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == "Reset voxel damage on 1 voxel map(s)."
    wait_until(
        lambda: abs(content(game, rock_id, point, 8) - full) < 1, 10, "the reset"
    )


@pytest.mark.xfail(
    strict=True, reason="RevertShape leaves the voxels as they are, SE1-0242"
)
def test_reset_gps_point(game, rock):
    rock_id, _ = rock
    point = surface_point(game, rock_id)
    full = content(game, rock_id, point, 8)
    cut(game, rock_id, point)
    wait_until(lambda: content(game, rock_id, point, 8) < full - 10, 10, "the cut")
    _, result = confirm(game.a, "voxels reset gps {} {} {} 20".format(*point))
    assert result == "Reset voxel damage on 1 voxel map(s)."
    wait_until(
        lambda: abs(content(game, rock_id, point, 8) - full) < 1, 10, "the reset"
    )


def test_reset_area_bad_input(game, rock):
    assert game.a.ess("voxels reset area 0") == "Radius must be greater than 0."
    assert game.a.ess("voxels reset gps 0 0 0 -5") == "Radius must be greater than 0."
    assert (
        game.a.ess("voxels reset gps 0 5000 0 10")
        == "No voxel maps intersect that area."
    )
    assert game.a.ess("voxels reset area 10") == "No voxel maps intersect that area."


def damaged(game, rock_id: int):
    point = surface_point(game, rock_id)
    full = content(game, rock_id, point, 8)
    cut(game, rock_id, point)
    wait_until(lambda: content(game, rock_id, point, 8) < full - 10, 10, "the cut")
    return point, full


def test_cleanup_unused_asteroids(game, rock):
    rock_id, _ = rock
    point, full = damaged(game, rock_id)
    prompt, result = confirm(game.a, "voxels cleanup asteroids")
    assert (
        prompt
        == "This will reset 1 asteroid voxel map(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == "Reset 1 voxel map(s)."
    wait_until(
        lambda: abs(content(game, rock_id, point, 8) - full) < 1, 10, "the reset"
    )


def test_cleanup_skips_asteroids_with_someone_near(game, rock):
    rock_id, _ = rock
    game.a.teleport(offset(SURFACE, dz=10))
    assert game.a.ess("voxels cleanup asteroids") == "Found 0 asteroid voxel map(s)."


def test_cleanup_distant_asteroids(game, rock):
    rock_id, _ = rock
    point, full = damaged(game, rock_id)
    # The players are about 560 m from the asteroid's centre
    assert (
        game.a.ess("voxels cleanup distant 1000")
        == "Found 0 distant asteroid voxel map(s)."
    )
    prompt, result = confirm(game.a, "voxels cleanup distant 300")
    assert (
        prompt
        == "This will reset 1 distant asteroid voxel map(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == "Reset 1 voxel map(s)."
    wait_until(
        lambda: abs(content(game, rock_id, point, 8) - full) < 1, 10, "the reset"
    )
    assert game.a.ess("voxels cleanup distant 0") == "Distance must be greater than 0."


def test_reset_all(game, rock):
    rock_id, _ = rock
    point, full = damaged(game, rock_id)
    prompt, result = confirm(game.a, "voxels reset all")
    assert (
        prompt
        == "This will reset 1 voxel map(s) and planet(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == "Reset 1 voxel map(s)."
    wait_until(
        lambda: abs(content(game, rock_id, point, 8) - full) < 1, 10, "the reset"
    )


def test_planet_resets_without_planets(game):
    assert game.a.ess("voxels reset planets") == "Found 0 planet(s)."
    assert (
        game.a.ess("voxels reset planet Earth")
        == "Could not find planet matching 'Earth'."
    )


# --- deformation switches ----------------------------------------------------


def total(game, rock_id: int) -> float:
    return content(game, rock_id, CENTER, RADIUS * 2)


def fire_missile(game, rock_id: int) -> float:
    """Fires one missile at the asteroid from 150 m and returns how much voxel
    content it took away"""
    launcher = prefabs.Grid(
        "Launcher", offset(CENTER, -2.5, -1.25, 150.0), slab=(3, 1, 3)
    )
    gun = launcher.add("launcher", (1, 1, 0))
    launcher.add("battery", (0, 1, 2))
    game.spawn(launcher)
    time.sleep(2)
    before = total(game, rock_id)
    assert game.a.probe("action", gun, "ShootOnce") == "Probe action: applied"
    time.sleep(6)
    return before - total(game, rock_id)


def test_missiles_cut_voxels_without_protection(game, rock):
    rock_id, _ = rock
    assert fire_missile(game, rock_id) > 1


def test_missile_protection(game, rock):
    rock_id, _ = rock
    game.configure(ProtectVoxelsFromMissiles=True)
    assert fire_missile(game, rock_id) == pytest.approx(0, abs=0.5)


def ram(game, rock_id: int) -> float:
    """Throws a heavy block of armor at the asteroid at 90 m/s and returns how
    much voxel content the crash took away"""
    ship = prefabs.Grid(
        "Rammer",
        offset(CENTER, -3.75, -3.75, 90.0),
        static=False,
        slab=(4, 4, 4),
        owner=0,
    )
    game.spawn(ship)
    time.sleep(1)
    before = total(game, rock_id)
    game.a.probe("velocity", ship.entity_id, 0, 0, -90)
    time.sleep(6)
    return before - total(game, rock_id)


def test_collisions_cut_voxels_without_protection(game, rock):
    rock_id, _ = rock
    assert ram(game, rock_id) > 1


def test_collision_protection(game, rock):
    rock_id, _ = rock
    game.configure(ProtectVoxelsFromGridCollisions=True)
    assert ram(game, rock_id) == pytest.approx(0, abs=0.5)


def test_protection_needs_the_plugin_enabled(game, rock):
    rock_id, _ = rock
    game.configure(Enabled=False, ProtectVoxelsFromMissiles=True)
    assert fire_missile(game, rock_id) > 1


def drop_meteor(game, rock_id: int) -> float:
    """Drops a meteor on a slab of armor half sunk into the top of the
    asteroid and returns how much voxel content the impact took away. A meteor
    hitting the voxels makes no crater; the cut comes from the grid hit."""
    x, z = CENTER[0], CENTER[2]
    y = CENTER[1] + RADIUS
    while content(game, rock_id, (x, y, z), 1) == 0:
        y -= 1.0
    slab = prefabs.Grid("Target Slab", (x - 2.5, y, z - 2.5), slab=(3, 1, 3), owner=0)
    game.spawn(slab)
    time.sleep(2)
    before = total(game, rock_id)
    game.a.probe("meteor", x, y + 60, z, 0, -1, 0)
    time.sleep(8)
    return before - total(game, rock_id)


def test_meteors_cut_voxels_without_protection(game, rock):
    rock_id, _ = rock
    assert drop_meteor(game, rock_id) > 1


def test_meteor_protection(game, rock):
    rock_id, _ = rock
    game.configure(ProtectVoxelsFromMeteors=True)
    assert drop_meteor(game, rock_id) == pytest.approx(0, abs=0.5)
