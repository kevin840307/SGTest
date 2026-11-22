"""HK item.itd record-boundary and presentation-evidence tests."""
import struct
import sys
import unittest
from pathlib import Path

REPO=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(REPO/"tools"))
from asset_converter.source.hk_item_itd import (
    ID_OFFSET,NAME_OFFSET,RECORD_LENGTH,TOOLTIP_BYTES,TOOLTIP_OFFSET,
    parse_hk_item_itd_plain,
)


def record(item_type: int,item_id: int,name: str,tooltip: str="") -> bytes:
    value=bytearray(RECORD_LENGTH[item_type])
    value[0]=item_type
    struct.pack_into("<H",value,ID_OFFSET[item_type],item_id)
    encoded=name.encode("cp950")
    if len(encoded)>=20:
        raise ValueError("test name too long")
    at=NAME_OFFSET[item_type]
    value[at:at+len(encoded)]=encoded
    encoded_tip=tooltip.encode("cp950")
    if len(encoded_tip)>=TOOLTIP_BYTES[item_type]:
        raise ValueError("test tooltip too long")
    at=TOOLTIP_OFFSET[item_type]
    value[at:at+len(encoded_tip)]=encoded_tip
    return bytes(value)


class HKItemItdTests(unittest.TestCase):
    def test_all_client_record_types_consume_exact_boundaries(self):
        payload=b"".join(record(t,1000+t,f"測試{t}",f"原版說明{t}\n等級限制： 1")
                         for t in (0,1,2,3,4,6))
        rows=parse_hk_item_itd_plain(payload)
        self.assertEqual([x.item_type for x in rows],[0,1,2,3,4,6])
        self.assertEqual([x.hk_item_id for x in rows],[1000,1001,1002,1003,1004,1006])
        self.assertTrue(all(x.name_valid and x.tooltip_valid for x in rows))
        self.assertEqual(rows[2].name,"測試2")
        self.assertIn("等級限制： 1",rows[2].tooltip)
        expected=0
        for row in rows:
            self.assertEqual(row.offset,expected)
            expected+=RECORD_LENGTH[row.item_type]
        self.assertEqual(expected,len(payload))

    def test_unknown_and_truncated_records_fail_closed(self):
        with self.assertRaisesRegex(ValueError,"unknown record type"):
            parse_hk_item_itd_plain(b"\x05")
        with self.assertRaisesRegex(ValueError,"truncated"):
            parse_hk_item_itd_plain(record(3,123,"測試")[:-1])

    def test_legacy_rgba_controls_are_removed_before_cp950_decode(self):
        payload=bytearray(record(3,123,"合法名稱"))
        at=TOOLTIP_OFFSET[3]
        raw=(b"\x7f\xff\xff\xff\xff"
             +"等級限制： 5".encode("cp950")
             +b"\x7f\x15\xff\xff\xff"
             +"\n物品效果： 攻擊+20".encode("cp950"))
        payload[at:at+len(raw)]=raw
        rows=parse_hk_item_itd_plain(bytes(payload))
        self.assertTrue(rows[0].tooltip_valid)
        self.assertEqual(rows[0].tooltip_color_tokens,2)
        self.assertEqual(rows[0].tooltip,"等級限制： 5\n物品效果： 攻擊+20")

    def test_equipment_structured_offsets_and_legacy_escape_order(self):
        payload=bytearray(record(0,31207,"測試裝備"))
        payload[0x07]=6
        for at,value in ((0x0E,101),(0x10,102),(0x12,103),(0x14,104),
                         (0x18,105),(0x1A,106),(0x1C,107)):
            struct.pack_into("<H",payload,at,value)
        struct.pack_into("<I",payload,0x2E,50)
        tip="說明\\n第二行\\e高亮\n其餘".encode("cp950")
        at=TOOLTIP_OFFSET[0]
        payload[at:at+len(tip)]=tip
        row=parse_hk_item_itd_plain(bytes(payload))[0]
        self.assertEqual(row.equipment_category,6)
        self.assertEqual(row.max_loss,50)
        self.assertEqual(
            (row.attack,row.defense,row.magic_attack,row.magic_defense,
             row.hit_or_block,row.evasion,row.luck),
            (101,102,103,104,105,106,107))
        self.assertEqual(row.tooltip,"說明\n第二行\n其餘\n高亮")
        self.assertEqual(row.tooltip_loss_insert,len("說明\n第二行"))

    def test_bad_cp950_tooltip_is_excluded_without_losing_boundary(self):
        payload=bytearray(record(3,123,"合法名稱","正常說明"))
        payload[TOOLTIP_OFFSET[3]]=0xff
        rows=parse_hk_item_itd_plain(bytes(payload))
        self.assertEqual(len(rows),1)
        self.assertTrue(rows[0].name_valid)
        self.assertFalse(rows[0].tooltip_valid)
        self.assertEqual(rows[0].tooltip,"")


if __name__=="__main__":
    unittest.main()
