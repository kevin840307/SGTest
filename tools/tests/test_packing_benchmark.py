import unittest
from PIL import Image

from asset_converter.benchmarks.packing.build_candidates import pack_maxrects, choose
from asset_converter.source.runtime_spr import Frame


class PackingBenchmarkTests(unittest.TestCase):
    def test_frame_level_maxrects_preserves_pixels_crop_and_origin(self):
        a=Image.new("RGBA",(12,10),(0,0,0,0))
        b=Image.new("RGBA",(9,13),(0,0,0,0))
        for x in range(2,10):
            for y in range(1,8):
                a.putpixel((x,y),(255,10,20,255))
        for x in range(1,8):
            for y in range(3,12):
                b.putpixel((x,y),(10,220,30,255))
        frames=[Frame(a,4,5),Frame(b,-2,9)]
        pages,records=pack_maxrects(frames,64)
        self.assertEqual(len(records),2)
        for frame,record in zip(frames,records):
            self.assertGreaterEqual(record["page"],0)
            self.assertEqual(record["raw_origin"],[frame.origin_x,frame.origin_y])
            self.assertEqual(record["crop"],list(frame.image.getbbox()))
            x,y,w,h=record["rect"]
            actual=pages[record["page"]].crop((x,y,x+w,y+h))
            expected=frame.image.crop(tuple(record["crop"]))
            self.assertEqual(actual.tobytes(),expected.tobytes())

    def test_candidate_prefers_fewer_pages_within_five_percent_area(self):
        rows=[
            {"side":1024,"pages":3,"rgba_bytes":100},
            {"side":2048,"pages":1,"rgba_bytes":104},
            {"side":4096,"pages":1,"rgba_bytes":120},
        ]
        self.assertEqual(choose(rows)["side"],2048)


if __name__=="__main__":
    unittest.main()
