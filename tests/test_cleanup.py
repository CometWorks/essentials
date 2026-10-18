"""Grid cleanup: every condition of `!ess cleanup scan/list/delete`, the
confirmation of a delete, piloted grids, logical grid groups, floating
objects, and bad input."""

from __future__ import annotations

import time

import pytest

import prefabs
import world
from conftest import confirm, wait_until
from session import FRONT, offset


def listed(game, conditions: str) -> set[str]:
    """Names of the grids `cleanup list` reports"""
    lines = game.a.ess(f"cleanup list {conditions}".strip()).splitlines()
    count = int(lines[0].split()[1])
    names = {line.split(" (")[0] for line in lines[1:]}
    assert len(lines) - 1 == count, lines
    return names


@pytest.fixture
def junkyard(game):
    """Five grids with different owners, sizes and blocks"""
    grids = {
        # 4 armor blocks, nobody owns it
        "Junk Small": prefabs.Grid(
            "Junk Small", offset(FRONT, -40), static=False, slab=(2, 1, 2), owner=0
        ),
        # 25 armor blocks and a battery of A
        "Big Station": prefabs.Grid(
            "Big Station", offset(FRONT, 0, 0, -40), slab=(5, 1, 5)
        ),
        # a small grid with a reactor of the pirates
        "Pirate Skiff": prefabs.Grid(
            "Pirate Skiff",
            offset(FRONT, 40),
            static=False,
            size="Small",
            slab=(3, 1, 3),
            owner=world.NPC,
        ),
        # far from everyone, with a reactor of B
        "Far Junk": prefabs.Grid(
            "Far Junk",
            (5000.0, 0.0, 0.0),
            static=False,
            slab=(2, 1, 2),
            owner=world.PLAYER_B,
        ),
    }
    grids["Big Station"].add("battery", (2, 1, 2))
    grids["Pirate Skiff"].add("small_reactor", (1, 1, 1))
    grids["Far Junk"].add("reactor", (0, 1, 0))
    game.spawn(*grids.values())
    return grids


ALL = {"Junk Small", "Big Station", "Pirate Skiff", "Far Junk"}


@pytest.mark.parametrize(
    "conditions, expected",
    [
        ("", ALL),
        ("name ^Junk", {"Junk Small"}),
        ("name junk", set()),
        ("name Junk$", {"Far Junk"}),
        ("blockslessthan 6", {"Junk Small", "Far Junk"}),
        ("blocksgreaterthan 20", {"Big Station"}),
        ("hasgridtype static", {"Big Station"}),
        ("hasgridtype ship", {"Junk Small", "Pirate Skiff", "Far Junk"}),
        ("hasgridtype small", {"Pirate Skiff"}),
        ("hasgridtype large", {"Junk Small", "Big Station", "Far Junk"}),
        ("hasownertype nobody", {"Junk Small"}),
        ("hasownertype npc", {"Pirate Skiff"}),
        ("hasownertype player", {"Big Station", "Far Junk"}),
        ("haspower", {"Big Station", "Pirate Skiff", "Far Junk"}),
        ("nopower", {"Junk Small"}),
        ("playerdistancelessthan 1000", {"Junk Small", "Big Station", "Pirate Skiff"}),
        ("playerdistancegreaterthan 1000", {"Far Junk"}),
        ("centerdistancegreaterthan 1000", {"Far Junk"}),
        ("centerdistancelessthan 1000", {"Junk Small", "Big Station", "Pirate Skiff"}),
        ("poweredgriddistancegreaterthan 1000", {"Far Junk"}),
        ("ownedby Alpha", {"Big Station"}),
        (f"ownedby {world.PLAYER_B}", {"Far Junk"}),
        ("ownedby nobody", {"Junk Small"}),
        ("hastype Reactor", {"Pirate Skiff", "Far Junk"}),
        ("notype Reactor", {"Junk Small", "Big Station"}),
        (
            "hastype-fast Reactor,BatteryBlock",
            {"Big Station", "Pirate Skiff", "Far Junk"},
        ),
        ("notype-fast Reactor,BatteryBlock", {"Junk Small"}),
        ("hassubtype LargeBlockBatteryBlock", {"Big Station"}),
        (
            "nosubtype LargeBlockBatteryBlock",
            {"Junk Small", "Pirate Skiff", "Far Junk"},
        ),
        (
            "hassubtype-fast SmallBlockSmallGenerator,LargeBlockSmallGenerator",
            {"Pirate Skiff", "Far Junk"},
        ),
        ("pcugreaterthan 38", {"Big Station"}),
        ("pculessthan 5", {"Junk Small"}),
        ("insideplanet", set()),
        ("hasgridtype ship blockslessthan 6 haspower", {"Far Junk"}),
    ],
)
def test_conditions(game, junkyard, conditions, expected):
    assert listed(game, conditions) == expected
    reply = game.a.ess(f"cleanup scan {conditions}".strip())
    assert reply == f"Found {len(expected)} grids matching the given conditions."


