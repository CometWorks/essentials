"""Player and admin quality-of-life commands and the identity lookups: MOTD,
stats, player count and list, Steam id lookups, names, last login, NPC check,
faction id, world PCU, promote levels, reserved slots, item gifts, chat mutes,
private messages and teleports. Kicks and bans disconnect player B, so they
are in test_joining.py."""

from __future__ import annotations

import re
import time

import pytest

import prefabs
import world
from conftest import wait_until
from session import distance


def character_id(game, player) -> int:
    return int(player.state()["character"])


# --- MOTD --------------------------------------------------------------------


def test_motd_not_configured(game):
    assert game.b.ess("motd") == "MOTD is not configured."


def test_motd_falls_back_to_private_chat(game):
    # Without MagnetarMod on the clients there are no mission screens
    game.configure(
        Motd="Welcome to the test\nBe nice", MotdUrl="https://example.com/rules"
    )
    a_before = game.a.mark()
    assert game.b.ess("motd").splitlines() == [
        "Mission screen unavailable. MOTD:",
        "Welcome to the test",
        "Be nice",
        "",
        "URL:",
        "https://example.com/rules",
    ]
    assert game.a.since(a_before) == []


def test_motd_url_only(game):
    game.configure(MotdUrl="https://example.com")
    assert game.b.ess("motd").splitlines()[-2:] == ["URL:", "https://example.com"]


# --- stats and player lists --------------------------------------------------


def test_stats(game):
    lines = game.a.ess("stats").splitlines()
    assert lines[0] == "Server stats:"
    assert "World: Empty World" in lines
    assert "Players: 2/8" in lines
    assert any(
        re.fullmatch(r"Sim speed: server [\d.]+, player [\d.]+", line) for line in lines
    ), lines
    assert any(
        re.fullmatch(r"Entities: [\d,]+ total, 0 grids, \d+ floating objects", line)
        for line in lines
    ), lines
    for start in ("PCU: ", "Game time: ", "Process uptime: ", "Memory: "):
        assert any(line.startswith(start) for line in lines), start


def test_player_count(game):
    assert (
        game.a.ess("admin playercount")
        == "Max player count: 8. Current online players: 2."
    )
    try:
        assert (
            game.a.ess("admin playercount 5")
            == "Max player count: 5. Current online players: 2."
        )
        assert (
            game.a.ess("admin playercount")
            == "Max player count: 5. Current online players: 2."
        )
    finally:
        game.a.ess("admin playercount 8")


def test_player_list(game):
    lines = game.a.ess("playerlist").splitlines()
    assert lines == [
        "Found 2 player(s) online:",
        "Alpha",
        f"> IdentityId: {world.PLAYER_A}",
        f"> SteamId: {game.a.steam_id}",
        # The server's administrators are owners
        "> Promote: Owner",
        "Bravo",
        f"> IdentityId: {world.PLAYER_B}",
        f"> SteamId: {game.b.steam_id}",
        "> Promote: None",
    ]


def test_getsteamid(game):
    expected = [
        "Display Name: Bravo",
        f"Steam ID: {game.b.steam_id}",
        f"Identity ID: {world.PLAYER_B}",
    ]
    for key in ("Bravo", "bravo", "rav", str(game.b.steam_id), str(world.PLAYER_B)):
        lines = game.b.ess(f"getsteamid {key}").splitlines()
        assert lines[1:] == expected, (key, lines)
        assert lines[0].startswith("Steam Name: "), lines
    # Offline identities are found unless online is asked for
    assert "Display Name: Charlie" in game.b.ess("getsteamid Charlie")
    assert game.b.ess("getsteamid Charlie true") == "Could not find that player."
    assert (
        game.b.ess("getsteamid Bravo true").splitlines()[2]
        == f"Steam ID: {game.b.steam_id}"
    )
    assert game.b.ess("getsteamid Zulu") == "Could not find that player."


