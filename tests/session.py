"""A running test session: the server with its world, and the players on the
clients. Also usable from the command line while iterating::

    uv run python tests/session.py start     # server and both clients
    uv run python tests/session.py stop
"""

from __future__ import annotations

import datetime
import math
import sys
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import rig  # noqa: E402
import world  # noqa: E402

SERVER = rig.Server()
CLIENT_A = rig.Client(1, "Alpha")
CLIENT_B = rig.Client(2, "Bravo")
CLIENTS = (CLIENT_A, CLIENT_B)

REPLY_TIMEOUT_S = 20.0
# A reply that hasn't started by the time the ping after it is answered (one
# the plugin sends later) is complete once no more of it came for this long
QUIET_S = 1.0
POLL_S = 0.1
PING = "Probe ping: "
# Replies come from the command's root, errors from its title
ROOT_TITLES = {
    "ess": "Essentials",
    "eprobe": "EssentialsProbe",
    "stone": "Stone Cleanup",
    "fixship": "Essentials",
    "fixshipmod": "Essentials",
    "fixshipmodid": "Essentials",
    "reload": "Essentials",
}
NO_PERMISSION = "You do not have permission to use that command."

# The players wait here between tests, A at HOME and B 20 m to its +x
HOME = (0.0, 0.0, 0.0)
# Where the tests put their grids: in front of A (-z), at eye height
FRONT = (0.0, 0.0, -20.0)

# No NPC ships: the game spawns and removes encounters on its own, which would
# change the set of grids under the tests
WORLD_SETTINGS = {
    "EnableEncounters": "false",
    "GlobalEncounterCap": "0",
    "CargoShipsEnabled": "false",
    "EnableContainerDrops": "false",
    "EnableDrones": "false",
    "EnablePlanetaryEncounters": "false",
    "EnableVoxelDestruction": "true",
    "InfiniteAmmo": "true",
    "ProceduralDensity": "0",
}


def parse(reply: str) -> dict[str, str]:
    """`Probe x: a=1 b=2` as {"a": "1", "b": "2"}"""
    body = reply.split(": ", 1)[1]
    return dict(p.split("=", 1) for p in body.split() if "=" in p)


def vector(text: str) -> tuple[float, float, float]:
    x, y, z = (float(v) for v in text.split(","))
    return x, y, z


def offset(position, dx=0.0, dy=0.0, dz=0.0):
    return (position[0] + dx, position[1] + dy, position[2] + dz)


