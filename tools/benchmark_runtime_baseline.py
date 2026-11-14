"""Reproducible native GPU lifecycle + real multiplayer baseline.

Generated art stays private. Outputs contain measurements, hashes and logs.
No experimental atlas builder/streaming manager is selected by this runner.
"""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import tempfile
import time
import psutil
from runtime_processes import server_command

RUN_ENV = {}

def asset_identity(monster_assets=None):
    root=Path('client/assets/generated').resolve()
    population=root/'maps/13/population.json'
    definition=json.loads(population.read_text(encoding='utf-8'))
    atlas_root=Path(monster_assets).resolve() if monster_assets else root
    files={population}
    groups=[]
    for relative in sorted({row['atlas'] for row in definition['definitions']}):
        path=atlas_root/relative
        manifest=json.loads(path.read_text(encoding='utf-8'))
        files.add(path)
        files.update(path.parent/page for page in manifest['pages'])
        groups.append({'manifest':relative,'version':manifest['version'],
                       'shape':manifest.get('shape',manifest.get('atlas_shape','unspecified')),
                       'page_sizes':manifest['page_sizes'],'pages':len(manifest['pages'])})
    candidate=atlas_root/'candidate.json'
    if candidate.exists():files.add(candidate)
    digests={path.as_posix():hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(files)}
    # Relative labels keep this identity stable when the checkout moves.
    identity=[(path.relative_to(atlas_root).as_posix() if path.is_relative_to(atlas_root) else 'population.json',
               digests[path.as_posix()]) for path in sorted(files)]
    return {'root':str(atlas_root.resolve()),'groups':groups,'files':digests,
            'content_sha256':hashlib.sha256(json.dumps(identity,sort_keys=True).encode()).hexdigest()}


def provenance(monster_assets=None):
    files=['client/scripts/main.gd','client/scripts/entity_atlas.gd','client/scripts/monster_visuals.gd',
           'client/scripts/frame_profiler.gd','client/project.godot','server/bin/Release/net8.0/SGTest.Server.dll',
           'client/tools/benchmark_asset_lifecycle.gd','tools/benchmark_runtime_baseline.py',
           'client/assets/generated/catalog.json']
    return {'git_head':subprocess.check_output(['git','rev-parse','HEAD']).decode().strip(),
            'tracked_worktree_diff_sha256':hashlib.sha256(subprocess.check_output(['git','diff','HEAD','--binary'],stderr=subprocess.DEVNULL)).hexdigest(),
            'files':{name:hashlib.sha256(Path(name).read_bytes()).hexdigest() for name in files},
            'assets':asset_identity(monster_assets),'runtime_environment':RUN_ENV,
            'os':platform.platform(),'cpu_logical':psutil.cpu_count(),'physical_ram_bytes':psutil.virtual_memory().total,
            'cache':'warm OS cache; cold disk NOT VERIFIED','physical_driver_vram':'NOT VERIFIED (engine counters reported separately)'}

def meters(processes):
    result=[]
    for process in processes:
        root=psutil.Process(process.pid)
        result.append([root,*root.children(recursive=True)])
    for group in result:
        for process in group:process.cpu_percent()
    return result

def sample(groups):
    values=[]
    for group in groups:
        current={'cpu_one_core_percent':0,'rss_bytes':0}
        for process in group:
            try:
                current['cpu_one_core_percent']+=process.cpu_percent()
                current['rss_bytes']+=process.memory_info().rss
            except psutil.NoSuchProcess:pass
        values.append(current)
    return values

def stop(processes):
    for process in reversed(processes):
        if process.poll() is None:
            try:
                for child in psutil.Process(process.pid).children(recursive=True):child.terminate()
                process.terminate()
            except psutil.NoSuchProcess:pass
        process.wait(timeout=10)

def lifecycle(godot,output,cycles,monster_assets=None):
    native_report=(output/'lifecycle.json').resolve()
    before=provenance(monster_assets)
    Path(str(native_report)+'.progress.json').write_text('{"cycle":-1,"phase":"starting"}',encoding='utf-8')
    started_wall=time.time()
    with tempfile.TemporaryFile(mode='w+',encoding='utf-8') as log:
        command=[godot,'--path','client','--rendering-method','gl_compatibility','--max-fps','60','--resolution','1440x900',
                 '--script','tools/benchmark_asset_lifecycle.gd','--',f'--cycles={cycles}',f'--report={native_report}']
        if monster_assets:command+=[f'--monster-assets={monster_assets.resolve()}']
        process=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT)
        points=[]
        try:
            time.sleep(.5);groups=meters([process]);started=time.monotonic()
            while process.poll() is None:
                time.sleep(.01)
                marker={}
                try:marker=json.loads(Path(str(native_report)+'.progress.json').read_text(encoding='utf-8'))
                except (OSError,ValueError):pass
                points.append({'elapsed':time.monotonic()-started,'processes':sample(groups),'marker':marker})
                if time.monotonic()-started>180:raise TimeoutError('lifecycle baseline exceeded 180s')
        finally:stop([process])
        log.seek(0);text=log.read()
    result=json.loads(native_report.read_text(encoding='utf-8')) if native_report.exists() and native_report.stat().st_mtime>=started_wall else {}
    after=provenance(monster_assets)
    result.update({'exit_code':process.returncode,'process_samples':points,'log':text,
                   'rss_peak_bytes':max((p['processes'][0]['rss_bytes'] for p in points),default=0),
                   'measurement_provenance':before,
                   'source_files_unchanged_during_run':before['files']==after['files'] and before['assets']==after['assets']})
    unload_rss={}
    for point in points:
        marker=point['marker']
        if marker.get('phase')=='unloaded':unload_rss.setdefault(marker['cycle'],[]).append(point['processes'][0]['rss_bytes'])
    import statistics
    per_cycle={cycle:statistics.median(values) for cycle,values in unload_rss.items()}
    warm=[value for cycle,value in per_cycle.items() if cycle<2]
    measured=[per_cycle[cycle] for cycle in range(2,cycles+2) if cycle in per_cycle]
    result['unloaded_rss_per_cycle']=per_cycle
    if warm and len(measured)==cycles:
        baseline=warm[-1];growth=statistics.median(measured[-10:])-statistics.median(measured[:10])
        result['ram_recovery']={'warm_baseline_bytes':baseline,'after_peak_bytes':max(measured),'last10_minus_first10_median_bytes':growth,
                                'passed':max(measured)<=baseline+max(16*1048576,baseline*.1) and growth<=8*1048576}
    else:result['ram_recovery']={'passed':None,'status':'NOT VERIFIED: missing per-cycle RSS samples'}
    result['execution_passed']=process.returncode==0 and 'ERROR:' not in text and bool(result.get('records'))
    native_report.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:result.get(k) for k in ['execution_passed','cycles','all_texture_handles_released','engine_texture_recovery_passed','frame_ms_p95','frame_ms_p99','rss_peak_bytes']},indent=2))
    return result['execution_passed']

