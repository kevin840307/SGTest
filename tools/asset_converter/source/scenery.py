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


def point_in_front(position, front, point):
    """DIRECT CheckPointInFront 0x00549560; equality stays behind."""
    x, y = point[0]-position[0], point[1]-position[1]
    left, ly, right, ry = front
    if x <= 0 and x <= left:
        return y > ly
    if x > 0 and x >= right:
        return y > ry
    return 2*(ly-y)-(left-x) < 0 or 2*(ry-y)+(right-x) < 0


def swap_required(first, second):
    """Original boolean swap predicate, NOT a strict ordering comparator.

    Inputs are presentation records with p, sorting_mode, front (buildings).
    DIRECT SortMapObject 0x00529CAA; CheckBldgInFront 0x005497EE.
    """
    first_building = first['sorting_mode'] == 0
    second_building = second['sorting_mode'] == 0
    if first_building and second_building:
        # Pick the other building's edge closest to this building's X.
        offset = 2 if second['p'][0] < first['p'][0] else 0
        x = second['p'][0]+second['front'][offset]
        y = second['p'][1]+second['front'][offset+1]
        threshold = first['front'][1 if x <= first['p'][0] else 3]+first['p'][1]
        return y < threshold
    if first_building:
        return not point_in_front(first['p'],first['front'],second['p'])
    if second_building:
        return point_in_front(second['p'],second['front'],first['p'])
    return second['p'][1] < first['p'][1]


def compile_scenery_order(placements, definitions):
    """Bake legacy root ordering once, outside the Client's frame loop.

    DIRECT CELList::BubbleSort 0x0052AB10–0x0052ABED: adjacent swaps,
    stop when unchanged or after 1025 full passes. Children are not roots.
    """
    groups, records, passes, settled = [[],[],[]], {}, [], []
    for index, placement in enumerate(placements):
        if placement['parent_index'] is not None:
            continue
        definition = definitions[placement['definition']]
        records[index] = {'p':placement['p'],'sorting_mode':definition['sorting_mode'],
                          'front':definition.get('front')}
        groups[definition['draw_group']].append(index)
    for group in groups:
        changed = False
        for pass_index in range(1025):
            changed = False
            for at in range(len(group)-1):
                if swap_required(records[group[at]],records[group[at+1]]):
                    group[at],group[at+1] = group[at+1],group[at]
                    changed = True
            if not changed:
                break
        passes.append(pass_index+1)
        settled.append(not changed)
    return {'version':1,'groups':groups,'passes':passes,'settled':settled}