class Player:
    """A client's player: chat commands and moving around through Remote"""

    def __init__(self, client: rig.Client, identity_id: int):
        self.client = client
        self.identity_id = identity_id
        self.steam_id = client.steam_id
        self.name = client.name
        self.api = client.api()
        # Ping tokens, unique across Player objects of the same client
        self._pings = time.time_ns()

    # --- chat ----------------------------------------------------------------

    def hud(self) -> list[tuple[str, str, str]]:
        """Every chat line on the HUD, scripted messages included"""
        hud = self.api.get_chat_messages().get("hud") or []
        return [
            (m.get("sender"), m.get("text"), m.get("timestamp"))
            for m in hud
            if not (m.get("text") or "").startswith(PING)
        ]

    def mark(self) -> set:
        return set(self.hud())

    def since(self, before: set) -> list[tuple[str, str]]:
        return [(s, t) for s, t, ts in self.hud() if (s, t, ts) not in before]

    def relayed_since(self, latest: int) -> list[tuple[str, str]]:
        """Player chat this client got after `latest` (see chat_latest), as
        (author, text)"""
        got = self.api.get_chat_messages(latest).get("messages") or []
        return [(m.get("Author"), m.get("Text")) for m in got]

    def chat_latest(self) -> int:
        return self.api.get_chat_messages().get("latest", 0)

    def command(self, text: str, senders=None, timeout: float = REPLY_TIMEOUT_S) -> str:
        """Sends a chat command and returns its reply: the new lines from the
        command's root and title (or from `senders`), joined by newlines.

        A ping follows the command. The server answers commands in order, so
        once the ping is answered the reply is complete."""
        if senders is None:
            prefix = text.split()[0][1:]
            senders = {prefix, ROOT_TITLES.get(prefix, "Essentials")}
        before = self.mark()
        self._pings += 1
        pong = f"{PING}{self._pings}"
        self.api.send_chat(text)
        self.api.send_chat(f"!eprobe ping {self._pings}")
        deadline = time.monotonic() + timeout
        replies, last, ponged = [], None, False
        while time.monotonic() < deadline:
            hud = self.api.get_chat_messages().get("hud") or []
            ponged = ponged or any(m.get("text") == pong for m in hud)
            new = [
                (m.get("text") or "").rstrip()
                for m in hud
                if m.get("sender") in senders
                and (m.get("sender"), m.get("text"), m.get("timestamp")) not in before
                and not (m.get("text") or "").startswith(PING)
            ]
            if ponged and new:
                return "\n".join(new)
            if new != replies:
                replies, last = new, time.monotonic()
            elif replies and time.monotonic() - last > QUIET_S:
                return "\n".join(replies)
            time.sleep(POLL_S)
        if replies:
            return "\n".join(replies)
        raise TimeoutError(f"No reply to {text!r}")

    def ess(self, text: str, **kwargs) -> str:
        return self.command("!ess " + text, **kwargs)

    def say(self, text: str) -> None:
        """Chat that isn't a command"""
        self.api.send_chat(text)

    def wait_line(self, before: set, predicate, timeout: float = 15.0, what=""):
        """Waits for a new HUD line (sender, text) matching predicate and
        returns it with the time it came, in seconds since the epoch"""
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            for sender, text, stamp in self.hud():
                if (sender, text, stamp) not in before and predicate(sender, text):
                    return (
                        sender,
                        text,
                        datetime.datetime.fromisoformat(stamp).timestamp(),
                    )
            time.sleep(0.3)
        raise AssertionError(f"{self.name} got no line {what}: {self.since(before)}")

    def lines_from(self, before: set, sender: str) -> list[str]:
        return [t for s, t in self.since(before) if s == sender]

    def probe(self, *args) -> str:
        return self.command("!eprobe " + " ".join(str(a) for a in args))

    # --- HUD notifications ---------------------------------------------------

    def notification_mark(self) -> int:
        return self.api.get_hud_notifications().get("latest", 0)

    def notifications(self, after: int) -> list[dict]:
        return self.api.get_hud_notifications(after).get("notifications") or []

    def wait_notification(self, after: int, text: str, timeout: float = 15.0) -> dict:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            for n in self.notifications(after):
                if text in n.get("text", ""):
                    return n
            time.sleep(0.3)
        raise AssertionError(
            f"{self.name} got no notification {text!r}: {self.notifications(after)}"
        )

    # --- state ---------------------------------------------------------------

    def state(self) -> dict:
        """The probe's view of this player on the server"""
        return parse(players()[0].probe("player", self.steam_id))

    def position(self) -> tuple[float, float, float]:
        return vector(self.state()["pos"])

    # --- moving --------------------------------------------------------------

    def teleport(self, position, settle: float = 2.0) -> None:
        """The admin teleport. Player B may not use it, so the probe runs it
        for B on the server."""
        if self.client is CLIENT_A:
            self.api.character_teleport(*position)
        else:
            reply = players()[0].probe("tp", self.steam_id, *position)
            assert reply == "Probe tp: done", reply
        time.sleep(settle)

    def go_home(self) -> None:
        self.teleport(home_of(self))

    def look_at(self, position, settle: float = 1.0) -> None:
        """Turns the character to face a point; the server learns the new
        head orientation a moment later"""
        self.api.character_look_at(*position, tolerance=0.5)
        time.sleep(settle)

    def stand_and_look(self, stand, target) -> None:
        self.teleport(stand)
        self.look_at(target)

    def sit(self, seat_position, seat_id: int, timeout: float = 60.0) -> None:
        """Gets into a seat facing -z: stands in front of it, waits until the
        client has the seat under the crosshair, and uses it"""
        deadline = time.monotonic() + timeout
        stand = offset(seat_position, dz=-2.0)
        target = None
        while time.monotonic() < deadline:
            self.stand_and_look(stand, seat_position)
            target = self.api.get_character_target(4.0)
            if (target.get("block") or {}).get(
                "blockType"
            ) == "MyObjectBuilder_Cockpit":
                self.api.character_use()
                for _ in range(10):
                    if self.state().get("seat") == str(seat_id):
                        return
                    time.sleep(0.5)
            time.sleep(1)
        raise AssertionError(
            f"{self.name} could not sit in {seat_id}, last target {target}"
        )


def home_of(player: Player):
    """A and B wait 20 m apart, so they never stand in each other"""
    dx = 0.0 if player.client is CLIENT_A else 20.0
    return offset(HOME, dx)


def distance(a, b) -> float:
    return math.dist(a, b)


# ---------------------------------------------------------------------------
# Start and stop
# ---------------------------------------------------------------------------


def prepare() -> None:
    """A fresh world and the server's configs"""
    world.prepare(
        rig.EMPTY_WORLD,
        SERVER.world,
        {
            CLIENT_A.steam_id: (world.PLAYER_A, CLIENT_A.name),
            CLIENT_B.steam_id: (world.PLAYER_B, CLIENT_B.name),
        },
        WORLD_SETTINGS,
    )
    SERVER.prepare([CLIENT_A.steam_id])
    SERVER.write_plugin_config(rig.plugin_config())


def join(client: rig.Client) -> rig.RemoteAPI:
    """Launches a client, which connects on its own, and waits for its
    character"""
    client.launch(SERVER.port)
    api = client.wait_api()
    rig.wait_world(api)
    rig.ensure_character(api)
    rig.focus_gameplay(api)
    return api


def join_all(clients=CLIENTS) -> None:
    """Joins the clients in parallel, and sends the players home"""
    errors = []

    def run(client):
        try:
            join(client)
        except Exception as error:  # noqa: BLE001 -- reported below
            errors.append((client.name, error))

    threads = [threading.Thread(target=run, args=(c,)) for c in clients]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    if errors:
        raise RuntimeError(f"Joining failed: {errors}")
    for player in players():
        if player.client in clients:
            player.go_home()


def stop_all() -> None:
    for client in CLIENTS:
        client.stop()
    SERVER.stop()


_players: tuple[Player, Player] | None = None


def players() -> tuple[Player, Player]:
    """A and B. Their Remote APIs stay valid across client restarts."""
    global _players
    if _players is None:
        _players = Player(CLIENT_A, world.PLAYER_A), Player(CLIENT_B, world.PLAYER_B)
    return _players


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "start":
        stop_all()
        prepare()
        SERVER.start()
        join_all()
        print(
            f"Joined. Remote ports {CLIENT_A.port}, {CLIENT_B.port}; server log {SERVER.log}"
        )
    elif command == "stop":
        stop_all()
    else:
        sys.exit(__doc__)
