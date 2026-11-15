"""Planning must match real packing while allocating no texture pages."""
import json
import tempfile
import struct
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_converter.atlas.dense_atlas import build_group
from test_asset_converter import tiny_spr
from asset_converter.atlas.archive_audit import audit_archive
from asset_converter.cli import archive_all
from asset_converter import cli
from asset_converter.source.convert_assets import convert_act,convert_sif,convert_draw_order
from asset_converter.operation_lock import converter_operation,ConverterBusy


class PlanningTests(unittest.TestCase):
    def test_busy_cli_does_not_execute_or_overwrite_reports(self):
        with patch.object(sys,'argv',['cli.py','build','unused','--archive-only']), \
             patch.object(cli,'converter_operation',side_effect=ConverterBusy('fixture busy')), \
             patch.object(cli,'_execute') as execute,patch.object(cli,'write_json') as write:
            self.assertEqual(cli.main(),2)
            execute.assert_not_called()
            write.assert_not_called()

    def test_cross_process_lock_and_stale_file_release(self):
        code='''import sys,os
sys.path.insert(0,sys.argv[2])
from pathlib import Path
from asset_converter.operation_lock import converter_operation,ConverterBusy
try:
 with converter_operation(Path(sys.argv[1])):
  if len(sys.argv)>3:os._exit(0)
except ConverterBusy:sys.exit(2)
'''
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'operation.lock'
            command=[sys.executable,'-c',code,str(path),str(Path(__file__).resolve().parents[1])]
            with converter_operation(path):
                result=subprocess.run(command,capture_output=True,text=True,timeout=10)
                self.assertEqual(result.returncode,2,result.stderr)
            self.assertTrue(path.exists())
            self.assertEqual(subprocess.run(command,timeout=10).returncode,0)
            # Process death releases the OS lock even when finally never ran.
            self.assertEqual(subprocess.run(command+['abrupt-exit'],timeout=10).returncode,0)
            with converter_operation(path):pass

    def test_sidecar_content_corruption_is_detected_and_rebuilt(self):
        class Settings:
            category=None
            prefix=None
            preview_groups=None
            skip_sidecars=False
            force=False
        with tempfile.TemporaryDirectory() as temp:
            source,archive=Path(temp)/'source',Path(temp)/'archive'
            source.mkdir()
            (source/'one.spr').write_bytes(tiny_spr())
            act=source/'one.act'
            act.write_bytes(b'\x01'+struct.pack('<I',2)+b'S\0'+b'\x03'+struct.pack('<I',1)
                            +b'\x01\x02'+struct.pack('<III',1,0,5)+b'\xff')
            sif=source/'one.sif'
            name=b'one.spr'
            sif.write_bytes(bytes([len(name)])+name+b'\x00'+struct.pack('<4i',1,2,3,4))
            draw=source/'CharDrawOrder.dat'
            draw.write_text('NS1:Body+2>Head\n',encoding='ascii')
            archive_all(source,archive,Settings())
            self.assertEqual(audit_archive(source,archive)['status'],'pass')
            for path,suffix,convert in [(act,'.act.json',convert_act),(sif,'.sif.json',convert_sif),
                                        (draw,'.draw_order.json',convert_draw_order)]:
                target=archive/'sidecars'/(path.stem+suffix)
                data=json.loads(target.read_text())
                original_hash=data['sha256']
                data['corrupted_field']='unexpected generated metadata'
                target.write_text(json.dumps(data))
                self.assertEqual(json.loads(target.read_text())['sha256'],original_hash)
                self.assertEqual(audit_archive(source,archive)['status'],'failed')
                self.assertEqual(convert(path,source,archive/'sidecars'),'converted')
                self.assertEqual(convert(path,source,archive/'sidecars'),'cached')
            self.assertEqual(audit_archive(source,archive)['status'],'pass')

    def test_complete_archive_audit_and_corrupted_page(self):
        class Settings:
            category=None
            prefix=None
            preview_groups=None
            skip_sidecars=False
            force=False
        with tempfile.TemporaryDirectory() as temp:
            source,archive=Path(temp)/'source',Path(temp)/'archive'
            source.mkdir()
            (source/'one.spr').write_bytes(tiny_spr())
            archive_all(source,archive,Settings())
            report=audit_archive(source,archive)
            self.assertEqual(report['status'],'pass',report)
            self.assertEqual(report['counts']['sources'],1)
            self.assertEqual(report['counts']['frames'],1)
            (archive/'sprites/one/page_0.png').write_bytes(b'bad PNG')
            self.assertEqual(audit_archive(source,archive)['status'],'failed')

    def test_preview_index_is_not_full_archive_evidence(self):
        class Settings:
            category=None
            prefix=None
            preview_groups=1
            skip_sidecars=True
            force=False
        with tempfile.TemporaryDirectory() as temp:
            source,archive=Path(temp)/'source',Path(temp)/'archive'
            source.mkdir()
            (source/'one.spr').write_bytes(tiny_spr())
            (source/'two.spr').write_bytes(tiny_spr())
            archive_all(source,archive,Settings())
            report=audit_archive(source,archive)
            self.assertEqual(report['status'],'failed')
            self.assertTrue(any('index' in error for error in report['errors']))

    def test_invalid_json_type_is_rebuilt_as_a_cache_miss(self):
        with tempfile.TemporaryDirectory() as temp:
            source,output = Path(temp)/'source',Path(temp)/'output'
            source.mkdir()
            path = source/'one.spr'
            path.write_bytes(tiny_spr())
            manifest = output/'one/atlas.json'
            manifest.parent.mkdir(parents=True)
            manifest.write_text('[]')
            self.assertEqual(build_group(source,output,Path('one'),[path])['status'],'converted')
            self.assertIsInstance(json.loads(manifest.read_text()),dict)






if __name__ == '__main__':
    unittest.main()
