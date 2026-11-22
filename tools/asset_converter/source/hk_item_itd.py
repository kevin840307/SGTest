"""Parse HK SGO Item/item.itd using record boundaries recovered from SGOClient.

This module is evidence extraction only. HK item IDs are version-local and must
never replace SGTest runtime IDs without an explicit cross-version mapping.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
import hashlib
from pathlib import Path
import struct

from asset_converter.source.runtime_spr import unwrap_cde

MAX_FILE = 32 * 1024 * 1024
MAX_RECORDS = 20000

# CONFIRMED from SGOClient.exe.lst sub_4054AF Size decrements. Values include
# the leading type byte consumed before each branch.
RECORD_LENGTH = {
    0: 0x446,
    1: 0x4B1,
    2: 0x480,
    3: 0x47E,
    4: 0x480,
    6: 0x14A,
}
# CONFIRMED from branch copy order + HK item_extra.csv unique-name cross-check
# for types 0-4. Type 6 uses the same observed 0x08 u16 layout as 2/3/4 but
# remains INFERRED because item_extra.csv has no unique type-6 ID cross-check.
NAME_OFFSET = {0:0x32, 1:0x39, 2:0x1C, 3:0x1A, 4:0x1C, 6:0x0E}
TOOLTIP_OFFSET = {0:0x46, 1:0x4D, 2:0x80, 3:0x7E, 4:0x80, 6:0x36}
TOOLTIP_BYTES = {0:0x400, 1:0x400, 2:0x400, 3:0x400, 4:0x400, 6:0x100}
ID_OFFSET = {0:0x0C, 1:0x0F, 2:0x08, 3:0x08, 4:0x08, 6:0x08}

# CONFIRMED from sub_4054AF loader copy order and sub_473E5B equipment
# comparison formatter. These are record-relative offsets in item.itd.
COMPARE_STAT_OFFSET = {
    0: {"attack":0x0E, "defense":0x10, "magic_attack":0x12,
        "magic_defense":0x14, "hit_or_block":0x18, "evasion":0x1A,
        "luck":0x1C},
    1: {"attack":0x11, "defense":0x13, "magic_attack":0x15,
        "magic_defense":0x17, "hit_or_block":0x1B, "evasion":0x1D,
        "luck":0x1F},
}
# +0x2E is the equipment category used by sub_473E5B; type 0 category 6
# labels the +0x3C comparison value as 阻擋率, otherwise it is 命中率.
EQUIP_CATEGORY_OFFSET = {0:0x07, 1:0x07}
# sub_473E5B formats definition+0x50 as the denominator of
# "損耗值：%d／%d". Loader mapping makes these the source-record offsets.
MAX_LOSS_OFFSET = {0:0x2E, 1:0x35}


@dataclass(frozen=True)
class HKItemRecord:
    offset: int
    item_type: int
    hk_item_id: int
    name: str
    tooltip: str
    name_valid: bool
    tooltip_valid: bool
    tooltip_color_tokens: int
    tooltip_loss_insert: int
    equipment_category: int | None
    max_loss: int | None
    attack: int | None
    defense: int | None
    magic_attack: int | None
    magic_defense: int | None
    hit_or_block: int | None
    evasion: int | None
    luck: int | None


def _fixed_cp950(record: bytes, offset: int, size: int) -> tuple[str,bool]:
    field = record[offset:offset+size]
    if len(field) != size:
        return "",False
    field = field.split(b"\0",1)[0]
    if not field:
        return "",True
    try:
        text = field.decode("cp950")
    except UnicodeDecodeError:
        return "",False
    text = text.replace("\r\n","\n").replace("\r","\n").strip()
    if len(text) > size or any(ord(c) == 127 or (ord(c) < 32 and c not in "\n\t")
                               for c in text):
        return "",False
    return text,True


def _tooltip_cp950(record: bytes, item_type: int) -> tuple[str,bool,int,int]:
    """Decode legacy tooltip text for plain UI presentation.

    Original client helpers emit five-byte inline color controls: 0x7f plus
    RGBA. They are byte protocol, not CP950. For equipment (types 0/1),
    sub_473E5B also interprets literal backslash-e as a highlighted first-line
    fragment and literal backslash-n inside the head as line breaks.
    """
    offset = TOOLTIP_OFFSET[item_type]
    size = TOOLTIP_BYTES[item_type]
    field = record[offset:offset+size]
    if len(field) != size:
        return "",False,0,-1
    field = field.split(b"\0",1)[0]
    clean = bytearray()
    color_tokens = 0
    cursor = 0
    while cursor < len(field):
        if field[cursor] == 0x7F:
            if cursor + 5 > len(field):
                return "",False,color_tokens,-1
            color_tokens += 1
            cursor += 5
            continue
        clean.append(field[cursor])
        cursor += 1
    try:
        text = bytes(clean).decode("cp950")
    except UnicodeDecodeError:
        return "",False,color_tokens,-1
    text = text.replace("\r\n","\n").replace("\r","\n")
    if any(ord(c) == 127 or (ord(c) < 32 and c not in "\n\t") for c in text):
        return "",False,color_tokens,-1

    loss_insert = -1
    if item_type in (0,1) and text:
        first_newline = text.find("\n")
        if first_newline >= 0:
            marker = text.find("\\e")
            if 0 <= marker < first_newline:
                head = text[:marker].replace("\\n","\n")
                highlight = text[marker+2:first_newline].replace("\\n","\n")
            else:
                head = text[:first_newline].replace("\\n","\n")
                highlight = ""
            body = text[first_newline+1:]
            # Original order: head, runtime loss line, body, highlighted text.
            text = head
            loss_insert = len(text)
            if body:
                text += "\n" + body
            if highlight:
                text += "\n" + highlight + "\n"
        else:
            text = text.replace("\\n","\n").replace("\\e","")
            loss_insert = len(text)
    leading_trim = len(text) - len(text.lstrip())
    text = text.strip()
    if loss_insert >= 0:
        loss_insert = max(0,loss_insert-leading_trim)
        if loss_insert > len(text):
            loss_insert = len(text)
    return text,True,color_tokens,loss_insert


def parse_hk_item_itd_plain(plain: bytes) -> list[HKItemRecord]:
    if not 1 <= len(plain) <= MAX_FILE:
        raise ValueError("HK item.itd plain payload size is invalid")
    records: list[HKItemRecord] = []
    offset = 0
    while offset < len(plain):
        item_type = plain[offset]
        length = RECORD_LENGTH.get(item_type)
        if length is None:
            raise ValueError(f"HK item.itd unknown record type {item_type} at {offset:#x}")
        end = offset + length
        if end > len(plain):
            raise ValueError(f"HK item.itd truncated type {item_type} record at {offset:#x}")
        record = plain[offset:end]
        name,name_valid = _fixed_cp950(record,NAME_OFFSET[item_type],20)
        tooltip,tooltip_valid,color_tokens,loss_insert = _tooltip_cp950(
            record,item_type)
        hk_item_id = struct.unpack_from("<H",record,ID_OFFSET[item_type])[0]

        equipment_category = max_loss = None
        stats: dict[str,int] = {}
        if item_type in COMPARE_STAT_OFFSET:
            equipment_category = record[EQUIP_CATEGORY_OFFSET[item_type]]
            max_loss = struct.unpack_from(
                "<I",record,MAX_LOSS_OFFSET[item_type])[0]
            stats = {
                key:struct.unpack_from("<H",record,at)[0]
                for key,at in COMPARE_STAT_OFFSET[item_type].items()
            }
        records.append(HKItemRecord(
            offset,item_type,hk_item_id,name,tooltip,name_valid,tooltip_valid,
            color_tokens,loss_insert,equipment_category,max_loss,
            stats.get("attack"),stats.get("defense"),
            stats.get("magic_attack"),stats.get("magic_defense"),
            stats.get("hit_or_block"),stats.get("evasion"),stats.get("luck")))
        if len(records) > MAX_RECORDS:
            raise ValueError("HK item.itd exceeds bounded record count")
        offset = end
    if offset != len(plain):
        raise ValueError("HK item.itd parser did not consume the entire payload")
    return records


def read_hk_item_itd(path: Path) -> tuple[list[HKItemRecord],dict]:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError("HK item.itd source is missing or symbolic")
    blob = source.read_bytes()
    if not 1 <= len(blob) <= MAX_FILE:
        raise ValueError("HK item.itd source size is invalid")
    plain = unwrap_cde(blob) if blob.startswith(b"C.D.E.") else blob
    records = parse_hk_item_itd_plain(plain)
    types = Counter(x.item_type for x in records)
    return records,{
        "record_count":len(records),
        "type_counts":{str(k):types[k] for k in sorted(types)},
        "invalid_name_records":sum(not x.name_valid for x in records),
        "invalid_tooltip_records":sum(not x.tooltip_valid for x in records),
        "tooltip_color_tokens":sum(x.tooltip_color_tokens for x in records),
        "tooltip_loss_insert_records":sum(x.tooltip_loss_insert >= 0 for x in records),
        "structured_equipment_records":sum(
            x.item_type in COMPARE_STAT_OFFSET for x in records),
        "source_sha256":hashlib.sha256(blob).hexdigest(),
        "plain_sha256":hashlib.sha256(plain).hexdigest(),
        "plain_bytes":len(plain),
        "record_boundaries":"confirmed_full_consumption",
        "type6_id_offset":"inferred",
    }
