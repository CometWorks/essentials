"""One session for the whole run, or for each pytest-xdist worker: the server
with the test world, player A on client 1 (an administrator) and player B on
client 2 (no rights, in the faction ETF with player C, who never joins).

Every test starts from an empty world (no grids and no characters but A's and
B's), the default plugin config with the plugin's cooldowns, confirmations,
mutes, homes and votes forgotten, B back to no rights, no GPS entries and both
players at home. A test spawns the grids it needs and changes the config it
needs through the probe (`configure`), so the server keeps running between
tests. Only test_lifecycle.py restarts or stops it.

ES_ATTACH=1 reuses a running server and clients (session.py start), ES_KEEP=1
leaves them running after the run.
"""

from __future__ import annotations

import os
import sys
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent))

import prefabs  # noqa: E402
import rig  # noqa: E402
import session  # noqa: E402
from session import CLIENTS, SERVER, parse, vector  # noqa: E402

ATTACH = os.environ.get("ES_ATTACH") == "1"
KEEP = os.environ.get("ES_KEEP") == "1"


def step(action: str, command: str = "", delay: str = "00:00:00", **fields) -> dict:
    """A CommandStep of an auto command"""
    return {
        "_tag": "CommandStep",
        "Action": action,
        "Command": command,
        "Delay": delay,
        **fields,
    }


def auto(name: str, steps, trigger: str = "Disabled", **fields) -> dict:
    """An AutoCommand"""
    return {
        "_tag": "AutoCommand",
        "Name": name,
        "Trigger": trigger,
        "Steps": list(steps),
        **fields,
    }


def wait_until(predicate, timeout: float, what: str, interval: float = 0.5):
    deadline = time.monotonic() + timeout
    while True:
        value = predicate()
        if value:
            return value
        if time.monotonic() > deadline:
            raise AssertionError(f"Timed out waiting for {what}")
        time.sleep(interval)


