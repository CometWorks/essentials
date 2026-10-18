"""What disconnects player B or happens when a player joins: kick, ban and
unban, and the MOTD on joining. Each of these tests brings B back, which takes
a few minutes."""

from __future__ import annotations

import re
import time

import pytest

import rig
import session
from conftest import wait_until

STRANGER = 76561199500098765


def banned(game) -> list[int]:
    text = game.server.dedicated_cfg.read_text(encoding="utf-8")
    block = re.search(r"<Banned>(.*?)</Banned>", text, re.S)
    return (
        [
            int(i)
            for i in re.findall(r"<unsignedLong>(\d+)</unsignedLong>", block.group(1))
        ]
        if block
        else []
    )


def offline(game, player) -> bool:
    return game.a.probe("player", player.steam_id).split("online=")[1].startswith("0")


def test_kick(game):
    assert game.a.ess("kick Bravo") == "Player 'Bravo' kicked."
    wait_until(lambda: offline(game, game.b), 30, "B gone")
    game.rejoin([game.b.client])
    assert game.b.ess("home list") == "You do not have any homes."


def test_ban_and_unban_a_player(game):
    steam = game.b.steam_id
    try:
        assert game.a.ess("ban Bravo") == f"Player Bravo banned. ({steam})"
        assert steam in banned(game)
        wait_until(lambda: offline(game, game.b), 30, "B gone")
        # B cannot come back while banned
        game.b.client.stop()
        game.b.client.launch(game.server.port)
        game.b.client.wait_api()
        time.sleep(60)
        assert offline(game, game.b)
    finally:
        assert game.a.ess(f"unban {steam}") == f"Player Bravo unbanned. ({steam})"
    assert steam not in banned(game)
    game.rejoin([game.b.client])
    assert game.b.ess("home list") == "You do not have any homes."


def test_ban_someone_never_seen(game):
    try:
        assert (
            game.a.ess(f"ban {STRANGER}") == f"Player {STRANGER} banned. ({STRANGER})"
        )
        assert STRANGER in banned(game)
    finally:
        assert (
            game.a.ess(f"unban {STRANGER}")
            == f"Player {STRANGER} unbanned. ({STRANGER})"
        )
    assert STRANGER not in banned(game)


def test_kick_and_ban_bad_input(game):
    assert game.a.ess("kick Zulu") == "Player not found."
    assert game.a.ess("ban Zulu") == "Player 'Zulu' not found."
    assert game.a.ess("unban Zulu") == "Player 'Zulu' not found."


@pytest.mark.xfail(
    strict=True, reason="Motd is shown only by !ess motd, never on joining, SE1-0239"
)
def test_motd_on_joining(game):
    game.configure(Motd="Welcome back, pilot")
    game.rejoin([game.b.client])
    wait_until(
        lambda: any("Welcome back, pilot" in t for _, t, _ in game.b.hud()),
        30,
        "the MOTD",
    )


@pytest.mark.xfail(strict=True, reason="NewUserMotd is not implemented, SE1-0239")
def test_new_user_motd_for_a_first_time_player(game):
    newcomer = rig.Client(3, "Delta")
    game.configure(Motd="Welcome back", NewUserMotd="First time here?")
    try:
        session.join(newcomer)
        player = session.Player(newcomer, 0)
        wait_until(
            lambda: any("First time here?" in t for _, t, _ in player.hud()),
            30,
            "the new-user MOTD",
        )
    finally:
        newcomer.stop()
        # Delta's identity would show up in the balance and player lists
        wait_until(
            lambda: game.a.probe("dropplayer", newcomer.steam_id)
            == "Probe dropplayer: done",
            30,
            "Delta gone",
        )