def test_list_shows_blocks_pcu_and_id(game, junkyard):
    g = junkyard["Junk Small"]
    line = game.a.ess("cleanup list name Small").splitlines()[1]
    assert line == f"Junk Small (4 block(s), 4 PCU, entity {g.entity_id})"


def test_ownedby_pirates(game, junkyard):
    # The Space Pirates of the Empty World own the skiff
    assert game.a.probe("pirates") == f"Probe pirates: id={world.NPC} faction=SPRT"
    assert listed(game, "ownedby pirates") == {"Pirate Skiff"}


@pytest.mark.xfail(
    strict=True,
    reason="ownedby npc looks the owner's identity id up as a faction id, SE1-0244",
)
def test_ownedby_npc(game, junkyard):
    assert listed(game, "ownedby npc") == {"Pirate Skiff"}


def test_delete_needs_confirmation(game, junkyard):
    prompt, result = confirm(game.a, "cleanup delete blockslessthan 6")
    assert (
        prompt
        == "This will delete 2 grid(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == "Deleted 2 grids matching the given conditions."
    for name in ("Junk Small", "Far Junk"):
        assert not game.exists(junkyard[name])
    for name in ("Big Station", "Pirate Skiff"):
        assert game.exists(junkyard[name])


def test_delete_without_confirmation_deletes_nothing(game, junkyard):
    assert game.a.ess("cleanup delete nopower").startswith(
        "This will delete 1 grid(s)."
    )
    # A different command line is a different operation
    assert game.a.ess("cleanup delete nopower hasgridtype ship").startswith(
        "This will delete 1 grid(s)."
    )
    assert game.exists(junkyard["Junk Small"])


def test_confirmation_is_per_caller(game, junkyard):
    game.a.probe("promote", game.b.steam_id, "Admin")
    assert game.a.ess("cleanup delete nopower").startswith("This will delete")
    assert game.b.ess("cleanup delete nopower").startswith("This will delete")
    assert game.exists(junkyard["Junk Small"])


def test_confirmation_expires(game, junkyard):
    assert game.a.ess("cleanup delete nopower").startswith("This will delete")
    time.sleep(31)
    assert game.a.ess("cleanup delete nopower").startswith("This will delete")
    assert game.exists(junkyard["Junk Small"])


def test_delete_nothing(game, junkyard):
    assert (
        game.a.ess("cleanup delete name ^Nothing$")
        == "Found 0 grids matching the given conditions."
    )


def test_piloted_grids_are_skipped_unless_asked_for(game):
    g = prefabs.Grid(
        "Occupied",
        offset(FRONT, dy=-1.25),
        static=False,
        slab=(3, 1, 3),
        owner=world.PLAYER_B,
    )
    seat = g.add("seat", (1, 1, 1))
    game.spawn(g)
    game.b.sit(g.world_of((1, 1, 1)), seat)
    assert listed(game, "name Occupied") == set()
    assert listed(game, "name Occupied haspilot") == {"Occupied"}
    _, result = confirm(game.a, "cleanup delete name Occupied haspilot")
    assert result == "Deleted 1 grids matching the given conditions."
    assert not game.exists(g)
    state = game.b.state()
    assert state["seat"] == "0" and state["dead"] == "0", state


def test_logical_groups_match_as_a_whole(game):
    base = prefabs.Grid("Rotor Base", offset(FRONT, 0, 0, -20), slab=(3, 1, 3))
    stator = base.add("stator", (1, 1, 1))
    game.spawn(base)
    top = int(
        game.a.probe("attach", stator, '"Rotor Top"').removeprefix("Probe attach: ")
    )
    assert game.grid(top)["blocks"] == 1
    # The top alone has 1 block, but its group has 10
    assert listed(game, "blockslessthan 5") == set()
    assert listed(game, "blocksgreaterthan 0") == {"Rotor Base", "Rotor Top"}


@pytest.mark.parametrize(
    "conditions, error",
    [
        (
            "frobnicate",
            "Unknown cleanup condition 'frobnicate'. Use !ess cleanup help.",
        ),
        ("blockslessthan", "Cleanup condition 'blockslessthan' requires an argument."),
        (
            "blockslessthan haspower",
            "Cleanup condition 'blockslessthan' requires an argument.",
        ),
        ("haspower 3", "Cleanup condition 'haspower' does not accept an argument."),
        ("blockslessthan many", "Could not parse integer argument 'many'."),
        ("playerdistancelessthan far", "Could not parse number argument 'far'."),
    ],
)
def test_bad_conditions(game, junkyard, conditions, error):
    for verb in ("scan", "list", "delete"):
        assert game.a.ess(f"cleanup {verb} {conditions}") == error, verb
    assert game.exists(junkyard["Junk Small"])


def test_bad_regex(game, junkyard):
    assert game.a.ess("cleanup scan name [").startswith("Invalid regex: ")


def test_help_lists_every_condition(game):
    lines = game.a.ess("cleanup help").splitlines()
    assert lines[0] == "Cleanup conditions:"
    for name in (
        "name:",
        "blockslessthan:",
        "haspower / nopower",
        "insideplanet",
        "ownedby",
        "hastype / notype",
        "hastype-fast / notype-fast",
        "hassubtype / nosubtype",
        "hassubtype-fast / nosubtype-fast",
        "haspilot",
        "poweredgriddistancegreaterthan",
        "playerdistancelessthan / playerdistancegreaterthan",
        "centerdistancelessthan / centerdistancegreaterthan",
        "pcugreaterthan",
        "pculessthan",
        "hasgridtype",
        "hasownertype",
    ):
        assert any(line.startswith(name) for line in lines), name
    assert (
        lines[-1] == "No haspilot condition means piloted grids are skipped by default."
    )


def floats(game) -> int:
    return int(game.a.probe("floats").removeprefix("Probe floats: "))


def test_floating_objects(game):
    assert game.a.ess("cleanup delete floatingobjects") == "Deleted 0 floating objects."
    for i in range(3):
        game.a.probe("floating", "Stone", 10, 5.0 * i, 30.0, -30.0)
    wait_until(lambda: floats(game) == 3, 10, "three floating objects")
    prompt, result = confirm(game.a, "cleanup delete floatingobjects")
    assert (
        prompt
        == "This will delete 3 floating object(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == "Deleted 3 floating objects."
    assert floats(game) == 0


# --- grid look notifications -------------------------------------------------


def notice(conditions: str, message: str) -> dict:
    return {"_tag": "CleanupLookNotice", "Conditions": conditions, "Message": message}


@pytest.fixture
def wreck_of_b(game):
    g = prefabs.Grid("Small Wreck", FRONT, slab=(2, 1, 2), owner=world.PLAYER_B)
    g.add("reactor", (0, 1, 0))
    game.spawn(g)
    return g


def look_at(player, g) -> None:
    target = g.top(dy=-g.cell_m / 2)
    player.stand_and_look(offset(target, dz=10, dy=1.5), target)


def notes_with(player, mark: int, text: str) -> list[dict]:
    return [n for n in player.notifications(mark) if text in n.get("text", "")]


def test_look_notice_reaches_the_owner(game, wreck_of_b):
    game.configure(
        CleanupLookNotices=[
            notice("blocksgreaterthan 100", "Too big: {GridName}"),
            notice("blockslessthan 20 hasownertype player", "Please repair {GridName}"),
        ]
    )
    a_mark, b_mark = game.a.notification_mark(), game.b.notification_mark()
    look_at(game.b, wreck_of_b)
    look_at(game.a, wreck_of_b)
    game.b.wait_notification(b_mark, "Please repair Small Wreck", timeout=15)
    time.sleep(4)
    # Only BigOwners get it, and only the first matching rule shows
    assert notes_with(game.a, a_mark, "Small Wreck") == []
    assert notes_with(game.b, b_mark, "Too big") == []


def test_look_notice_reminds_while_near(game, wreck_of_b):
    game.configure(
        CleanupLookReminderMinutes=1,
        CleanupLookNotices=[
            notice("blocksgreaterthan 100", "x"),
            notice("blockslessthan 20", "Fix {GridName}"),
        ],
    )
    mark = game.b.notification_mark()
    look_at(game.b, wreck_of_b)
    game.b.wait_notification(mark, "Fix Small Wreck", timeout=15)
    # Looking away does not end the reminders while B stays within 500 m
    game.b.look_at(offset(game.b.position(), dy=100))
    wait_until(
        lambda: len(notes_with(game.b, mark, "Fix Small Wreck")) >= 2,
        80,
        "the reminder",
    )


def test_look_notice_needs_a_match_and_the_plugin(game, wreck_of_b):
    game.configure(
        CleanupLookNotices=[
            notice("blocksgreaterthan 100", "Too big {GridName}"),
            notice("bad condition", "Broken {GridName}"),
        ]
    )
    mark = game.b.notification_mark()
    look_at(game.b, wreck_of_b)
    time.sleep(8)
    assert notes_with(game.b, mark, "Small Wreck") == []
    assert "Invalid cleanup look notification rule 'bad condition'" in game.plugin_log()
    game.configure(
        Enabled=False,
        CleanupLookNotices=[notice("blockslessthan 20", "Off {GridName}")],
    )
    mark = game.b.notification_mark()
    game.b.go_home()
    look_at(game.b, wreck_of_b)
    time.sleep(8)
    assert notes_with(game.b, mark, "Small Wreck") == []