class Game:
    def __init__(self):
        self.a, self.b = session.players()
        self.server = SERVER
        self._spawns = 0

    # --- config ------------------------------------------------------------

    def configure(self, **options) -> None:
        """Puts the default config with these options into the running plugin"""
        SERVER.probe_cfg.write_text(rig.plugin_config(**options), encoding="utf-8")
        assert self.a.probe("reload") == "Probe: config reloaded"

    def cfg(self, name: str) -> str:
        """A live config value of the plugin"""
        return parse(self.a.probe("cfg", name))[name]

    def saved_cfg(self) -> str:
        """Essentials.cfg as the plugin saved it, half a second after a change"""
        time.sleep(1.5)
        return SERVER.plugin_cfg.read_text(encoding="utf-8")

    # --- start state ---------------------------------------------------------

    def reset(self) -> None:
        """The state every test starts from. Brings the server and the
        clients back if a failed test left them down."""
        if not SERVER.pid():
            SERVER.start()
        down = [c for c in CLIENTS if not c.pid()]
        if down:
            if not self.b.client.pid():
                # A test that banned B and failed
                self.a.ess(f"unban {self.b.steam_id}")
            for client in down:
                client.stop()
            session.join_all(down)
        SERVER.probe_cfg.write_text(rig.plugin_config(), encoding="utf-8")
        homes = [(p.steam_id, *session.home_of(p)) for p in (self.a, self.b)]
        for _ in range(5):
            reply = self.a.probe("reset", *homes[0], *homes[1])
            if not reply.startswith("Probe reset: seated="):
                break
            seated = reply.removeprefix("Probe reset: seated=").split(",")
            for player in (self.a, self.b):
                if str(player.steam_id) in seated:
                    player.api.character_use()
            time.sleep(2)
        assert reply.startswith("Probe reset: moved="), reply
        if reply != "Probe reset: moved=0":
            time.sleep(2)

    def rejoin(self, clients=CLIENTS) -> None:
        """After a server restart, or a kick"""
        for client in clients:
            client.stop()
        session.join_all(clients)

    # --- grids ---------------------------------------------------------------

    def spawn(self, *grids: prefabs.Grid) -> None:
        """Puts the grids into the world, ids as made by prefabs.Grid"""
        self._spawns += 1
        name = f"spawn/{self._spawns}.xml"
        (SERVER.data / name).write_text(prefabs.prefab_file(grids), encoding="utf-8")
        reply = self.a.probe("spawn", name)
        ids = [int(i) for i in reply.removeprefix("Probe spawn: ").split()]
        assert ids == [g.entity_id for g in grids], reply

    def grid(self, g: prefabs.Grid | int) -> dict:
        """The probe's view of a grid, empty when the server has no such grid"""
        reply = self.a.probe("grid", g if isinstance(g, int) else g.entity_id)
        if reply == "Probe grid: none":
            return {}
        state = parse(reply)
        for key in ("static", "editable", "destructible"):
            state[key] = state[key] == "1"
        for key in ("pos", "vel"):
            state[key] = vector(state[key])
        state["blocks"] = int(state["blocks"])
        state["pcu"] = int(state["pcu"])
        state["owners"] = [int(o) for o in state["owners"].split(",") if o]
        return state

    def exists(self, g: prefabs.Grid | int) -> bool:
        return bool(self.grid(g))

    def named(self, name: str) -> list[int]:
        reply = self.a.probe("named", f'"{name}"')
        return [int(i) for i in reply.removeprefix("Probe named:").split()]

    def block(self, block_id: int) -> dict:
        reply = self.a.probe("block", block_id)
        return {} if reply == "Probe block: none" else parse(reply)

    def inventory(self, entity_id: int) -> dict[str, float]:
        body = self.a.probe("inv", entity_id).removeprefix("Probe inv: ")
        if body in ("empty", "none"):
            return {}
        return {k: float(v) for k, v in (p.split("=") for p in body.split())}

    def balance(self, owner_id: int) -> int:
        return int(self.a.probe("balance", owner_id).removeprefix("Probe balance: "))

    def set_balance(self, owner_id: int, amount: int) -> None:
        assert (
            self.a.probe("setbalance", owner_id, amount)
            == f"Probe setbalance: {amount}"
        )

    def auto_running(self) -> list[str]:
        body = self.a.probe("auto").removeprefix("Probe auto:").strip()
        return body.split("|") if body else []

    # --- logs and the saved world ---------------------------------------------

    def plugin_log(self) -> str:
        """The game log of the server, where the plugin logs"""
        assert self.a.probe("flushlog") == "Probe flushlog: done"
        return SERVER.game_log.read_text(errors="replace")

    def magnetar_log(self) -> str:
        return SERVER.magnetar_log.read_text(errors="replace")

    def world_mtime(self) -> float:
        return (SERVER.world / "SANDBOX_0_0_0_.sbs").stat().st_mtime


def confirm(player: session.Player, command: str) -> tuple[str, str]:
    """Runs a command that asks for confirmation twice: the prompt, then the
    result"""
    prompt = player.ess(command)
    return prompt, player.ess(command)


@pytest.fixture(scope="session")
def session_game():
    attach = ATTACH and SERVER.pid() and all(c.pid() for c in CLIENTS)
    try:
        if not attach:
            session.stop_all()
            session.prepare()
            SERVER.start()
            session.join_all()
        yield Game()
    finally:
        if not KEEP:
            session.stop_all()


@pytest.fixture
def game(session_game):
    session_game.reset()
    return session_game


def pytest_collection_modifyitems(items):
    """The tests that restart the server or rejoin a client take minutes each,
    so they go first: run in parallel, the short tests fill in around them
    instead of waiting at the end"""
    slow = ("test_lifecycle", "test_joining")
    items.sort(key=lambda item: item.module.__name__ not in slow)
