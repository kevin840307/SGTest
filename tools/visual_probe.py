"""Capture the real GPU-rendered client, not the headless dummy renderer."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import time
import psutil


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot', type=Path)
    parser.add_argument('--stress', action='store_true')
    parser.add_argument('--map',type=int,default=13)
    args = parser.parse_args()
    exe = str(args.godot.resolve())
    output = Path('reports/client.png').resolve()
    output.parent.mkdir(exist_ok=True)
    started = time.time()
    with tempfile.TemporaryFile(mode='w+', encoding='utf-8') as log:
        extra = (['--monsters=1000','--drops=5000'] if args.stress else [])+[f'--map={args.map}']
        server = subprocess.Popen([exe,'--headless','--path','.','--max-fps','60','--','--server']+extra,stdout=log,stderr=subprocess.STDOUT)
        try:
            time.sleep(1)
            if server.poll() is not None:
                raise RuntimeError('probe server exited before client startup')
            client = subprocess.run([exe,'--path','.','--rendering-method','gl_compatibility','--max-fps','60',
                                     '--resolution','1440x900','--quit-after','900' if args.stress else '300',
                                     '--','--capture='+str(output)]+(['--profile'] if args.stress else []),
                                    capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=40)
        finally:
            for child in psutil.Process(server.pid).children(recursive=True):
                child.terminate()
            server.terminate()
            server.wait(timeout=10)
    fresh = output.exists() and output.stat().st_mtime >= started
    result = {'exit_code':client.returncode, 'screenshot_exists':fresh, 'stress':args.stress,
              'map_id':args.map,
              'server_monsters':1000 if args.stress else 80, 'server_drops':5000 if args.stress else 0,
              'log':client.stdout+client.stderr}
    Path('reports/rendered_probe.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False,indent=2))
    return client.returncode or (0 if fresh and 'ERROR:' not in result['log'] else 1)


if __name__ == '__main__':
    raise SystemExit(main())
