"""Audit Old SGOBattleSys battle.cfg SP variation values without unlocking combat.

Old SGOBattleSys::LoadCfg (SGOBattleSys.dll.lst, 0x10003722-0x10003812,
0x10003A85-0x10003B15) loads the SP buckets and race deltas, scaling
them by current/default server FPS. The original WillDamage_ForSP and
HPLostChange_ForSP consume RaceDelta; this is not evidence of player
attack cooldown. No authenticated profile/equipment cadence is verified.
"""

from __future__ import annotations

from dataclasses import dataclass
from hashlib import sha256
from pathlib import Path
import re

MAX_SOURCE_BYTES = 64 * 1024
MAX_BUCKETS = 64
RACE_COUNT = 4  # Old LoadCfg [Core] RaceCount fallback; audit these four keys only.


@dataclass(frozen=True)
class OldBattleTiming:
    multiple: int
    sp: tuple[int, ...]
    race_delta: tuple[int, ...]

    def scaled_for_fps(self, current_fps: int, default_fps: int) -> dict:
        """Reproduce the observed LoadCfg integer scaling, not combat cadence."""
        if not 1 <= current_fps <= 240 or not 1 <= default_fps <= 240:
            raise ValueError("Server FPS is outside audited bounds")
        # The race calculation uses a signed 16-bit intermediate in the Old
        # executable. Reject overflow rather than reproducing silent wrap.
        if any(value * current_fps > 32767 for value in self.race_delta):
            raise ValueError("RaceDelta would overflow Old signed 16-bit multiply")
        sp = tuple(value * current_fps // default_fps for value in self.sp)
        race = tuple(value * current_fps // default_fps for value in self.race_delta)
        if any(value < 1 or value > 255 for value in sp):
            raise ValueError("Old SP byte conversion would truncate the result")
        if any(value < 1 or value > 32767 for value in race):
            raise ValueError("Old RaceDelta conversion would be invalid")
        return {
            "current_fps": current_fps,
            "default_fps": default_fps,
            "sp_loaded_bytes": list(sp),
            "race_delta_loaded_words": list(race),
            "combat_cooldown_verified": False,
        }


def parse_old_battle_cfg(payload: bytes) -> OldBattleTiming:
    if not payload or len(payload) > MAX_SOURCE_BYTES:
        raise ValueError("battle.cfg source size is invalid")
    try:
        content = payload.decode("ascii")
    except UnicodeDecodeError as error:
        raise ValueError("battle.cfg must use the audited ASCII keys") from error

    sections: dict[str, dict[str, str]] = {}
    current: dict[str, str] | None = None
    for line_number, original in enumerate(content.splitlines(), 1):
        line = original.split("//", 1)[0].split(";", 1)[0].strip()
        if not line:
            continue
        match = re.fullmatch(r"\[([A-Za-z0-9_]+)\]", line)
        if match:
            name = match.group(1).casefold()
            if name in sections:
                raise ValueError(f"Duplicate section on line {line_number}")
            current = sections.setdefault(name, {})
            continue
        if current is None or "=" not in line:
            raise ValueError(f"Invalid battle.cfg line {line_number}")
        key, value = (part.strip() for part in line.split("=", 1))
        if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", key):
            raise ValueError(f"Invalid key on line {line_number}")
        folded = key.casefold()
        if folded in current:
            raise ValueError(f"Duplicate key on line {line_number}")
        current[folded] = value

    speed = sections.get("sp")
    if speed is None:
        raise ValueError("battle.cfg is missing the SP section")

    def integer(key: str, minimum: int, maximum: int) -> int:
        raw = speed.get(key.casefold())
        if raw is None or not re.fullmatch(r"[0-9]+", raw):
            raise ValueError(f"SP.{key} is missing or invalid")
        number = int(raw, 10)
        if not minimum <= number <= maximum:
            raise ValueError(f"SP.{key} is outside source bounds")
        return number

    count = integer("Count", 1, MAX_BUCKETS)
    multiple = integer("Multiple", 1, 255)
    keys = {f"sp{index:02d}" for index in range(count)}
    races = {f"racedelta{index:02d}" for index in range(RACE_COUNT)}
    if {key for key in speed if re.fullmatch(r"sp[0-9]+", key)} != keys:
        raise ValueError("SP bucket count disagrees with source keys")
    if {key for key in speed if re.fullmatch(r"racedelta[0-9]+", key)} != races:
        raise ValueError("RaceDelta count disagrees with audited four-race source")
    buckets = tuple(integer(f"SP{index:02d}", 1, 255) for index in range(count))
    delta = tuple(integer(f"RaceDelta{index:02d}", 1, 32767)
                  for index in range(RACE_COUNT))
    return OldBattleTiming(multiple=multiple, sp=buckets, race_delta=delta)


def read_old_battle_cfg(path: Path) -> tuple[OldBattleTiming, dict]:
    if not path.is_file() or path.is_symlink():
        raise ValueError("Old battle.cfg source must be a regular file")
    data = path.read_bytes()
    timing = parse_old_battle_cfg(data)
    return timing, {
        "source_sha256": sha256(data).hexdigest(),
        "source_bytes": len(data),
        "sp_count": len(timing.sp),
        "race_count": len(timing.race_delta),
        "old_loader_anchor": "SGOBattleSys::LoadCfg 0x10003722..0x10003B15",
    }
