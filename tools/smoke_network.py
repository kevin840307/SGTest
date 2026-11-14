"""Real ENet multi-client load probe, CPU/RSS and snapshot delivery evidence."""
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
    parser.add_argument('--clients', type=int, default=2)
    parser.add_argument('--monsters', type=int, default=1000)
    parser.add_argument('--drops', type=int, default=5000)
    parser.add_argument('--seconds', type=int, default=15)
    parser.add_argument('--combat-arena', action='store_true', help='idle observers verify HP/death/respawn over real ENet')
    args = parser.parse_args()
    processes, logs, measurements = [], [], []
    base = [str(args.godot.resolve()), '--headless', '--path', '.', '--max-fps', '60']
    try:
        for index in range(args.clients+1):
            log = tempfile.TemporaryFile(mode='w+', encoding='utf-8')
            logs.append(log)
            extra = ['--server', f'--monsters={args.monsters}', f'--drops={args.drops}'] if index == 0 else ['--bot']
            if args.combat_arena:
                extra = ['--server','--monsters=1','--drops=0','--combat-arena'] if index == 0 else []
            processes.append(subprocess.Popen(base+['--']+extra, stdout=log, stderr=subprocess.STDOUT))
            if index == 0:
                time.sleep(1)
        time.sleep(1)
        # Windows *_console.exe is a launcher; meter the whole process tree.
        meters = [[psutil.Process(p.pid), *psutil.Process(p.pid).children(recursive=True)] for p in processes]
        for tree in meters:
            for meter in tree:
                meter.cpu_percent()
        for _ in range(args.seconds):
            time.sleep(1)
            if any(p.poll() is not None for p in processes):
                raise RuntimeError('a Godot process exited early')
            measurements.append([{'cpu_one_core_percent': sum(m.cpu_percent() for m in tree),
                                  'rss_bytes': sum(m.memory_info().rss for m in tree),
                                  'processes': len(tree)} for tree in meters])
    finally:
        for process in processes:
            if process.poll() is None:
                for child in psutil.Process(process.pid).children(recursive=True):
                    child.terminate()
                process.terminate()
            process.wait(timeout=10)
    contents = []
    for log in logs:
        log.seek(0)
        contents.append(log.read())
        log.close()
    joined = contents[0].count(' joined')
    delivered = all('snapshot_received' in content for content in contents[1:])
    errors = [c for c in contents if 'SCRIPT ERROR' in c or 'ERROR:' in c]
    actions = all('action_received' in content for content in contents[1:])
    inventories = all('inventory_received' in content for content in contents[1:])
    health = all('health_received' in content for content in contents[1:])
    deaths = all('death_received' in content for content in contents[1:])
    respawns = all('respawn_received' in content for content in contents[1:])
    behavior = health and deaths and respawns if args.combat_arena else actions and inventories
    passed = joined == args.clients and delivered and behavior and not errors
    report = {'passed': passed, 'clients': args.clients, 'monsters': 1 if args.combat_arena else args.monsters,
              'drops': 0 if args.combat_arena else args.drops, 'combat_arena':args.combat_arena,
              'seconds': args.seconds, 'joined': joined, 'snapshots_received_by_all': delivered,
              'actions_received_by_all': actions, 'inventories_received_by_all': inventories,
              'health_received_by_all':health,'death_received_by_all':deaths,'respawn_received_by_all':respawns,
              'headless': True, 'samples': measurements, 'logs': contents}
    out = Path('reports/combat_network.json' if args.combat_arena else 'reports/network_load.json')
    out.parent.mkdir(exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('samples', 'logs')}, indent=2))
    for content in errors:
        print(content)
    return 0 if passed else 1


if __name__ == '__main__':
    raise SystemExit(main())
