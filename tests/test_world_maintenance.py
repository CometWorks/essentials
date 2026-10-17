"""Identity, reputation, faction and sandbox maintenance: identity
clean/purge/clear, rep wipe, faction clean/remove/info and sandbox clean.
Every destructive one asks for confirmation first.

The tests make their own identities and factions through the probe, which
removes them again after each test. Player C's last login, and that of the
player identity the Empty World comes with, is set to now before each test."""

from __future__ import annotations

import re

import pytest

import prefabs
import world
from conftest import confirm, wait_until
from session import FRONT, offset, parse


@pytest.fixture(autouse=True)
def nobody_old(game):
    """C and the Empty World's own player identity logged in just now"""
    assert game.a.probe("lastlogin", world.PLAYER_C, 0) == "Probe lastlogin: set"
    # sandbox clean removes it, as it owns no grid
    game.a.probe("lastlogin", world.SKINFACE, 0)


def make_identity(game, name: str, days_ago: float, npc: bool = False) -> int:
    reply = game.a.probe("identity", f'"{name}"', days_ago, str(npc).lower())
    return int(reply.removeprefix("Probe identity: "))


def has_identity(game, identity_id: int) -> bool:
    return game.a.probe("hasidentity", identity_id) != "Probe hasidentity: none"


def owned_grid(
    name: str, owner: int, dx: float, co_owner: int | None = None
) -> prefabs.Grid:
    g = prefabs.Grid(name, offset(FRONT, dx, 0, -30), slab=(3, 1, 3), owner=owner)
    g.add("reactor", (0, 1, 0))
    if co_owner is not None:
        g.add("battery", (2, 1, 2), owner=co_owner)
    return g


# --- identities --------------------------------------------------------------


def test_identity_clean_keeps_the_grids(game):
    old = make_identity(game, "Old Timer", 100)
    g = owned_grid("Old Base", old, 0)
    game.spawn(g)
    prompt, result = confirm(game.a, "identity clean 30")
    assert prompt == (
        "This will remove 1 old identit(y/ies) and preserve their grids. "
        "Run the same command again within 30 seconds to confirm."
    )
    assert re.fullmatch(
        r"Removed 1 old identit\(y/ies\), reassigned ownership on 1 grid\(s\), "
        r"removed \d+ empty faction\(s\)\.",
        result,
    ), result
    assert not has_identity(game, old)
    state = game.grid(g)
    assert state and state["owners"] == []


def test_identity_clean_gives_shared_grids_to_the_other_owner(game):
    old = make_identity(game, "Old Partner", 100)
    g = owned_grid("Shared Base", old, 0, co_owner=world.PLAYER_C)
    game.spawn(g)
    confirm(game.a, "identity clean 30")
    wait_until(
        lambda: game.block(g.blocks["reactor"]).get("owner") == str(world.PLAYER_C),
        10,
        "the new owner",
    )


def test_identity_purge_closes_the_grids(game):
    old = make_identity(game, "Old Purge", 100)
    g = owned_grid("Doomed Base", old, 0)
    keep = owned_grid("Kept Base", world.PLAYER_A, 40)
    game.spawn(g, keep)
    prompt, result = confirm(game.a, "identity purge 30")
    assert prompt == (
        "This will remove 1 old identit(y/ies) and close up to 1 solely-owned grid(s). "
        "Run the same command again within 30 seconds to confirm."
    )
    assert result.startswith("Removed 1 old identit(y/ies), closed 1 grid(s), ")
    assert not has_identity(game, old)
    assert not game.exists(g)
    assert game.exists(keep)


def test_identity_clean_leaves_npcs_unless_asked(game):
    make_identity(game, "Old Human", 100)
    make_identity(game, "Old Bot", 100, npc=True)
    without = game.a.ess("identity clean 30")
    with_npcs = game.a.ess("identity clean 30 true")
    count = lambda text: int(
        re.search(r"remove (\d+) old", text).group(1)
    )  # noqa: E731
    assert count(without) == 1
    assert count(with_npcs) > 1


def test_identity_clean_nothing_old(game):
    assert (
        game.a.ess("identity clean 3650")
        == "No identities found older than 3650 day(s)."
    )
    assert (
        game.a.ess("identity purge 3650")
        == "No identities found older than 3650 day(s)."
    )


@pytest.mark.xfail(
    strict=True,
    reason="identity clean counts online players with 0 or fewer days, SE1-0245",
)
def test_identity_clean_never_takes_online_players(game):
    reply = game.a.ess("identity clean 0")
    assert reply == "No identities found older than 0 day(s).", reply


