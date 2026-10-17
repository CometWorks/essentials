"""Auto commands: running, listing and cancelling them, every step action, the
time and condition triggers, and player votes. OnStart and the restart and
shutdown sequences are in test_lifecycle.py.

Announcements come as chat from "Server" to every player, so the tests watch
player B's HUD for them."""

from __future__ import annotations

import datetime
import time

import pytest

from conftest import auto, step, wait_until


def announced(game, before, text, timeout=15.0, player=None):
    """Waits for a Server chat line and returns when it came, in seconds"""
    return (player or game.b).wait_line(
        before, lambda s, t: s == "Server" and t == text, timeout, what=text
    )[2]


def server_lines(game, before, player=None) -> list[str]:
    return (player or game.b).lines_from(before, "Server")


def log_count(game, text: str, magnetar: bool = False) -> int:
    return (game.magnetar_log() if magnetar else game.plugin_log()).count(text)


def now() -> float:
    return time.time()


# --- running, listing, cancelling --------------------------------------------


def test_runauto_runs_the_steps_in_order_with_their_delays(game):
    game.configure(
        AutoCommands=[
            auto(
                "Countdown",
                [
                    step("Announce", "Three", "00:00:03"),
                    step("Announce", "Two", "00:00:02"),
                    step("Announce", "One"),
                ],
            )
        ]
    )
    before = game.b.mark()
    assert game.a.ess("runauto Countdown") == "Auto command 'Countdown' started."
    t3 = announced(game, before, "Three")
    t2 = announced(game, before, "Two")
    t1 = announced(game, before, "One")
    assert 2.0 < t2 - t3 < 5.0
    assert 1.0 < t1 - t2 < 4.0
    assert server_lines(game, before) == ["Three", "Two", "One"]
    wait_until(lambda: game.auto_running() == [], 5, "the sequence to finish")


def test_runauto_by_name_ignores_case(game):
    game.configure(AutoCommands=[auto("Hello World", [step("Announce", "Hi")])])
    before = game.b.mark()
    assert game.a.ess("runauto hello world") == "Auto command 'hello world' started."
    announced(game, before, "Hi")


def test_runauto_bad_input(game):
    game.configure(AutoCommands=[auto("Empty", [])])
    assert game.a.ess("runauto") == "Usage: !ess runauto <name>"
    assert game.a.ess("runauto Nothing") == "No auto command named 'Nothing'."
    # A command without steps cannot run
    assert game.a.ess("runauto Empty") == "No auto command named 'Empty'."


def test_a_running_command_does_not_start_twice(game):
    game.configure(
        AutoCommands=[
            auto("Slow", [step("Announce", "Slow start", "00:00:20"), step("None")])
        ]
    )
    game.a.ess("runauto Slow")
    game.a.ess("runauto Slow")
    assert game.auto_running() == ["Slow"]
    assert (
        "Auto command 'Slow' is already running; skipping (runauto)"
        in game.plugin_log()
    )


def test_listauto(game):
    game.configure(
        AutoCommands=[
            auto("Slow", [step("None", delay="00:00:30"), step("None")]),
            auto("Ticker", [step("None")], trigger="Timed", Interval="01:00:00"),
        ]
    )
    assert game.a.ess("listauto").splitlines() == [
        "Auto commands: 2 configured, 0 running",
        "- Slow [Disabled] 2 step(s)",
        "- Ticker [Timed] 1 step(s)",
    ]
    game.a.ess("runauto Slow")
    assert game.a.ess("listauto").splitlines()[-3:] == [
        "- Ticker [Timed] 1 step(s)",
        "Running auto commands:",
        "1. Slow",
    ]


def test_cancelauto_stops_the_sequence(game):
    game.configure(
        AutoCommands=[
            auto(
                "Long", [step("Announce", "Begin", "00:00:06"), step("Announce", "End")]
            )
        ]
    )
    before = game.b.mark()
    game.a.ess("runauto Long")
    announced(game, before, "Begin")
    assert game.a.ess("cancelauto long") == "Cancelled 1 instance(s) of 'long'."
    time.sleep(8)
    assert "End" not in server_lines(game, before)
    assert game.auto_running() == []


