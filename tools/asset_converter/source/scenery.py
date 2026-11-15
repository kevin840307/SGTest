"""Original SIF/BLG presentation records; independent of images and Godot.

DIRECT Paradise.exe: CELBldg2D ctor 0x005485BF, CELStaticObj2D ctor
0x0054A172, CELBldg2D::CheckBldgEntrance 0x00549950. No source text copied.
"""
import struct


class Reader:
    def __init__(self, data):
        self.data, self.at = data, 0

    def take(self, size):
        if size < 0 or size > len(self.data)-self.at:
            raise ValueError('truncated scenery record')
        result = self.data[self.at:self.at+size]
        self.at += size
        return result

    def name(self):
        size = self.take(1)[0]
        if size > 127:
            raise ValueError('scenery string exceeds original signed-byte length')
        try:
            value = self.take(size).decode('cp950')
        except UnicodeDecodeError as error:
            raise ValueError('invalid scenery string encoding') from error
        if '\0' in value:
            raise ValueError('embedded NUL in scenery string')
        return value

    def finish(self):
        if self.at != len(self.data):
            raise ValueError('unexpected scenery trailer')


def parse_sif_contract(raw):
    """SIF name + optional mode byte + center XY, width, length.

    The file-based constructors default mode=1 for the older 16-byte tail.
    This semantic adapter does not change archived raw SIF normalization.
    """
    reader = Reader(raw)
    name = reader.name()
    if not name.lower().endswith('.spr'):
        raise ValueError('scenery SIF must reference a SPR')
    remaining = len(raw)-reader.at
    if remaining not in (16,17):
        raise ValueError('invalid scenery SIF record size')
    present = remaining == 17
    mode = reader.take(1)[0] if present else 1
    cx, cy, width, length = struct.unpack('<4i',reader.take(16))
    if width <= 0 or length <= 0:
        raise ValueError('invalid scenery SIF extent')
    reader.finish()
    return {'spr_name':name, 'mode':mode, 'mode_source_present':present,
            'center':[cx,cy], 'size':[width,length]}


def parse_blg(raw):
    reader = Reader(raw)
    # Authoring absolute paths are not runtime dependencies or exported paths.
    reader.name()
    primary = reader.name()
    reader.name()
    alternate = reader.name()
    front = list(struct.unpack('<4i',reader.take(16)))
    groups = []
    for _ in range(2):
        count = struct.unpack('<I',reader.take(4))[0]
        # Bound by the actual bytes before allocating a Python list.
        if count > (len(raw)-reader.at)//8:
            raise ValueError('BLG point count outside remaining bytes')
        groups.append([list(pair) for pair in struct.iter_unpack('<2i',reader.take(count*8))])
    reader.finish()
    if not primary:
        raise ValueError('BLG empty primary appearance is not yet supported')
    # The file-based ctor clamps positive endpoint Y to -10 before sorting.
    effective = front.copy()
    for index in (1,3):
        if effective[index] > 0:
            effective[index] = -10
    return {'primary':primary, 'alternate':alternate, 'front_raw':front,
            'front':effective, 'inside_cells':groups[0], 'outside_cells':groups[1]}