def test_identity_clear(game):
    ghost = make_identity(game, "Ghost Owner", 0)
    g = owned_grid("Ghost Base", ghost, 0)
    game.spawn(g)
    prompt, result = confirm(game.a, 'identity clear "Ghost Owner"')
    assert prompt == (
        "This will remove identity 'Ghost Owner' and close up to 1 solely-owned grid(s). "
        "Run the same command again within 30 seconds to confirm."
    )
    assert re.fullmatch(
        r"Removed identity 'Ghost Owner', closed 1 grid\(s\), removed \d+ empty faction\(s\)\.",
        result,
    )
    assert not has_identity(game, ghost)
    assert not game.exists(g)
    assert game.a.ess("identity clear Zulu") == "No identity found for Zulu."


# --- reputation --------------------------------------------------------------


def reps(game) -> dict[str, int]:
    return {k: int(v) for k, v in parse(game.a.probe("reps")).items()}


def test_rep_wipe(game):
    game.a.probe("rep", world.PLAYER_B, "SPRT", -900)
    before = reps(game)
    assert before["players"] > 0
    total = before["players"] + before["factions"]
    prompt, result = confirm(game.a, "rep wipe")
    assert (
        prompt
        == f"This will wipe {total} reputation relation(s). Run the same command again within 30 seconds to confirm."
    )
    assert result == f"Wiped {total} reputation relation(s)."
    assert reps(game) == {"players": 0, "factions": 0}
    assert game.a.ess("rep wipe") == "Wiped 0 reputations."


def test_rep_wipe_players_only(game):
    game.a.probe("rep", world.PLAYER_B, "SPRT", -900)
    before = reps(game)
    _, result = confirm(game.a, "rep wipe true false")
    assert result == f"Wiped {before['players']} reputation relation(s)."
    assert reps(game) == {"players": 0, "factions": before["factions"]}


# --- factions ----------------------------------------------------------------


def make_faction(game, tag: str, founder: int) -> None:
    game.a.probe("faction", tag, founder)
    wait_until(
        lambda: game.a.probe("factioninfo", tag) != "Probe factioninfo: none",
        10,
        f"faction {tag}",
    )


def test_faction_info(game):
    lines = game.a.ess(f"faction info {world.FACTION_TAG}").splitlines()
    assert lines == [
        f"ETF - {world.FACTION_NAME} - 2 member(s)",
        f"  Charlie ({world.PLAYER_C}) - Founder",
        f"  Bravo ({world.PLAYER_B}) - Member",
    ]
    everything = game.a.ess("faction info")
    assert "ETF - " in everything
    assert re.search(r"^SPRT - .* - NPC$", everything, re.M), everything
    assert game.a.ess("faction info NOPE") == "No matching factions found."


def test_faction_remove(game):
    founder = make_identity(game, "Lone Founder", 0)
    make_faction(game, "TMP", founder)
    prompt, result = confirm(game.a, "faction remove TMP")
    assert (
        prompt
        == "This will remove faction TMP (TMP Testers). Run the same command again within 30 seconds to confirm."
    )
    assert result == "TMP removed."
    assert game.a.probe("factioninfo", "TMP") == "Probe factioninfo: none"
    assert game.a.ess("faction remove NOPE") == "NOPE is not a faction on this server."


def test_faction_clean(game):
    founder = make_identity(game, "Small Founder", 0)
    make_faction(game, "SML", founder)
    prompt = game.a.ess("faction clean 2")
    assert re.fullmatch(
        r"This will remove \d+ faction\(s\) with fewer than 2 valid member\(s\)\. "
        r"Run the same command again within 30 seconds to confirm\.",
        prompt,
    ), prompt
    result = game.a.ess("faction clean 2")
    assert re.fullmatch(
        r"Removed \d+ faction\(s\) with fewer than 2 valid member\(s\)\.", result
    ), result
    wait_until(
        lambda: game.a.probe("factioninfo", "SML") == "Probe factioninfo: none",
        10,
        "SML gone",
    )
    # Two members: kept
    assert game.a.probe("factioninfo", world.FACTION_TAG) != "Probe factioninfo: none"


def test_faction_clean_nothing(game):
    assert (
        game.a.ess("faction clean 0")
        == "No factions found with fewer than 0 valid member(s)."
    )


# --- sandbox -----------------------------------------------------------------


def test_sandbox_clean_removes_identities_without_grids(game):
    ghost = make_identity(game, "Gridless Ghost", 0)
    owner = make_identity(game, "Grid Owner", 0)
    game.spawn(
        owned_grid("Owner Base", owner, 0),
        owned_grid("Charlie Base", world.PLAYER_C, 40),
    )
    prompt, result = confirm(game.a, "sandbox clean")
    assert prompt == (
        "This will remove stale sandbox data and cannot be previewed exactly. "
        "Run the same command again within 30 seconds to confirm."
    )
    assert re.fullmatch(
        r"Removed \d+ unnecessary sandbox element\(s\)\.", result
    ), result
    assert not has_identity(game, ghost)
    assert has_identity(game, owner)
    assert has_identity(game, world.PLAYER_C)
    for player in (world.PLAYER_A, world.PLAYER_B):
        assert has_identity(game, player)