def test_listids(game):
    lines = game.b.ess("listids").splitlines()
    assert len(lines) == 2
    assert re.fullmatch(rf"Names: .+ : Alpha \| ID: {game.a.steam_id}", lines[0]), lines
    assert re.fullmatch(rf"Names: .+ : Bravo \| ID: {game.b.steam_id}", lines[1]), lines


def test_updatename_and_listnames(game):
    assert game.b.ess("listnames") == "No players with mismatching names."
    try:
        assert (
            game.a.ess("updatename Bravo Bravo Two") == "New identity name: Bravo Two"
        )
        assert (
            game.a.probe("hasidentity", world.PLAYER_B)
            == "Probe hasidentity: Bravo_Two"
        )
        assert game.b.ess("listnames") == "Steam: Bravo | Identity: Bravo Two"
    finally:
        game.a.ess(f"updatename {world.PLAYER_B} Bravo")
    assert game.a.probe("hasidentity", world.PLAYER_B) == "Probe hasidentity: Bravo"


def test_updatename_bad_input(game):
    assert (
        game.a.ess("updatename Bravo")
        == "Usage: !ess updatename <player|steamId|identityId> <newName>"
    )
    assert game.a.ess("updatename Zulu Yankee") == "Could not find that identity."


def test_lastlogin(game):
    reply = game.a.ess("lastlogin Bravo")
    match = re.fullmatch(r"Bravo last login: (\d{4})-.*", reply)
    assert match and int(match.group(1)) >= 2026, reply
    assert game.a.ess("lastlogin Zulu") == "Could not find that player."


def test_isnpc(game):
    assert game.a.ess("isnpc Bravo") == "False"
    assert game.a.ess(f"isnpc {world.NPC}") == "True"
    assert game.a.ess("isnpc Zulu") == "Could not find that identity."


def test_getfacid(game):
    assert game.a.ess(f"getfacid {world.FACTION_TAG}") == str(world.FACTION)
    assert game.a.ess("getfacid NOPE") == "Error faction not found."


def test_worldpcu(game):
    # The game's own counter; it drifts below zero in this rig, so only the format counts
    assert re.fullmatch(r"Total world PCU: -?[\d,]+", game.a.ess("worldpcu"))


# --- promote levels and reserved slots ---------------------------------------


@pytest.mark.parametrize(
    "rank", ["Scripter", "Moderator", "SpaceMaster", "Admin", "none"]
)
def test_setrank(game, rank):
    level = "None" if rank == "none" else rank
    assert (
        game.a.ess(f"admin setrank Bravo {rank}")
        == f"Player 'Bravo' promoted to '{level}'."
    )
    assert game.b.state()["level"] == level


def test_setrank_bad_input(game):
    message = (
        "Invalid rank '{}'. Valid ranks: None, Scripter, Moderator, SpaceMaster, Admin."
    )
    assert game.a.ess("admin setrank Bravo Owner") == message.format("Owner")
    assert game.a.ess("admin setrank Bravo King") == message.format("King")
    assert (
        game.a.ess("admin setrank Zulu Admin")
        == "Player 'Zulu' not found or ID is invalid."
    )
    assert game.b.state()["level"] == "None"


@pytest.mark.xfail(
    strict=True, reason="Enum.TryParse takes numbers outside MyPromoteLevel, SE1-0245"
)
def test_setrank_refuses_numbers_outside_the_levels(game):
    assert game.a.ess("admin setrank Bravo -1").startswith("Invalid rank '-1'.")
    assert game.b.state()["level"] == "None"


def reserved(game) -> list[int]:
    text = game.server.dedicated_cfg.read_text(encoding="utf-8")
    block = re.search(r"<Reserved>(.*?)</Reserved>", text, re.S)
    return (
        [
            int(i)
            for i in re.findall(r"<unsignedLong>(\d+)</unsignedLong>", block.group(1))
        ]
        if block
        else []
    )