def test_cancelauto_bad_input(game):
    assert game.a.ess("cancelauto") == "Usage: !ess cancelauto <name>"
    assert game.a.ess("cancelauto Ghost") == "No running auto command named 'Ghost'."


def test_cancel_by_index(game):
    game.configure(
        AutoCommands=[
            auto("First", [step("None", delay="00:00:30"), step("None")]),
            auto("Second", [step("None", delay="00:00:30"), step("None")]),
        ]
    )
    game.a.ess("runauto First")
    game.a.ess("runauto Second")
    assert game.auto_running() == ["First", "Second"]
    assert game.a.ess("admin cancelautobyindex 3") == "3 is out of range."
    assert (
        game.a.ess("admin cancelautobyindex 0")
        == "Usage: !ess admin cancelautobyindex <index>"
    )
    assert game.a.ess("admin cancelautobyindex 2") == "Auto command 'Second' cancelled."
    assert game.auto_running() == ["First"]


# --- step actions ------------------------------------------------------------


def test_command_step_runs_essentials_commands_as_the_server(game):
    game.configure(
        AutoCommands=[
            auto(
                "Cmd",
                [
                    step("Command", "!ess broadcast From a step"),
                    # The leading ! is optional, and the server may run admin commands
                    step("Command", "ess voxels protect missiles on"),
                ],
            )
        ]
    )
    before = game.b.mark()
    game.a.ess("runauto Cmd")
    announced(game, before, "From a step")
    wait_until(
        lambda: game.cfg("ProtectVoxelsFromMissiles") == "True",
        5,
        "the protect command",
    )


def test_command_step_cannot_reach_other_roots(game):
    game.configure(AutoCommands=[auto("Other", [step("Command", "!stone")])])
    warnings = log_count(game, "unrecognised command line '!stone'")
    game.a.ess("runauto Other")
    wait_until(
        lambda: log_count(game, "unrecognised command line '!stone'") > warnings,
        10,
        "the warning",
    )


def test_notify_step_fonts(game):
    game.configure(
        AutoCommands=[
            auto(
                "Notes",
                [
                    step(
                        "Notify", "Font note", NotifyFont="Blue", NotifyDurationMs=2000
                    ),
                    # Default takes the legacy Color field
                    step("Notify", "Legacy note", Color="Green"),
                    step("Notify", "Plain note"),
                ],
            )
        ]
    )
    mark = game.b.notification_mark()
    game.a.ess("runauto Notes")
    assert game.b.wait_notification(mark, "Font note")["font"] == "Blue"
    assert game.b.wait_notification(mark, "Legacy note")["font"] == "Green"
    assert game.b.wait_notification(mark, "Plain note")["font"] == "White"


def test_announce_step_with_a_colour(game):
    game.configure(
        AutoCommands=[
            auto(
                "Colour",
                [
                    step("Announce", "Red text", Color="Red"),
                    step("Announce", "RGB text", Color="10 200 30"),
                ],
            )
        ]
    )
    before = game.b.mark()
    game.a.ess("runauto Colour")
    announced(game, before, "Red text")
    announced(game, before, "RGB text")


def test_save_step(game):
    game.configure(AutoCommands=[auto("Saver", [step("Save")])])
    before = game.world_mtime()
    time.sleep(1.1)
    game.a.ess("runauto Saver")
    wait_until(lambda: game.world_mtime() > before, 30, "the save")


def test_reload_config_step(game):
    game.configure(AutoCommands=[auto("Reloader", [step("ReloadConfig")])])
    count = log_count(game, "Dedicated config reloaded", magnetar=True)
    game.a.ess("runauto Reloader")
    wait_until(
        lambda: log_count(game, "Dedicated config reloaded", magnetar=True) > count,
        30,
        "the reload",
    )


