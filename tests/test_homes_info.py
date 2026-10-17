"""Homes (add, list, goto, del, the limit, the switch, the file they live in)
and the configured info commands with their chat, notification, dialog and
URL responses. Homes surviving a restart are in test_lifecycle.py."""

from __future__ import annotations

import time


import prefabs
import world
from conftest import wait_until
from session import distance, offset

# --- homes -------------------------------------------------------------------


def saved_homes(game) -> str:
    return (
        game.server.homes.read_text(encoding="utf-8")
        if game.server.homes.exists()
        else ""
    )


def test_home_round_trip(game):
    spot = game.b.position()
    assert game.b.ess("home list") == "You do not have any homes."
    assert game.b.ess("home add base") == "Home successfully added."
    assert game.b.ess("home add Mine") == "Home successfully added."
    # Sorted by the culture rules of the server, not by code points
    assert game.b.ess("home list") == "List of homes: base, Mine"
    text = saved_homes(game)
    assert f'SteamId="{game.b.steam_id}"' in text and 'Name="base"' in text

    game.b.teleport(offset(spot, 200.0, 50.0, -100.0))
    assert game.b.ess("home goto BASE") == "Teleported to 'BASE'."
    wait_until(lambda: distance(game.b.position(), spot) < 5, 10, "B at home")

    assert game.b.ess("home del base") == "Home successfully removed."
    assert game.b.ess("home list") == "List of homes: Mine"
    assert 'Name="base"' not in saved_homes(game)


def test_home_bad_input(game):
    game.b.ess("home add base")
    assert game.b.ess("home add Base") == "Home 'Base' already exists."
    assert game.b.ess("home del attic") == "The stated home does not exist."
    assert game.b.ess("home goto attic") == "The stated home does not exist."


def test_homes_are_per_player(game):
    game.b.ess("home add secret")
    assert game.a.ess("home goto secret") == "The stated home does not exist."
    assert game.a.ess("home list") == "You do not have any homes."


def test_home_limit(game):
    game.configure(MaxHomesPerPlayer=2)
    game.b.ess("home add one")
    game.b.ess("home add two")
    assert game.b.ess("home add three") == "You have the maximum amount of homes (2)."
    game.configure(MaxHomesPerPlayer=0)
    assert game.a.ess("home add first") == "You have the maximum amount of homes (0)."


def test_homes_switched_off(game):
    game.b.ess("home add base")
    game.configure(HomesEnabled=False)
    for command in ("home add other", "home del base", "home list", "home goto base"):
        assert game.b.ess(command) == "Homes are not enabled for this server.", command


def test_no_home_teleport_from_a_seat(game):
    g = prefabs.Grid("Chair", (0.0, -1.25, -10.0), slab=(3, 1, 3), owner=world.PLAYER_B)
    seat = g.add("seat", (1, 1, 1))
    game.spawn(g)
    game.b.ess("home add base")
    game.b.sit(g.world_of((1, 1, 1)), seat)
    assert (
        game.b.ess("home goto base")
        == "You cannot use !ess home while in control of a grid."
    )


# --- info commands -----------------------------------------------------------


def info(command, **responses) -> dict:
    return {"_tag": "InfoCommand", "Command": command, **responses}


def test_info_chat_response_is_private(game):
    game.configure(InfoCommands=[info("rules", ChatResponse="No griefing, no spam")])
    latest = game.a.chat_latest()
    a_before = game.a.mark()
    assert game.b.command("!rules") == "No griefing, no spam"
    time.sleep(1)
    assert game.a.since(a_before) == []
    assert "!rules" not in [t for _, t in game.a.relayed_since(latest)]


def test_info_names_ignore_case_and_extra_words(game):
    game.configure(InfoCommands=[info("!Discord", ChatResponse="Join us")])
    assert game.b.command("!DISCORD please now") == "Join us"


def test_info_notification(game):
    game.configure(
        InfoCommands=[
            info(
                "tip",
                NotificationResponse="Press F to pay respects",
                NotificationDurationMs=2500,
            )
        ]
    )
    a_mark, b_mark = game.a.notification_mark(), game.b.notification_mark()
    game.b.say("!tip")
    game.b.wait_notification(b_mark, "Press F to pay respects")
    time.sleep(1)
    assert not [
        n for n in game.a.notifications(a_mark) if "pay respects" in n.get("text", "")
    ]


def test_info_dialog_falls_back_to_chat(game):
    game.configure(InfoCommands=[info("about", DialogResponse="A test server")])
    assert game.b.command("!about") == "A test server"


def test_info_url(game):
    game.configure(
        InfoCommands=[
            info("web", URL="https://example.com/discord"),
            info("bad", URL="ftp://example.com/x"),
        ]
    )
    assert game.b.command("!web") == "https://example.com/discord"
    assert game.b.command("!bad") == "This info command has an invalid URL."


def test_info_several_responses(game):
    game.configure(
        InfoCommands=[
            info("all", ChatResponse="Chat part", URL="https://example.com/a")
        ]
    )
    assert game.b.command("!all").splitlines() == ["Chat part", "https://example.com/a"]


def test_info_list(game):
    assert game.b.ess("info list") == "No info commands are configured."
    game.configure(
        InfoCommands=[
            info("rules", ChatResponse="r"),
            info("Discord", URL="https://example.com"),
            info("empty"),
            info("bad name!", ChatResponse="x"),
            info("ess", ChatResponse="x"),
            info("stone", ChatResponse="x"),
            info("help", ChatResponse="x"),
        ]
    )
    assert game.b.ess("info list") == "!Discord, !rules"


def test_info_cannot_shadow_reserved_names(game):
    game.configure(InfoCommands=[info("help", ChatResponse="Not the help")])
    reply = game.b.command("!help", senders={"Help", "Essentials"})
    assert "Not the help" not in reply


def test_registered_commands_take_precedence(game):
    game.configure(InfoCommands=[info("fixship", ChatResponse="Info about fixship")])
    reply = game.b.command("!fixship", senders={"fixship", "Essentials"})
    assert "Info about fixship" not in reply, reply