def multiplayer(godot,output,clients,monsters,drops,seconds,monster_assets=None):
    processes=[];logs=[];points=[]
    started=time.time()
    before=provenance(monster_assets)
    try:
        for index in range(clients+1):
            log=tempfile.TemporaryFile(mode='w+',encoding='utf-8');logs.append(log)
            if index==0:command=server_command()+[f'--monsters={monsters}',f'--drops={drops}','--map=13']
            else:
                command=[godot,'--path','client','--max-fps','60']
                if index==1:command+=['--rendering-method','gl_compatibility','--resolution','1440x900']
                else:command+=['--headless']
                command+=['--','--bot',f'--bot-index={index}']
                if monster_assets:command+=[f'--monster-assets={monster_assets.resolve()}']
                if index==1:command+=['--profile',f'--profile-seconds={seconds-5}']
            processes.append(subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT))
            if index==0:time.sleep(1)
        time.sleep(1);groups=meters(processes)
        for _ in range(seconds):
            time.sleep(1)
            if any(p.poll() is not None for p in processes):raise RuntimeError('baseline process exited early')
            points.append(sample(groups))
    finally:stop(processes)
    contents=[]
    for log in logs:
        log.seek(0);contents.append(log.read());log.close()
    profile_path=Path('reports/rendered_load.json')
    profile=json.loads(profile_path.read_text(encoding='utf-8')) if profile_path.exists() and profile_path.stat().st_mtime>=started else None
    passed=(contents[0].count(' joined')==clients and profile is not None and
            all('prediction_ready' in log and 'snapshot_received' in log for log in contents[1:]) and
            not any('ERROR:' in log for log in contents))
    after=provenance(monster_assets)
    report={'execution_passed':passed,'scenario':{'clients':clients,'native_clients':1,'headless_clients':clients-1,
            'monster_assets':before['assets']['root'],
            'monsters':monsters,'drops':drops,'map':13,'seconds':seconds,'render_cap':60,'resolution':[1440,900],
            'bot_paths':'deterministic per-client phase; not identical trajectories'},
            'rendered':profile,'samples':points,'logs':contents,'measurement_provenance':before,
            'source_files_unchanged_during_run':before['files']==after['files'] and before['assets']==after['assets'],
            'frame_p95_target_passed':profile is not None and profile['frame_ms_p95']<=25,
            'frame_p99_target_passed':profile is not None and profile.get('frame_ms_p99',float('inf'))<=40}
    (output/'multiplayer.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ['logs','samples','measurement_provenance']},indent=2))
    return passed

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot',type=Path)
    parser.add_argument('--case',choices=['lifecycle','multiplayer','all'],default='all')
    parser.add_argument('--output',type=Path,default=Path('reports/runtime_baseline'))
    parser.add_argument('--cycles',type=int,default=30)
    parser.add_argument('--clients',type=int,default=20)
    parser.add_argument('--monsters',type=int,default=300)
    parser.add_argument('--drops',type=int,default=1000)
    parser.add_argument('--seconds',type=int,default=40)
    parser.add_argument('--monster-assets',type=Path)
    args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    if args.cycles<1 or args.clients<1 or args.seconds<20:parser.error('positive cycles/clients and >=20s required')
    passed=True;godot=str(args.godot.resolve())
    RUN_ENV.update({'godot_version':subprocess.check_output([godot,'--version'],text=True).strip(),
                    'godot_executable_sha256':hashlib.sha256(Path(godot).read_bytes()).hexdigest(),
                    'dotnet_sdk_version':subprocess.check_output([server_command()[0],'--version'],text=True).strip(),
                    'python':platform.python_version()})
    if args.case in ['lifecycle','all']:passed=lifecycle(godot,args.output,args.cycles,args.monster_assets) and passed
    if args.case in ['multiplayer','all']:passed=multiplayer(godot,args.output,args.clients,args.monsters,args.drops,args.seconds,args.monster_assets) and passed
    return 0 if passed else 1

if __name__=='__main__':raise SystemExit(main())