def test_runauto_step_chains_commands(game):
    game.configure(
        AutoCommands=[
            auto(
                "Outer",
                [
                    step("Announce", "Outer"),
                    step("RunAuto", "Inner"),
                    step("RunAuto", "Missing"),
                ],
            ),
            auto("Inner", [step("Announce", "Inner")]),
        ]
    )
    warnings = log_count(game, "no auto command named 'Missing'")
    before = game.b.mark()
    game.a.ess("runauto Outer")
    announced(game, before, "Outer")
    announced(game, before, "Inner")
    wait_until(
        lambda: log_count(game, "no auto command named 'Missing'") > warnings,
        10,
        "the warning",
    )


def shell_script(game, name: str, body: str):
    """An executable script in the server's folder; its path has no spaces"""
    path = game.server.root / name
    path.write_text("#!/bin/sh\n" + body + "\n")
    path.chmod(0o755)
    return path


def test_shell_script_runs_before_the_action(game):
    marker = game.server.root / "shell-marker"
    marker.unlink(missing_ok=True)
    script = shell_script(game, "shell-step.sh", f"sleep 2\necho ran > '{marker}'")
    game.configure(
        AutoCommands=[
            auto("Shell", [step("Announce", "After shell", ShellScript=str(script))])
        ]
    )
    before = game.b.mark()
    started = now()
    game.a.ess("runauto Shell")
    done = announced(game, before, "After shell")
    assert marker.read_text().strip() == "ran"
    assert done - started > 1.5


@pytest.mark.xfail(
    strict=True,
    reason="the script goes through ProcessStartInfo.Arguments, which splits it at spaces, SE1-0238",
)
def test_shell_script_with_arguments(game):
    marker = game.server.root / "shell-args-marker"
    marker.unlink(missing_ok=True)
    game.configure(
        AutoCommands=[
            auto(
                "Shell",
                [step("Announce", "After echo", ShellScript=f"echo ran > {marker}")],
            )
        ]
    )
    before = game.b.mark()
    game.a.ess("runauto Shell")
    announced(game, before, "After echo")
    assert marker.exists()


def test_shell_script_timeout(game):
    script = shell_script(game, "shell-hang.sh", "sleep 60")
    game.configure(
        AutoCommands=[
            auto(
                "Hang",
                [
                    step(
                        "Announce",
                        "After hang",
                        ShellScript=str(script),
                        ShellTimeoutSeconds=3,
                    ),
                ],
            )
        ]
    )
    before = game.b.mark()
    started = now()
    game.a.ess("runauto Hang")
    done = announced(game, before, "After hang", timeout=20)
    assert 2.5 < done - started < 15
    wait_until(
        lambda: "shell script timed out" in game.plugin_log(), 10, "the timeout warning"
    )


def test_none_step_is_a_pure_delay(game):
    game.configure(
        AutoCommands=[
            auto(
                "Wait",
                [
                    step("Announce", "Before", "00:00:01"),
                    step("None", delay="00:00:03"),
                    step("Announce", "After"),
                ],
            )
        ]
    )
    before = game.b.mark()
    game.a.ess("runauto Wait")
    t0 = announced(game, before, "Before")
    t1 = announced(game, before, "After")
    assert t1 - t0 > 3.0


# --- triggers ----------------------------------------------------------------


def test_timed_trigger_repeats(game):
    game.configure(
        AutoCommands=[
            auto(
                "Tick", [step("Announce", "Tick")], trigger="Timed", Interval="00:00:04"
            )
        ]
    )
    before = game.b.mark()
    started = now()
    first = announced(game, before, "Tick", timeout=10)
    # Not at once: the first run waits one interval
    assert first - started > 2.5
    wait_until(
        lambda: server_lines(game, before).count("Tick") >= 2, 10, "a second tick"
    )


def test_timed_trigger_needs_a_positive_interval(game):
    before = game.b.mark()
    game.configure(
        AutoCommands=[
            auto(
                "Zero", [step("Announce", "Zero")], trigger="Timed", Interval="00:00:00"
            ),
            auto("Junk", [step("Announce", "Junk")], trigger="Timed", Interval="soon"),
        ]
    )
    time.sleep(6)
    assert server_lines(game, before) == []


