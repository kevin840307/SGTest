"""Build and verify a local Windows release without importing raw sprite PNGs.

Run from any directory. Official 4.7.2 Windows templates must be placed in
.cache/godot-templates (see docs/PERF002_MEMORY.md); no global installation.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

REPO = Path(__file__).resolve().parents[1]
TEMPLATE_SHA256 = {
    'windows_release_x86_64.exe': 'd34d36f3be1a6c49c56525ae86469b92e4f417ddf0b43cf00dd80c385c4b0562',
    'windows_debug_x86_64.exe': '51498b72b3a237f882ebd7d1787f06a4bc1eaf0572daab93837adcfd3cfdc107',
}


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify_package(executable, assets, report_path):
    """Reject a stale PCK or mismatching raw assets before runtime measurement."""
    executable, assets, report_path = map(lambda p: Path(p).resolve(), (executable, assets, report_path))
    report_path.parent.mkdir(parents=True, exist_ok=True)
    started = time.time()
    checked = subprocess.run([str(executable), '--headless', '--', '--verify-pack',
                              f'--export-check-report={report_path}'],
                             cwd=REPO, capture_output=True, text=True, timeout=30)
    if checked.returncode or not report_path.is_file() or report_path.stat().st_mtime < started:
        raise RuntimeError('export integrity check did not complete: ' + checked.stdout + checked.stderr)
    report = json.loads(report_path.read_text(encoding='utf-8'))
    if not report.get('passed') or not report.get('template_release') or report.get('editor_build') or report.get('debug_build'):
        raise RuntimeError('packed release integrity failed')
    client = REPO / 'client'
    expected_sources = {f'res://{path.relative_to(client).as_posix()}': sha256(path)
                        for folder in ['scripts', 'scenes', 'shaders']
                        for path in (client / folder).rglob('*')
                        if path.is_file() and path.suffix in ['.gd', '.tscn', '.gdshader']}
    expected_sources['res://project.godot'] = sha256(client / 'project.godot')
    if report.get('source_sha256') != expected_sources:
        raise RuntimeError('exported app sources differ from checkout; rebuild before comparing')
    expected_assets = {f'res://assets/{path.relative_to(assets).as_posix()}': sha256(path)
                       for path in assets.rglob('*')
                       if path.is_file() and path.suffix in ['.png', '.json', '.bin']}
    if not expected_assets or report.get('asset_sha256') != expected_assets:
        raise RuntimeError('PCK raw assets differ from declared source folder')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot', type=Path)
    parser.add_argument('--assets', type=Path, default=REPO / 'client/assets')
    parser.add_argument('--output', type=Path, default=REPO / '.cache/client-release/SGTest.exe')
    args = parser.parse_args()
    godot, output, assets = args.godot.resolve(), args.output.resolve(), args.assets.resolve()
    version = subprocess.check_output([str(godot), '--version'], text=True).strip()
    if not version.startswith('4.7.2.stable.'):
        parser.error('this preset requires official Godot 4.7.2 and matching templates')
    for name, digest in TEMPLATE_SHA256.items():
        path = REPO / '.cache/godot-templates' / name
        if not path.is_file() or sha256(path) != digest:
            parser.error(f'official template missing/mismatched: {path}; see docs/PERF002_MEMORY.md')
    if not (assets / 'bundles/index.json').is_file():
        parser.error('build runtime assets from the original game first')
    output.parent.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, SGTEST_EXPORT_ASSETS=assets.as_posix())
    for arguments in [['--editor', '--import'], ['--export-release', 'Windows Desktop', str(output)]]:
        subprocess.run([str(godot), '--headless', '--path', str(REPO / 'client'), *arguments],
                       cwd=REPO, env=env, check=True, timeout=180)
    report = verify_package(output, assets, output.with_suffix('.integrity.json'))
    print(json.dumps({'passed': True, 'raw_files': report['files'], 'executable': str(output),
                      'executable_sha256': sha256(output), 'package_sha256': sha256(output.with_suffix('.pck'))}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
