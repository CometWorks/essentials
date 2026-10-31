"""`!ess grids list` with GPS markers on a cluster (SE-810), through the cluster's own scenario bench
(CometWorks/cluster Test/). The pytest suite runs on a plain server and can't see this.

    python tests/cluster_grids.py CLUSTER_TEST_DIR

CLUSTER_TEST_DIR is the Test/ folder of a CometWorks/cluster checkout with its host.env (CLUSTER_PACKAGE,
test root, port base, MAGNETAR_HOME) and `uv sync` done; run this with that folder's virtualenv python.
WA + 2 regular nodes + one client without rights, on the package's example world. The grids are spawned on
the node they should live on, and Essentials runs from this working copy with MarkerShowPosition on.
"""

from __future__ import annotations

import json
import math
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote

REPO = Path(__file__).resolve().parents[1]
TEST_DIR = Path(sys.argv[1]).resolve()
sys.path[:0] = [str(TEST_DIR), str(TEST_DIR.parent / "Cli"), str(REPO / "tests")]

import bench as bench_module  # noqa: E402  (loads host.env, sets MAGNETAR_HOME)
from cluster import ClusterError, wait_until  # noqa: E402
from scenarios_core import (  # noqa: E402
    CLIENT_ID,
    _client_partition,
    _handover_url,
    _partitions,
)

ROOT = Path.home() / "cluster-runs/essentials-grids"
PLUGINS = ROOT / "plugins"
BLUEPRINT = ROOT / "blueprint.sbc"
PLUGIN_ID = "DF09925B-6D53-41F2-899C-CA6214B5DD6F"
NODES = ["node-1", "node-2"]
PROCESSES = ["wa-1", *NODES]
PREFIX = "Ess810"
NEAR = PREFIX + "Near"
FAR = PREFIX + "Far"


def build() -> None:
    """Essentials as a local plugin (DLL + manifest) for CLUSTER_EXTRA_PLUGINS"""
    out = PLUGINS / ".build"
    subprocess.run(
        ["nice", "dotnet", "build", str(REPO / "ServerPlugin/ServerPlugin.csproj"), "-c", "Release",
         "--nologo", "-v", "q", f"-p:Magnetar={os.environ['MAGNETAR_HOME']}", "-o", str(out)],
        check=True,
    )  # fmt: skip
    folder = PLUGINS / "Essentials"
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "Essentials.dll").write_bytes((out / "Essentials.dll").read_bytes())
    (folder / "Essentials.xml").write_text(
        '<?xml version="1.0"?>\n<PluginData xmlns:xsd="http://www.w3.org/2001/XMLSchema" '
        'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="GitHubPlugin">'
        f"<Id>{PLUGIN_ID}</Id><FriendlyName>Essentials</FriendlyName><RepoId>CometWorks/essentials</RepoId>"
        "<Commit>0000000000000000000000000000000000000000</Commit><Runtimes>CoreCLR</Runtimes></PluginData>\n",
        encoding="utf-8",
    )
    # One battery: a grid has an owner only through a block that can have one
    BLUEPRINT.write_text(
        '<?xml version="1.0"?>\n<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        'xmlns:xsd="http://www.w3.org/2001/XMLSchema"><ShipBlueprints>'
        '<ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">'
        '<Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="Ess810" />'
        '<CubeGrids><CubeGrid><SubtypeName /><EntityId>0</EntityId>'
        "<PersistentFlags>InScene</PersistentFlags><GridSizeEnum>Large</GridSizeEnum><CubeBlocks>"
        '<MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_BatteryBlock">'
        "<SubtypeName>LargeBlockBatteryBlock</SubtypeName></MyObjectBuilder_CubeBlock>"
        "</CubeBlocks><DisplayName>Ess810</DisplayName></CubeGrid></CubeGrids>"
        "</ShipBlueprint></ShipBlueprints></Definitions>\n",
        encoding="utf-8",
    )