def _time_of_day(seconds_ahead: float) -> str:
    return (
        datetime.datetime.now() + datetime.timedelta(seconds=seconds_ahead)
    ).strftime("%H:%M:%S")


def test_scheduled_trigger_fires_at_the_time_of_day(game):
    at = _time_of_day(8)
    game.configure(
        AutoCommands=[
            auto(
                "Clock",
                [step("Announce", "Clock")],
                trigger="Scheduled",
                Interval=at,
                DayOfWeek="All",
            )
        ]
    )
    before = game.b.mark()
    started = now()
    fired = announced(game, before, "Clock", timeout=20)
    assert fired - started > 5
    time.sleep(4)
    assert server_lines(game, before).count("Clock") == 1


def test_scheduled_trigger_skips_other_days(game):
    before = game.b.mark()
    tomorrow = (datetime.datetime.now() + datetime.timedelta(days=1)).strftime("%A")
    at = _time_of_day(4)
    game.configure(
        AutoCommands=[
            auto(
                "Other day",
                [step("Announce", "Other day")],
                trigger="Scheduled",
                Interval=at,
                DayOfWeek=tomorrow,
            )
        ]
    )
    time.sleep(9)
    assert server_lines(game, before) == []


def test_player_count_trigger_with_cooldown(game):
    before = game.b.mark()
    game.configure(
        AutoCommands=[
            auto(
                "Crowd",
                [step("Announce", "Crowd")],
                trigger="PlayerCount",
                Compare="GreaterThan",
                TriggerCount=1,
                Interval="00:10:00",
            ),
            auto(
                "Empty",
                [step("Announce", "Empty")],
                trigger="PlayerCount",
                Compare="LessThan",
                TriggerCount=1,
            ),
            auto(
                "Pair",
                [step("Announce", "Pair")],
                trigger="PlayerCount",
                Compare="Equal",
                TriggerCount=2,
                Interval="00:10:00",
            ),
        ]
    )
    announced(game, before, "Crowd")
    announced(game, before, "Pair")
    time.sleep(5)
    lines = server_lines(game, before)
    assert lines.count("Crowd") == 1 and lines.count("Pair") == 1
    assert "Empty" not in lines


def test_grid_count_trigger(game):
    import prefabs
    from session import FRONT

    game.configure(
        AutoCommands=[
            auto(
                "Grids",
                [step("Announce", "Grids")],
                trigger="GridCount",
                Compare="GreaterThan",
                TriggerCount=0,
                Interval="00:10:00",
            ),
        ]
    )
    before = game.b.mark()
    time.sleep(4)
    assert server_lines(game, before) == []
    game.spawn(prefabs.Grid("Counted", FRONT))
    announced(game, before, "Grids")


def test_sim_speed_trigger_waits_for_the_condition_to_hold(game):
    # The 4 s dwell starts with the reload inside configure()
    started = now()
    game.configure(
        AutoCommands=[
            auto(
                "Fast",
                [step("Announce", "Fast")],
                trigger="SimSpeed",
                Compare="GreaterThan",
                TriggerRatio=0.1,
                TriggerCount=4,
                Interval="00:10:00",
            ),
            auto(
                "Slow",
                [step("Announce", "Slow")],
                trigger="SimSpeed",
                Compare="LessThan",
                TriggerRatio=0.05,
                TriggerCount=0,
            ),
        ]
    )
    before = game.b.mark()
    fired = announced(game, before, "Fast", timeout=20)
    assert fired - started > 3.0
    assert "Slow" not in server_lines(game, before)


def test_disabled_plugin_runs_no_auto_commands(game):
    before = game.b.mark()
    game.configure(
        Enabled=False,
        AutoCommands=[
            auto(
                "Tick", [step("Announce", "Tick")], trigger="Timed", Interval="00:00:02"
            )
        ],
    )
    time.sleep(6)
    assert server_lines(game, before) == []


# --- votes -------------------------------------------------------------------

