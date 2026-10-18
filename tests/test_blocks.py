"""Switching and removing blocks across every grid: `!ess blocks on/off` by
type, subtype and category, and `!ess blocks remove` by type and subtype.
Blocks of projections are left alone."""

from __future__ import annotations

import pytest

import prefabs
from conftest import wait_until

# Far from the players: the turret is switched on by some tests
SITE = (0.0, 0.0, -2000.0)


@pytest.fixture
def plant(game):
    g = prefabs.Grid("Power Plant", SITE, slab=(7, 1, 7))
    g.add("reactor", (0, 1, 0))
    g.add("battery", (2, 1, 0))
    g.add("assembler", (4, 1, 0))
    g.add("gatling", (6, 1, 0))
    g.add("launcher", (0, 1, 3))
    projection = prefabs.Grid("Blueprint", (0.0, 0.0, 0.0), slab=(1, 1, 1))
    projection.add("reactor", (0, 1, 0))
    g.add("projector", (4, 1, 4), extra=projector_xml(projection))
    other = prefabs.Grid("Second Plant", (40.0, 0.0, -2000.0), slab=(2, 1, 2))
    other.add("reactor", (0, 1, 0), name="reactor")
    game.spawn(g, other)
    wait_until(lambda: projected(game, g) == 2, 20, "the projection")
    return g, other


def projector_xml(blueprint: prefabs.Grid) -> str:
    grid = (
        blueprint.xml()
        .replace("<CubeGrid>", "<MyObjectBuilder_CubeGrid>")
        .replace("</CubeGrid>", "</MyObjectBuilder_CubeGrid>")
    )
    return (
        f"<ProjectedGrids>{grid}</ProjectedGrids>"
        '<ProjectionOffset x="0" y="2" z="0" /><ProjectionRotation x="0" y="0" z="0" />'
        "<KeepProjection>true</KeepProjection><ShowOnlyBuildable>false</ShowOnlyBuildable>"
    )


def projected(game, g) -> int:
    reply = game.a.probe("projected", g.blocks["projector"])
    return int(reply.split("blocks=")[1]) if "blocks=" in reply else -1


def cells(game, g) -> set[str]:
    return set(game.a.probe("cells", g.entity_id).removeprefix("Probe cells: ").split())


def enabled(game, block_id: int) -> str:
    return game.block(block_id).get("enabled", "gone")


def test_off_and_on_by_type(game, plant):
    g, other = plant
    assert game.a.ess("blocks off type Reactor") == "Disabled 2 blocks of type Reactor."
    assert enabled(game, g.blocks["reactor"]) == "0"
    assert enabled(game, other.blocks["reactor"]) == "0"
    assert enabled(game, g.blocks["battery"]) == "1"
    # Already off: nothing to count
    assert game.a.ess("blocks off type Reactor") == "Disabled 0 blocks of type Reactor."
    assert game.a.ess("blocks on type reactor") == "Enabled 2 blocks of type reactor."
    assert enabled(game, g.blocks["reactor"]) == "1"


def test_off_and_on_by_subtype(game, plant):
    g, _ = plant
    assert (
        game.a.ess("blocks off subtype LargeBlockBatteryBlock")
        == "Disabled 1 blocks of subtype LargeBlockBatteryBlock."
    )
    assert enabled(game, g.blocks["battery"]) == "0"
    assert enabled(game, g.blocks["reactor"]) == "1"
    assert (
        game.a.ess("blocks on subtype largeblockbatteryblock")
        == "Enabled 1 blocks of subtype largeblockbatteryblock."
    )
    assert enabled(game, g.blocks["battery"]) == "1"


@pytest.mark.parametrize(
    "category, kinds",
    [
        ("Power", ["reactor", "battery"]),
        ("Production", ["assembler"]),
        ("Weapons", ["gatling"]),
    ],
)
def test_categories(game, plant, category, kinds):
    g, other = plant
    count = len(kinds) + (1 if category == "Power" else 0)
    assert (
        game.a.ess(f"blocks off general {category}")
        == f"Disabled {count} {category} blocks."
    )
    for kind in kinds:
        assert enabled(game, g.blocks[kind]) == "0", kind
    assert enabled(game, g.blocks["launcher"]) == "1"
    assert (
        game.a.ess(f"blocks on general {category}")
        == f"Enabled {count} {category} blocks."
    )
    for kind in kinds:
        assert enabled(game, g.blocks[kind]) == "1", kind


def test_unknown_category(game, plant):
    reply = game.a.ess("blocks on general Food")
    assert "Usage: !ess blocks on general" in reply, reply


def test_unknown_type(game, plant):
    assert (
        game.a.ess("blocks off type Nonsense") == "Disabled 0 blocks of type Nonsense."
    )


def test_remove_by_type(game, plant):
    g, other = plant
    before = cells(game, g)
    assert (
        game.a.ess("blocks remove type Reactor") == "Removed 2 blocks of type Reactor."
    )
    assert enabled(game, g.blocks["reactor"]) == "gone"
    assert enabled(game, other.blocks["reactor"]) == "gone"
    # The turret (6,1,0) splits off into a grid of its own on any change to the
    # plant, so it doesn't count
    assert before - cells(game, g) - {"6,1,0"} == {"0,1,0"}
    # The projection keeps its reactor
    assert projected(game, g) == 2


def test_remove_by_subtype(game, plant):
    g, _ = plant
    assert (
        game.a.ess("blocks remove subtype LargeBlockBatteryBlock")
        == "Removed 1 blocks of subtype LargeBlockBatteryBlock."
    )
    assert enabled(game, g.blocks["battery"]) == "gone"
    assert enabled(game, g.blocks["reactor"]) == "1"


def test_projected_blocks_are_not_switched(game, plant):
    g, _ = plant
    # Two real reactors; the projected one is not counted
    assert game.a.ess("blocks off type Reactor") == "Disabled 2 blocks of type Reactor."
