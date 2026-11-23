"""Parse HK SGO Item/petcli.ITD using the client loader's exact byte order.

The format is source evidence only. HK PetID values are version-local and must
not silently replace SGTest DefinitionId values.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path
import re
import struct

from asset_converter.source.runtime_spr import unwrap_cde

MAX_FILE = 8 * 1024 * 1024
MAX_FAMILIES = 255
MAX_EVOLUTIONS = 255
_OUTLOOK = re.compile(r"^Enemy\\[A-Za-z0-9_-]+$")


@dataclass(frozen=True)
class HKPetEvolution:
    source_pet_id: int
    evolution_id: int
    unknown_u16_a: int
    unknown_u16_b: int
    evolution_name: str
    outlook: str
    variant_byte: int
    description: str


@dataclass(frozen=True)
class HKPetFamily:
    source_pet_id: int
    name: str
    evolutions: tuple[HKPetEvolution, ...]


def _cstring_cp950(payload: bytes, cursor: int, max_bytes: int, label: str) -> tuple[str, int]:
    if cursor < 0 or cursor >= len(payload):
        raise ValueError(f"HK petcli.ITD truncated before {label}")
    limit = min(len(payload), cursor + max_bytes + 1)
    end = payload.find(b"\0", cursor, limit)
    if end < 0:
        raise ValueError(f"HK petcli.ITD unterminated/oversized {label}")
    raw = payload[cursor:end]
    try:
        value = raw.decode("cp950")
    except UnicodeDecodeError as exc:
        raise ValueError(f"HK petcli.ITD invalid CP950 {label}") from exc
    if any(ord(ch) == 127 or (ord(ch) < 32 and ch not in "\n\t") for ch in value):
        raise ValueError(f"HK petcli.ITD control character in {label}")
    return value, end + 1


def _need(payload: bytes, cursor: int, count: int, label: str) -> None:
    if count < 0 or cursor < 0 or cursor + count > len(payload):
        raise ValueError(f"HK petcli.ITD truncated {label}")


def parse_hk_petcli_plain(plain: bytes) -> tuple[list[HKPetFamily], int]:
    """Parse an already-unwrapped petcli.ITD payload.

    CONFIRMED from SGOClient.exe.lst sub_403B49:
    byte family_count, one ignored header byte, then for every family
    u16 PetID + C string name + byte evolution_count. Every evolution is
    byte EvolID + u16 + u16 + C string + C string outlook + byte + C string.
    The stack destination sizes bound the strings to 99/99/254/511 bytes.
    """
    if not 2 <= len(plain) <= MAX_FILE:
        raise ValueError("HK petcli.ITD plain payload size is invalid")
    family_count = plain[0]
    header_byte = plain[1]
    if not 1 <= family_count <= MAX_FAMILIES:
        raise ValueError("HK petcli.ITD family count is invalid")

    cursor = 2
    families: list[HKPetFamily] = []
    seen_pet_ids: set[int] = set()
    for family_index in range(family_count):
        _need(plain, cursor, 2, f"family {family_index} PetID")
        source_pet_id = struct.unpack_from("<H", plain, cursor)[0]
        cursor += 2
        if source_pet_id == 0 or source_pet_id in seen_pet_ids:
            raise ValueError("HK petcli.ITD invalid/duplicate PetID")
        seen_pet_ids.add(source_pet_id)

        family_name, cursor = _cstring_cp950(
            plain, cursor, 99, f"family {source_pet_id} name")
        _need(plain, cursor, 1, f"family {source_pet_id} evolution count")
        evolution_count = plain[cursor]
        cursor += 1
        if not 1 <= evolution_count <= MAX_EVOLUTIONS:
            raise ValueError("HK petcli.ITD evolution count is invalid")

        evolutions: list[HKPetEvolution] = []
        seen_evolutions: set[int] = set()
        for _ in range(evolution_count):
            _need(plain, cursor, 5, f"family {source_pet_id} evolution header")
            evolution_id = plain[cursor]
            unknown_u16_a = struct.unpack_from("<H", plain, cursor + 1)[0]
            unknown_u16_b = struct.unpack_from("<H", plain, cursor + 3)[0]
            cursor += 5
            if evolution_id in seen_evolutions:
                raise ValueError("HK petcli.ITD duplicate EvolID")
            seen_evolutions.add(evolution_id)

            evolution_name, cursor = _cstring_cp950(
                plain, cursor, 99,
                f"family {source_pet_id} evolution {evolution_id} name")
            outlook, cursor = _cstring_cp950(
                plain, cursor, 254,
                f"family {source_pet_id} evolution {evolution_id} outlook")
            if not _OUTLOOK.fullmatch(outlook):
                raise ValueError(
                    f"HK petcli.ITD invalid Enemy outlook for {source_pet_id}:{evolution_id}")
            _need(plain, cursor, 1, f"family {source_pet_id} evolution variant")
            variant_byte = plain[cursor]
            cursor += 1
            description, cursor = _cstring_cp950(
                plain, cursor, 511,
                f"family {source_pet_id} evolution {evolution_id} description")
            evolutions.append(HKPetEvolution(
                source_pet_id, evolution_id, unknown_u16_a, unknown_u16_b,
                evolution_name, outlook, variant_byte, description))

        families.append(HKPetFamily(
            source_pet_id, family_name, tuple(evolutions)))

    if cursor != len(plain):
        raise ValueError(
            f"HK petcli.ITD parser did not consume payload: {cursor}/{len(plain)}")
    return families, header_byte


def read_hk_petcli(path: Path) -> tuple[list[HKPetFamily], dict]:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError("HK petcli.ITD source is missing or symbolic")
    blob = source.read_bytes()
    if not 2 <= len(blob) <= MAX_FILE:
        raise ValueError("HK petcli.ITD source size is invalid")
    plain = unwrap_cde(blob) if blob.startswith(b"C.D.E.") else blob
    families, header_byte = parse_hk_petcli_plain(plain)
    evolution_count = sum(len(row.evolutions) for row in families)
    sprites = {
        evolution.outlook.split("\\", 1)[1].upper()
        for family in families for evolution in family.evolutions
    }
    return families, {
        "family_count": len(families),
        "evolution_count": evolution_count,
        "unique_sprite_count": len(sprites),
        "header_byte": header_byte,
        "source_sha256": hashlib.sha256(blob).hexdigest(),
        "plain_sha256": hashlib.sha256(plain).hexdigest(),
        "plain_bytes": len(plain),
        "record_boundaries": "confirmed_full_consumption",
        "loader_evidence": "SGOClient.exe.lst sub_403B49",
        "unknown_u16_semantics": True,
        "variant_byte_semantics_verified": False,
    }
