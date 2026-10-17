"""Server control that ends the server process: the restart and shutdown
sequences, restart and stop at once, what survives a restart (config written
by commands, homes, the OnStart trigger), and a broken config file.

These tests restart or stop the server and join the clients again; conftest.py
runs them first."""

from __future__ import annotations

import time

import pytest

import prefabs
from conftest import auto, step
from session import FRONT

# Grid velocities seen after the restart of test_restart_now, for
# test_stop_ships_on_start
after_restart: dict[str, float] = {}


def send_at_once(game, command: str) -> None:
    """Sends a command that ends the server within a second, without reading
    its reply: client A's Remote API hangs and then fails while the client drops
    out of the session"""
    game.a.say(command)


def restarted(game, count: int) -> None:
    """Waits for the server to come back from a restart and joins again"""
    game.server.wait_ready(count, timeout=900)
    game.rejoin()


def test_restart_sequence_and_what_survives_it(game):
    game.configure(
        OnRestartSequence="Reboot",
        AutoCommands=[
            auto(
                "Reboot",
                [step("Announce", "Restarting in 3", "00:00:03"), step("Restart")],
            ),
            auto("Greeter", [step("Announce", "Server started")], trigger="OnStart"),
        ],
    )
    # Written by commands and the plugin, read back after the restart
    assert game.a.ess("voxels protect meteors on") == "Meteor voxel protection on."
    assert game.b.ess("home add keepsake") == "Home successfully added."
    started_before = game.plugin_log().count("Auto command 'Greeter' started (OnStart)")
    count = game.server.ready_count()

    before = game.b.mark()
    assert game.a.ess("restart") == "Restart sequence 'Reboot' started."
    game.b.wait_line(
        before,
        lambda s, t: s == "Server" and t == "Restarting in 3",
        what="the warning",
    )
    restarted(game, count)

    assert (
        game.a.ess("voxels protect status")
        == "Voxel protection: missiles off, meteors on, grid collisions off."
    )
    assert game.b.ess("home list") == "List of homes: keepsake"
    log = game.plugin_log()
    assert "Auto command 'Greeter' started (OnStart)" in log
    assert log.count("Auto command 'Greeter' started (OnStart)") >= started_before


# test_stop_ships_on_start reads what this test saw, so they share a worker
@pytest.mark.xdist_group("restart_now")
def test_restart_now(game):
    game.configure(
        OnRestartSequence="Reboot",
        StopShipsOnStart=True,
        AutoCommands=[
            auto("Reboot", [step("Announce", "Never", "00:01:00"), step("Restart")])
        ],
    )
    ship = prefabs.Grid("Coaster", FRONT, static=False, slab=(2, 1, 2))
    game.spawn(ship)
    game.a.probe("velocity", ship.entity_id, 0, 0, -1)
    count = game.server.ready_count()
    before = game.b.mark()
    send_at_once(game, "!ess restart now")
    restarted(game, count)
    assert "Never" not in [t for s, t in game.b.since(before) if s == "Server"]
    state = game.grid(ship)
    assert state, "the ship was saved and loaded"
    after_restart["Coaster"] = max(abs(v) for v in state["vel"])


@pytest.mark.xdist_group("restart_now")
@pytest.mark.xfail(strict=True, reason="StopShipsOnStart is not implemented, SE1-0239")
def test_stop_ships_on_start(game):
    assert after_restart.get("Coaster", 1.0) < 0.1


def test_restart_without_a_sequence_is_immediate(game):
    count = game.server.ready_count()
    send_at_once(game, "!ess restart")
    restarted(game, count)


def test_shutdown_sequence(game):
    game.configure(
        OnShutdownSequence="Halt",
        AutoCommands=[
            auto("Halt", [step("Announce", "Stopping in 2", "00:00:02"), step("Stop")])
        ],
    )
    saved = game.world_mtime()
    before = game.b.mark()
    assert game.a.ess("stop") == "Shutdown sequence 'Halt' started."
    game.b.wait_line(
        before, lambda s, t: s == "Server" and t == "Stopping in 2", what="the warning"
    )
    game.server.wait_exit()
    assert game.world_mtime() > saved
    game.server.start()
    game.rejoin()


def test_stop_now_and_a_broken_config(game):
    game.configure(
        OnShutdownSequence="Halt",
        AutoCommands=[auto("Halt", [step("None", delay="00:01:00"), step("Stop")])],
    )
    send_at_once(game, "!ess stop now")
    game.server.wait_exit(timeout=60)

    for old in game.server.data.glob("Essentials.cfg.corrupted.*"):
        old.unlink()
    game.server.plugin_cfg.write_text("<PluginConfig><Enabled>", encoding="utf-8")
    game.server.start()
    game.rejoin()
    assert len(list(game.server.data.glob("Essentials.cfg.corrupted.*.txt"))) == 1
    config = game.server.plugin_cfg.read_text(encoding="utf-8")
    assert "<MaxHomesPerPlayer>3</MaxHomesPerPlayer>" in config
    assert game.a.ess("listauto") == "Auto commands: 0 configured, 0 running"


def test_restart_from_inside_a_sequence_is_immediate(game):
    # A plain !ess restart in a sequence must not start the sequence again
    game.configure(
        OnRestartSequence="Loop",
        AutoCommands=[
            auto("Loop", [step("Announce", "Looping"), step("Command", "!ess restart")])
        ],
    )
    count = game.server.ready_count()
    send_at_once(game, "!ess restart")
    restarted(game, count)
    time.sleep(5)
    assert game.server.ready_count() == count + 1
