"""HK petcli.ITD loader-boundary tests; no private game data required."""
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.source.hk_petcli_itd import (
    parse_hk_petcli_plain, read_hk_petcli,
)


def cstring(value: str) -> bytes:
    return value.encode("cp950") + b"\0"


def evolution(
    evolution_id: int,
    outlook: str,
    *,
    raw_a: int = 50,
    raw_b: int = 5,
    variant: int = 0,
) -> bytes:
    return (
        bytes([evolution_id])
        + raw_a.to_bytes(2, "little")
        + raw_b.to_bytes(2, "little")
        + cstring("測試進化")
        + cstring(outlook)
        + bytes([variant])
        + cstring("原版寵物說明")
    )


def family(pet_id: int, evolutions: list[bytes]) -> bytes:
    return (
        pet_id.to_bytes(2, "little")
        + cstring("測試寵物")
        + bytes([len(evolutions)])
        + b"".join(evolutions)
    )


class HKPetCliItdTests(unittest.TestCase):
    def test_exact_client_loader_order_and_full_consumption(self):
        payload = (
            b"\x01\x00"
            + family(5100, [
                evolution(0, r"Enemy\En050a"),
                evolution(7, r"Enemy\En013b", raw_a=170, raw_b=7, variant=2),
            ])
        )
        rows, header = parse_hk_petcli_plain(payload)
        self.assertEqual(header, 0)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0].source_pet_id, 5100)
        self.assertEqual([x.evolution_id for x in rows[0].evolutions], [0, 7])
        self.assertEqual(rows[0].evolutions[0].outlook, r"Enemy\En050a")
        self.assertEqual(
            (rows[0].evolutions[1].unknown_u16_a,
             rows[0].evolutions[1].unknown_u16_b,
             rows[0].evolutions[1].variant_byte),
            (170, 7, 2),
        )

    def test_reader_audit_does_not_expose_original_prose(self):
        payload = b"\x01\x03" + family(5100, [evolution(0, r"Enemy\En050a")])
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "petcli.ITD"
            path.write_bytes(payload)
            rows, audit = read_hk_petcli(path)
        self.assertEqual(len(rows), 1)
        self.assertEqual(audit["family_count"], 1)
        self.assertEqual(audit["evolution_count"], 1)
        self.assertEqual(audit["unique_sprite_count"], 1)
        self.assertEqual(audit["header_byte"], 3)
        self.assertEqual(audit["record_boundaries"], "confirmed_full_consumption")
        self.assertNotIn("測試寵物", str(audit))

    def test_duplicate_identity_and_invalid_outlook_fail_closed(self):
        duplicate = (
            b"\x01\x00"
            + family(5100, [
                evolution(1, r"Enemy\En050a"),
                evolution(1, r"Enemy\En051a"),
            ])
        )
        with self.assertRaisesRegex(ValueError, "duplicate EvolID"):
            parse_hk_petcli_plain(duplicate)
        invalid = b"\x01\x00" + family(5100, [evolution(0, r"Player\G")])
        with self.assertRaisesRegex(ValueError, "invalid Enemy outlook"):
            parse_hk_petcli_plain(invalid)

    def test_truncated_and_trailing_payload_fail_closed(self):
        valid = b"\x01\x00" + family(5100, [evolution(0, r"Enemy\En050a")])
        with self.assertRaisesRegex(ValueError, "truncated|unterminated"):
            parse_hk_petcli_plain(valid[:-1])
        with self.assertRaisesRegex(ValueError, "did not consume"):
            parse_hk_petcli_plain(valid + b"\x00")


if __name__ == "__main__":
    unittest.main()