def test_reserved_slots(game):
    steam = game.b.steam_id
    try:
        assert (
            game.a.ess("admin reserve Bravo") == f"ID {steam} added to reserved slots."
        )
        assert steam in reserved(game)
        assert (
            game.a.ess(f"admin reserve {steam}") == f"ID {steam} is already reserved."
        )
        assert (
            game.a.ess("admin unreserve Bravo")
            == f"ID {steam} removed from reserved slots."
        )
        assert steam not in reserved(game)
        assert (
            game.a.ess("admin unreserve Bravo") == f"ID {steam} is already unreserved."
        )
    finally:
        game.a.ess("admin unreserve Bravo")
    assert (
        game.a.ess("admin reserve Zulu") == "Player 'Zulu' not found or ID is invalid."
    )


def test_reserve_by_steam_id_of_someone_never_seen(game):
    stranger = 76561199500099999
    try:
        assert (
            game.a.ess(f"admin reserve {stranger}")
            == f"ID {stranger} added to reserved slots."
        )
        assert stranger in reserved(game)
    finally:
        game.a.ess(f"admin unreserve {stranger}")
    assert stranger not in reserved(game)


# --- items -------------------------------------------------------------------


def test_give_an_item(game):
    character = character_id(game, game.b)
    before = game.inventory(character).get("Ore/Iron", 0)
    mark = game.b.notification_mark()
    assert game.a.ess("admin give Bravo Ore Iron 100") == "Item(s) given."
    wait_until(
        lambda: game.inventory(character).get("Ore/Iron", 0) == before + 100,
        10,
        "the iron",
    )
    game.b.wait_notification(mark, "You have been given 100 Iron Ore")


def test_give_everyone(game):
    ids = [character_id(game, p) for p in (game.a, game.b)]
    before = [game.inventory(i).get("Ingot/Gold", 0) for i in ids]
    assert (
        game.a.ess("admin give * Ingot Gold 7")
        == "Item(s) given to 2 online player(s)."
    )
    for i, b in zip(ids, before):
        wait_until(
            lambda: game.inventory(i).get("Ingot/Gold", 0) == b + 7, 10, "the gold"
        )


def test_give_bad_input(game):
    assert (
        game.a.ess("admin give Bravo Ore Iron 0")
        == "Quantity must be greater than zero."
    )
    assert (
        game.a.ess("admin give Bravo Nonsense Iron 1")
        == "Invalid item type or subtype."
    )
    assert game.a.ess("admin give Zulu Ore Iron 1") == "Player not found."


# --- mutes and private messages ----------------------------------------------


def test_mute_blocks_chat_until_unmuted(game):
    assert game.a.ess("mute Bravo 5") == "Muted user Bravo for 5 minute(s)."
    latest = game.a.chat_latest()
    b_before = game.b.mark()
    game.b.say("Can anyone hear me?")
    game.b.wait_line(
        b_before,
        lambda s, t: s == "Essentials" and t == "You are muted in chat.",
        what="the mute notice",
    )
    time.sleep(2)
    assert "Can anyone hear me?" not in [t for _, t in game.a.relayed_since(latest)]

    listed = game.a.ess("list mute").splitlines()
    assert listed[0] == "Muted users:"
    assert re.fullmatch(
        rf"Bravo \({game.b.steam_id}\): 00:0[45]:\d\d", listed[1]
    ), listed

    assert game.a.ess("unmute Bravo") == "Unmuted user Bravo."
    latest = game.a.chat_latest()
    game.b.say("Back again")
    wait_until(
        lambda: "Back again" in [t for _, t in game.a.relayed_since(latest)],
        10,
        "the chat",
    )
    assert game.a.ess("list mute") == "No muted users."


def test_muted_player_can_still_run_commands(game):
    game.a.ess("mute Bravo")
    assert game.b.ess("home list") in ("You do not have any homes.",)


