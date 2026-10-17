"""Who may run what. Every command is checked against the promote level its
source declares (commands.py reads it): player B, who has no rights, is
refused every command above None, the refusal reaches only B, and the command
line never shows up as chat for the other players. Then B is promoted to each
level in turn.

Arguments are left out on purpose: the host checks the permission before it
binds them, so a refused command never runs."""

from __future__ import annotations

import pytest

import commands
from session import NO_PERMISSION

ALL = commands.all_commands()
RESTRICTED = [c for c in ALL if c.level != "None"]
PUBLIC = [c for c in ALL if c.level == "None"]


def test_the_source_declares_every_documented_group():
    lines = {c.line for c in ALL}
    for line in (
        "!ess cleanup delete",
        "!ess voxels reset all",
        "!ess blocks remove type",
        "!ess admin makeship",
        "!ess transfer",
        "!ess ban",
        "!ess say",
        "!ess claim",
        "!ess acceptgrid",
        "!stone",
        "!fixship",
        "!reload",
    ):
        assert line in lines, line


@pytest.mark.parametrize("command", RESTRICTED, ids=lambda c: c.line)
def test_player_without_rights_is_refused(game, command):
    a_before = game.a.mark()
    a_chat = game.a.chat_latest()
    assert game.b.command(command.line) == NO_PERMISSION
    # The refusal is private, and the command line isn't relayed as chat
    assert game.a.since(a_before) == []
    assert command.line not in [t for _, t in game.a.relayed_since(a_chat)]


@pytest.mark.parametrize("command", PUBLIC, ids=lambda c: c.line)
def test_public_command_is_not_refused(game, command):
    # Commands that need arguments answer with their usage; the rest run
    reply = game.b.command(command.line)
    assert reply != NO_PERMISSION
    assert "Unknown command" not in reply


def test_overview_lists_only_what_the_caller_may_run(game):
    a_lines = game.a.command("!ess").splitlines()
    b_lines = game.b.command("!ess").splitlines()
    assert b_lines[0] == "=== Essentials - Essentials server commands ==="
    # The HUD keeps the last 100 lines and A's list is longer, so only its tail
    # arrives. The list is sorted ignoring case and shows one spelling of
    # commands registered under two (fulleconsync and FullEconSync).
    a_first = a_lines[0].split(" - ")[0].lower()

    def listed(lines, command):
        wanted = command.line.lower()
        return any(
            line.lower() == wanted or line.lower().startswith(wanted + " - ")
            for line in lines
        )

    for command in ALL:
        if command.root != "ess":
            continue
        if command.line.lower() >= a_first:
            assert listed(a_lines, command), command.line
        assert listed(b_lines, command) == (command.level == "None"), command.line


def test_help_for_one_command(game):
    reply = game.b.command("!ess help home add")
    assert "Usage: !ess home add" in reply


def test_unknown_command(game):
    assert game.b.command("!ess frobnicate") == "Unknown command. Try !ess help"


SAMPLES = {
    # level -> commands of that level that are harmless without arguments
    "Moderator": [
        "!ess pcu checkowner",
        "!ess pcu checkauthor",
        "!ess fixshipmodid",
        "!fixshipmodid",
    ],
    "SpaceMaster": [
        "!ess grids stopall",
        "!ess entities find",
        "!ess transfer",
        "!ess unprotect",
    ],
    "Admin": [
        "!ess worldpcu",
        "!ess listauto",
        "!ess stats",
        "!ess voxels protect status",
    ],
}


@pytest.mark.parametrize("level", ["Moderator", "SpaceMaster", "Admin"])
def test_promoted_player(game, level):
    assert game.a.probe("promote", game.b.steam_id, level) == f"Probe promote: {level}"
    for line in SAMPLES[level]:
        assert game.b.command(line) != NO_PERMISSION, line
    above = commands.LEVELS[commands.LEVELS.index(level) + 1]
    for command in [c for c in ALL if c.level == above][:6]:
        assert game.b.command(command.line) == NO_PERMISSION, command.line


def test_short_forms_run_the_same_code(game):
    assert (
        game.a.command("!fixshipmodid") == "Correct Usage is !ess fixshipmodid EntityID"
    )
    assert (
        game.a.command("!ess fixshipmodid")
        == "Correct Usage is !ess fixshipmodid EntityID"
    )
    assert game.b.command("!fixshipmod") == NO_PERMISSION
