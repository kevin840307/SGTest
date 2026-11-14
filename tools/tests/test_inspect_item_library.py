import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inspect_item_library import PAYLOAD_SIZES, PICTURE_OFFSETS, extract_definitions, scan_records


class ItemLibraryTests(unittest.TestCase):
    def test_each_type_uses_its_own_picture_offset(self):
        raw = bytearray()
        for tag in (0, 1, 2, 3, 4, 6):
            record = bytearray([tag]) + bytearray(PAYLOAD_SIZES[tag])
            offset = PICTURE_OFFSETS[tag]
            record[offset:offset + 4] = bytes([tag + 1, 0, tag + 20, 0])
            raw += record
        rows = extract_definitions(bytes(raw))
        self.assertEqual([(row["type_tag"], row["picture_number_one_based"],
                           row["legacy_definition_id"]) for row in rows],
                         [(tag, tag + 1, tag + 20) for tag in (0, 1, 2, 3, 4, 6)])

    def test_known_records_and_truncation(self):
        first = bytearray([2]) + bytearray(PAYLOAD_SIZES[2])
        first[6:10] = bytes([1, 0, 9, 0])
        second = bytearray([6]) + bytearray(PAYLOAD_SIZES[6])
        second[6:10] = bytes([2, 0, 9, 0])
        data = bytes(first + second)
        self.assertEqual(scan_records(data), [(0, 2), (1 + PAYLOAD_SIZES[2], 6)])
        self.assertEqual([(row["legacy_definition_id"], row["picture_number_one_based"])
                          for row in extract_definitions(data)], [(9, 1), (9, 2)])
        with self.assertRaisesRegex(ValueError, "truncated"):
            scan_records(data[:-1])
        with self.assertRaisesRegex(ValueError, "unknown"):
            scan_records(b"\x05")


if __name__ == "__main__":
    unittest.main()