def run(bench) -> None:
    cluster = bench.cluster
    cluster.kill()
    cluster.prepare(wipe=True)
    for process in PROCESSES:
        data = bench.run_root / f"{process}-data"
        data.mkdir(parents=True, exist_ok=True)
        (data / "Essentials.cfg").write_text(
            '<?xml version="1.0" encoding="utf-8"?>\n<PluginConfig '
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
            'xmlns:xsd="http://www.w3.org/2001/XMLSchema">'
            "<MarkerShowPosition>true</MarkerShowPosition></PluginConfig>\n",
            encoding="utf-8",
        )
    cluster.start(PROCESSES)
    cluster.wait_ready(nodes=PROCESSES)
    api = bench.start_client(CLIENT_ID)
    bench.wait_spawned(CLIENT_ID)

    def client_node() -> str | None:
        for node in NODES:
            try:
                if _client_partition(_partitions(bench, node)) is not None:
                    return node
            except (ClusterError, OSError):
                pass
        return None

    home = wait_until(client_node, 120, "the client's partition")
    other = NODES[1 - NODES.index(home)]
    player = cluster.node(home, f"/respawn-test-player?userId={CLIENT_ID}")
    identity = int(player["identityId"])
    here = tuple(api.get_character()["position"])

    def spawn(node: str, name: str, x_offset: float) -> tuple[int, tuple]:
        position = (here[0] + x_offset, here[1], here[2])
        reply = cluster.node(
            node,
            f"/blueprint-test-spawn?path={quote(str(BLUEPRINT))}&x={position[0]}&y={position[1]}"
            f"&z={position[2]}&owner={identity}&static=true&name={name}",
            "POST",
        )
        grid = reply["grids"][0]
        wait_until(
            lambda: cluster.node(node, f"/partition-of?entity={grid}").get("partition"),
            60,
            f"{name} in a partition of {node}",
        )
        return grid, position

    def hud() -> set:
        return {
            (m.get("sender"), m.get("text"), m.get("timestamp"))
            for m in api.get_chat_messages().get("hud") or []
        }

    def grids_list() -> str:
        before = hud()
        api.send_chat("!ess grids list")
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            found = [t for s, t, _ in hud() - before if s == "Essentials"]
            if found:
                return "\n".join(found)
            time.sleep(0.5)
        raise ClusterError("No reply to !ess grids list")

    def client_markers() -> dict[str, list[tuple]]:
        markers: dict[str, list[tuple]] = {}
        for entry in api.list_gps().get("entries", []):
            if (entry.get("name") or "").startswith(PREFIX):
                markers.setdefault(entry["name"], []).append((entry["x"], entry["y"], entry["z"]))
        return markers

    def tables_agree(expected: list[str]) -> bool:
        """The same markers of the player on the WA and on every node"""
        tables = [
            sorted(
                e["name"]
                for e in cluster.node(process, f"/gps-test-table?prefix={PREFIX}")["entries"]
                if e["identity"] == identity
            )
            for process in PROCESSES
        ]
        return all(table == sorted(expected) for table in tables)

    def settle(expected: dict[str, tuple]) -> tuple[dict, bool]:
        """The markers on the client and in the tables once they show what is expected, or after 30 s"""
        deadline = time.monotonic() + 30
        while True:
            markers = client_markers()
            agree = tables_agree(list(expected))
            matches = set(markers) == set(expected) and all(
                len(markers[name]) == 1 and math.dist(markers[name][0], position) < 1
                for name, position in expected.items()
            )
            if (matches and agree) or time.monotonic() > deadline:
                return markers, matches and agree
            time.sleep(1)

    _near, near_position = spawn(home, NEAR, 200)
    far_grid, far_position = spawn(other, FAR, 50_000)

    reply = grids_list()
    bench.check(
        f"the client on {home} lists its grids on both nodes",
        NEAR in reply and FAR in reply,
        reply,
    )
    expected = {NEAR: near_position, FAR: far_position}
    markers, ok = settle(expected)
    bench.check("one marker per grid at its position, on the client and every node", ok, str(markers))

    partition = _client_partition(_partitions(bench, home))
    bench.record("handover", cluster.post(_handover_url(bench, partition, source=home, target=other), timeout=300))
    wait_until(lambda: client_node() == other, 120, f"the client on {other}")
    markers, ok = settle(expected)
    bench.check(f"the markers followed the client to {other}", ok, str(markers))

    reply = grids_list()
    markers, ok = settle(expected)
    bench.check("listing again from the other node adds nothing", ok and FAR in reply, str(markers))

    # The far grid is gone and another of that name stands elsewhere, now on the first node
    cluster.node(other, f"/contract-test-delete-grid?grid={far_grid}", "POST")
    _moved, moved_position = spawn(home, FAR, 60_000)
    grids_list()
    expected[FAR] = moved_position
    markers, ok = settle(expected)
    bench.check("the marker of a moved grid moves with it", ok, str(markers))


run.regular_nodes = 2


def main() -> int:
    ROOT.mkdir(parents=True, exist_ok=True)
    build()
    os.environ["CLUSTER_EXTRA_PLUGINS"] = str(PLUGINS / "Essentials")
    label = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%SZ-essentials-grids")
    results = bench_module.HOST.results / label
    results.mkdir(parents=True, exist_ok=True)
    outcome = bench_module.run_scenario(
        run, "P2-ESS-GRIDS", "SE-810: !ess grids list and its GPS markers on a cluster", "3", results
    )
    print(json.dumps({"passed": outcome.passed, "error": outcome.error, "results": str(results)}))
    print(f"{sum(c.passed for c in outcome.checks)}/{len(outcome.checks)} checks passed")
    for item in outcome.checks:
        if not item.passed:
            print("FAILED: " + item.label + (f" - {item.detail}" if item.detail else ""))
    print("PASS" if outcome.passed else "FAIL")
    return 0 if outcome.passed else 1


if __name__ == "__main__":
    sys.exit(main())