VOTES = [
    auto("Night", [step("Announce", "Night falls")], trigger="Vote", TriggerRatio=0.6),
    auto("Party", [step("Announce", "Party time")], trigger="Vote", TriggerRatio=0.5),
    auto("Plain", [step("Announce", "Not a vote")]),
]


def test_vote_list(game):
    assert game.b.ess("vote list") == "No vote commands are configured."
    game.configure(AutoCommands=VOTES)
    assert game.b.ess("vote list").splitlines() == [
        "Vote commands (start with !ess vote <name>):",
        "- Night",
        "- Party",
    ]


def test_vote_passes_and_runs_the_command(game):
    game.configure(AutoCommands=VOTES, VoteDurationSeconds=5)
    before = game.a.mark()
    assert game.b.ess("vote party") == "Vote 'Party' started."
    announced(
        game,
        before,
        "Vote started: Party. Type !ess vote yes or !ess vote no (5s).",
        player=game.a,
    )
    assert game.a.ess("vote no") == "Vote registered: no."
    # 1 yes (the starter), 1 no: half passes a 0.5 ratio
    announced(game, before, "Vote 'Party' passed (1/2).", player=game.a)
    announced(game, before, "Party time", player=game.a)
    debug = game.a.ess("vote debug").splitlines()
    assert "Last vote: Party" in debug and "Last result: passed" in debug


def test_vote_fails_below_the_ratio(game):
    game.configure(AutoCommands=VOTES, VoteDurationSeconds=5)
    before = game.a.mark()
    game.b.ess("vote Night")
    game.a.ess("vote no")
    announced(game, before, "Vote 'Night' failed (1/2).", player=game.a)
    time.sleep(2)
    assert "Night falls" not in server_lines(game, before, player=game.a)


def test_changing_a_vote(game):
    game.configure(AutoCommands=VOTES, VoteDurationSeconds=5)
    before = game.a.mark()
    game.b.ess("vote Night")
    game.a.ess("vote no")
    assert game.a.ess("vote yes") == "Vote registered: yes."
    announced(game, before, "Vote 'Night' passed (2/2).", player=game.a)
    announced(game, before, "Night falls", player=game.a)


def test_vote_bad_input(game):
    game.configure(AutoCommands=VOTES, VoteDurationSeconds=30)
    assert game.b.ess("vote yes") == "No vote is in progress."
    assert game.b.ess("vote no") == "No vote is in progress."
    assert game.b.ess("vote Plain") == "No vote command named 'Plain'."
    assert game.b.ess("vote Nothing") == "No vote command named 'Nothing'."
    assert game.b.ess("vote") == "Usage: !ess vote <name> | yes | no | list"
    game.b.ess("vote Night")
    assert game.a.ess("vote Party") == "A vote is already in progress."


def test_vote_cancel_debug_and_reset(game):
    game.configure(AutoCommands=VOTES, VoteDurationSeconds=30)
    assert game.a.ess("vote cancel") == "No vote is in progress."
    before = game.b.mark()
    game.b.ess("vote Night")
    debug = game.a.ess("vote debug").splitlines()
    assert debug[0] == "Voting state:"
    assert "Current vote: Night" in debug
    assert "Votes: 1 yes, 0 no (100% yes)" in debug
    assert game.a.ess("vote cancel") == "Vote 'Night' cancelled."
    announced(game, before, "Vote 'Night' cancelled.")
    assert "Last result: cancelled" in game.a.ess("vote debug").splitlines()
    assert game.a.ess("vote reset") == "Vote state reset."
    debug = game.a.ess("vote debug").splitlines()
    assert (
        "Current vote: (none)" in debug
        and "Last vote: (none)" in debug
        and "Last result: none" in debug
    )


@pytest.mark.parametrize("seconds", [5])
def test_vote_duration(game, seconds):
    game.configure(AutoCommands=VOTES, VoteDurationSeconds=seconds)
    before = game.a.mark()
    started = now()
    game.b.ess("vote Party")
    ended = announced(
        game, before, "Vote 'Party' passed (1/1).", timeout=seconds + 10, player=game.a
    )
    assert seconds - 1 < ended - started < seconds + 5
