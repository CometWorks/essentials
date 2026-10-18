"""Test grids, written as prefab files the probe spawns (`!eprobe spawn`).

A grid is an armor slab with functional blocks on top of it. Every grid and
block gets a fresh entity id, so a test can spawn the same layout again and
refer to its blocks by id.
"""

from __future__ import annotations

import itertools
import random

import world
from world import PLAYER_A, block

# Functional blocks: kind -> (object builder type, subtype, extra XML)
KINDS = {
    "reactor": ("Reactor", "LargeBlockSmallGenerator", "<Enabled>true</Enabled>"),
    "battery": (
        "BatteryBlock",
        "LargeBlockBatteryBlock",
        "<Enabled>true</Enabled><CurrentStoredPower>3</CurrentStoredPower>"
        "<ProducerEnabled>true</ProducerEnabled>",
    ),
    "seat": ("Cockpit", "LargeBlockCockpitSeat", ""),
    "cargo": ("CargoContainer", "LargeBlockSmallContainer", ""),
    "assembler": ("Assembler", "LargeAssembler", "<Enabled>true</Enabled>"),
    # Turrets never aim at characters, so a test can't get a player shot
    "gatling": (
        "LargeGatlingTurret",
        "",
        "<Enabled>false</Enabled><TargetCharacters>false</TargetCharacters>"
        "<TargetMeteors>false</TargetMeteors><TargetSmallGrids>false</TargetSmallGrids>"
        "<TargetLargeGrids>false</TargetLargeGrids><TargetMissiles>false</TargetMissiles>",
    ),
    "launcher": (
        "SmallMissileLauncher",
        "LargeMissileLauncher",
        "<Enabled>true</Enabled>",
    ),
    "projector": ("Projector", "LargeProjector", "<Enabled>true</Enabled>"),
    "store": ("StoreBlock", "StoreBlock", ""),
    "stator": ("MotorStator", "LargeStator", "<Enabled>true</Enabled>"),
    "small_reactor": ("Reactor", "SmallBlockSmallGenerator", "<Enabled>true</Enabled>"),
}

# Entity ids: a random base per run, so a leftover from an earlier run never
# collides
_ids = itertools.count(random.randrange(10**6, 10**7) * 10**6, 1000)


class Grid:
    """A grid to spawn. Functional blocks belong to `owner` unless said
    otherwise; the armor is built by `owner` too."""

    def __init__(
        self,
        name: str,
        position,
        *,
        static: bool = True,
        size: str = "Large",
        owner: int = PLAYER_A,
        slab=(5, 1, 5),
    ):
        self.name = name
        self.position = position
        self.static = static
        self.size = size
        self.owner = owner
        self.entity_id = next(_ids)
        self._next_block = self.entity_id + 1
        self.blocks: dict[str, int] = {}
        self._xml = [
            block("CubeBlock", world.ARMOR[size], c, built_by=owner)
            for c in itertools.product(*(range(n) for n in slab))
        ]
        self.slab = slab

    @property
    def cell_m(self) -> float:
        return world.GRID_SIZE_M[self.size]

    def add(
        self,
        kind: str,
        cell,
        *,
        name: str | None = None,
        owner: int | None = None,
        share: str = "None",
        extra: str = "",
        forward: str = "Forward",
    ) -> int:
        """Adds a functional block and returns its entity id. The block is
        known by `name` (default: its kind) in self.blocks."""
        xsi_type, subtype, default = KINDS[kind]
        entity_id = self._next_block
        self._next_block += 1
        owner = self.owner if owner is None else owner
        self._xml.append(
            block(
                xsi_type,
                subtype,
                cell,
                built_by=owner,
                entity_id=entity_id,
                forward=forward,
                extra=default + f"<ShareMode>{share}</ShareMode>" + extra,
            )
        )
        self.blocks[name or kind] = entity_id
        return entity_id

    def world_of(self, cell):
        """World position of a cell's centre (the grid is not rotated)"""
        return tuple(p + c * self.cell_m for p, c in zip(self.position, cell))

    def top(self, dy: float = 0.0):
        """A point above the middle of the slab"""
        mid = [(n - 1) / 2 for n in self.slab]
        x, y, z = self.world_of(mid)
        return (x, y + self.cell_m * self.slab[1] + dy, z)

    def xml(self) -> str:
        return (
            "<CubeGrid>"
            f"<SubtypeName /><EntityId>{self.entity_id}</EntityId>"
            "<PersistentFlags>CastShadows InScene</PersistentFlags>"
            "<PositionAndOrientation>"
            + world.vec("Position", self.position)
            + world.vec("Forward", (0, 0, -1))
            + world.vec("Up", (0, 1, 0))
            + "</PositionAndOrientation>"
            f"<GridSizeEnum>{self.size}</GridSizeEnum>"
            f"<CubeBlocks>{''.join(self._xml)}</CubeBlocks>"
            f"<IsStatic>{str(self.static).lower()}</IsStatic>"
            f"<IsUnsupportedStation>{str(self.static).lower()}</IsUnsupportedStation>"
            f"<DisplayName>{self.name}</DisplayName>"
            "<DestructibleBlocks>true</DestructibleBlocks>"
            "<Editable>true</Editable>"
            "</CubeGrid>"
        )


def prefab_file(grids) -> str:
    return (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<Definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        'xmlns:xsd="http://www.w3.org/2001/XMLSchema"><Prefabs><Prefab>'
        "<Id><TypeId>MyObjectBuilder_PrefabDefinition</TypeId><SubtypeId>Test</SubtypeId></Id>"
        f"<CubeGrids>{''.join(g.xml() for g in grids)}</CubeGrids>"
        "</Prefab></Prefabs></Definitions>\n"
    )
