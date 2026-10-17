"""Every chat command of the plugin with its permission level, read from the
plugin's source, so the permission tests follow the code."""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

SOURCE = Path(__file__).resolve().parent.parent / "ServerPlugin"
LEVELS = ["None", "Scripter", "Moderator", "SpaceMaster", "Admin", "Owner"]

_ROOT = re.compile(r'\[CommandRoot\("([^"]+)"')
_COMMAND = re.compile(r'\[Command\("([^"]*)"')
_PERMISSION = re.compile(r"\[Permission\(MyPromoteLevel\.(\w+)\)\]")
_METHOD = re.compile(r"^\s*public\s")
# csharpier puts the arguments of a long attribute on the lines after it
_WRAPPED = re.compile(r"\[(\w+)\(\s*\n\s*")


@dataclass(frozen=True)
class Command:
    root: str
    path: str
    level: str

    @property
    def line(self) -> str:
        return f"!{self.root} {self.path}".strip()

    def allowed(self, level: str) -> bool:
        return LEVELS.index(level) >= LEVELS.index(self.level)


def _scan(text: str) -> list[Command]:
    commands = []
    root = "ess"
    pending = None
    level = None
    for line in _WRAPPED.sub(r"[\1(", text).splitlines():
        if m := _ROOT.search(line):
            root = m.group(1)
        if m := _COMMAND.search(line):
            pending, level = m.group(1), None
        if m := _PERMISSION.search(line):
            level = m.group(1)
        if pending is not None and _METHOD.match(line):
            # Commands without a Permission attribute need Admin
            commands.append(Command(root, pending, level or "Admin"))
            pending = None
    return commands


def all_commands() -> list[Command]:
    found, declared = [], 0
    for path in sorted(SOURCE.rglob("*.cs")):
        text = path.read_text(encoding="utf-8-sig")
        found.extend(_scan(text))
        declared += text.count("[Command(")
    # A layout the scan misses would drop commands from the tests silently
    assert len(found) == declared, f"read {len(found)} of {declared} commands"
    return sorted(set(found), key=lambda c: c.line)
