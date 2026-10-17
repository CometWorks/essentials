"""Chat, HUD and save commands of an admin: say, broadcast, notify, save and
reload (with its short form !reload). Restart and stop are in
test_lifecycle.py."""

from __future__ import annotations

import time

from conftest import wait_until


def test_say_reaches_everyone_as_server_chat(game):
    befores = [p.mark() for p in (game.a, game.b)]
    game.a.say("!ess say Hello from the test")
    for player, before in zip((game.a, game.b), befores):
        player.wait_line(
            before,
            lambda s, t: s == "Server" and t == "Hello from the test",
            what="say",
        )


def test_broadcast_reaches_everyone(game):
    befores = [p.mark() for p in (game.a, game.b)]
    game.a.say("!ess broadcast Mind the gap")
    for player, before in zip((game.a, game.b), befores):
        player.wait_line(
            before, lambda s, t: s == "Server" and t == "Mind the gap", what="broadcast"
        )


def test_say_and_broadcast_need_a_message(game):
    assert game.a.ess("say") == "Usage: !ess say <message>"
    assert game.a.ess("broadcast") == "Usage: !ess broadcast <message>"


def test_notify_shows_a_hud_notification_to_everyone(game):
    marks = [p.notification_mark() for p in (game.a, game.b)]
    assert game.a.ess('notify "Test notice" 3000 Red') == "Notification sent."
    for player, mark in zip((game.a, game.b), marks):
        note = player.wait_notification(mark, "Test notice")
        assert note.get("font") == "Red", note


def test_notify_defaults(game):
    mark = game.b.notification_mark()
    assert game.a.ess("notify Plain") == "Notification sent."
    note = game.b.wait_notification(mark, "Plain")
    assert note.get("font") == "White", note


def test_save_writes_the_world(game):
    before = game.world_mtime()
    time.sleep(1.1)
    assert game.a.ess("save") == "World saved."
    assert game.world_mtime() > before


def test_reload_saves_and_rereads_the_dedicated_config(game):
    log_before = game.magnetar_log().count("Dedicated config reloaded")
    before = game.world_mtime()
    time.sleep(1.1)
    assert game.a.ess("reload") == "Configuration reloaded."
    assert game.world_mtime() > before
    wait_until(
        lambda: game.magnetar_log().count("Dedicated config reloaded") > log_before,
        10,
        "the reload in the server log",
    )


def test_short_reload(game):
    log_before = game.magnetar_log().count("Dedicated config reloaded")
    assert game.a.command("!reload") == "Configuration reloaded."
    wait_until(
        lambda: game.magnetar_log().count("Dedicated config reloaded") > log_before,
        10,
        "the reload in the server log",
    )
