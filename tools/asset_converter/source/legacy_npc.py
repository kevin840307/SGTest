"""Bounded readers for original NPC placement and six-slot visual tables.

Recovered 0662e491, Infrastructure/GameData/NpcDatReader.cs and
LegacyNpcVisualCatalog.cs provide the layout reference. Original client data
can be C.D.E.-wrapped; decoding uses the same implementation as SPR conversion.
"""
import struct

from .runtime_spr import unwrap_cde


def plain(raw: bytes) -> bytes:
    return unwrap_cde(raw) if raw.startswith(b'C.D.E.') else raw


def _header(raw):
    if len(raw) < 4:
        raise ValueError('short NPC table header')
    count, = struct.unpack_from('<i', raw)
    if not 0 <= count <= 10000:
        raise ValueError('invalid NPC record count')
    return count, 4


def _string(raw, at, limit=1024):
    end = raw.find(b'\0', at, min(len(raw), at+limit+1))
    if end < 0:
        raise ValueError('NPC string missing terminator or over limit')
    return raw[at:end], end+1


def npc_placements(raw: bytes) -> list[dict]:
    raw = plain(raw)
    count, at = _header(raw)
    result = []
    for index in range(count):
        if at+13 > len(raw):
            raise ValueError('truncated NPC placement')
        type_id, x, y, reserved = struct.unpack_from('<iiiB', raw, at)
        at += 13
        if type_id < 0:
            raise ValueError('negative NPC type')
        script, at = _string(raw, at, 255)
        name, at = _string(raw, at, 255)
        try:
            script_text, name_text = script.decode('ascii'), name.decode('cp950')
        except UnicodeDecodeError as error:
            raise ValueError('invalid NPC script/name encoding') from error
        result.append({'source_index': index, 'type_id': type_id, 'p': [x, y],
                       'reserved': reserved, 'script': script_text,
                       'name': name_text, 'name_bytes_hex': name.hex()})
    if at != len(raw):
        raise ValueError('trailing NPC placement bytes')
    return result


def npc_visual_table(raw: bytes) -> list[list[str]]:
    raw = plain(raw)
    count, at = _header(raw)
    rows = []
    for _ in range(count):
        fields = []
        for _ in range(6):
            value, at = _string(raw, at)
            try:
                fields.append(value.decode('cp950'))
            except UnicodeDecodeError as error:
                raise ValueError('invalid NPC visual path encoding') from error
        rows.append(fields)
    if at != len(raw):
        raise ValueError('trailing NPC visual table bytes')
    return rows