def test_mute_indefinitely(game):
    assert game.a.ess("mute Bravo") == "Muted user Bravo indefinitely."
    assert game.a.ess("list mute").splitlines()[1] == f"Bravo ({game.b.steam_id}): inf"


def test_mute_bad_input(game):
    assert game.a.ess("mute Bravo -1") == "Mute minutes must be 0 or greater."
    assert game.a.ess("mute Zulu") == "Could not find user Zulu."
    assert game.a.ess("unmute Zulu") == "Could not find user Zulu."
    assert (
        game.a.ess("unmute Bravo") == "Failed to unmute user Bravo. They are not muted."
    )
    assert game.a.ess("list mute") == "No muted users."


@pytest.mark.parametrize("verb", ["msg", "whis"])
def test_private_message(game, verb):
    a_before, b_before = game.a.mark(), game.b.mark()
    game.b.say(f"!ess {verb} Alpha Meet me at the station")
    game.a.wait_line(
        a_before,
        lambda s, t: s == "PM from Bravo" and t == "Meet me at the station",
        what="the PM",
    )
    game.b.wait_line(
        b_before,
        lambda s, t: s == "PM to Alpha" and t == "Meet me at the station",
        what="the copy",
    )


def test_private_message_bad_input(game):
    assert game.b.ess("msg Zulu hello") == "Could not find online player Zulu."
    assert game.b.ess("msg Charlie hello") == "Could not find online player Charlie."
    reply = game.b.ess("msg Alpha")
    assert reply == "Usage: !ess msg <player> <message>", reply


def test_muted_player_cannot_send_private_messages(game):
    game.a.ess("mute Bravo")
    a_before = game.a.mark()
    assert game.b.ess("msg Alpha psst") == "You are muted in chat."
    time.sleep(1)
    assert not [line for line in game.a.since(a_before) if line[0].startswith("PM")]


# --- teleports ---------------------------------------------------------------


def test_tphere_brings_a_grid(game):
    g = prefabs.Grid("Far Ship", (0.0, 0.0, -300.0), static=False, slab=(2, 1, 2))
    game.spawn(g)
    assert game.a.ess('tphere "Far Ship"') == "Teleported 'Far Ship' to 'Alpha'."
    wait_until(
        lambda: distance(game.grid(g)["pos"], game.a.position()) < 50,
        10,
        "the grid near A",
    )


def test_tp_moves_a_grid_to_another(game):
    g = prefabs.Grid("Mover", (0.0, 0.0, -300.0), static=False, slab=(2, 1, 2))
    target = prefabs.Grid("Anchor", (300.0, 0.0, 0.0), slab=(2, 1, 2))
    game.spawn(g, target)
    assert game.a.ess("tp Mover Anchor") == "Teleported 'Mover' to 'Anchor'."
    wait_until(
        lambda: distance(game.grid(g)["pos"], target.position) < 50,
        10,
        "the grid near the anchor",
    )


def test_tpto_moves_the_caller(game):
    target = prefabs.Grid("Beacon Rock", (0.0, 500.0, 0.0), slab=(2, 1, 2))
    game.spawn(target)
    assert game.a.ess('tpto "Beacon Rock"') == "Teleported 'Alpha' to 'Beacon Rock'."
    time.sleep(3)
    assert distance(game.a.position(), target.position) < 50


def test_tp_another_player(game):
    game.a.ess("admin setrank Bravo SpaceMaster")
    target = prefabs.Grid("Meeting Point", (0.0, -400.0, 0.0), slab=(2, 1, 2))
    game.spawn(target)
    reply = game.b.ess('tpto "Meeting Point"')
    assert reply == "Teleported 'Bravo' to 'Meeting Point'.", reply
    time.sleep(3)
    assert distance(game.b.position(), target.position) < 50


def test_tp_bad_input(game):
    assert game.a.ess("tp Nowhere") == "Target entity not found."
    assert game.a.ess("tpto Nowhere") == "Destination entity not found."
    assert game.a.ess("tphere Nowhere") == "Target entity not found."
