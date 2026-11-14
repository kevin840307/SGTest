"""Summarize measured runs without turning execution success into acceptance."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics

MIB = 1048576


def quantile(values, fraction):
    if not values:
        return None
    values = sorted(values)
    return values[max(0, math.ceil(len(values) * fraction) - 1)]


def status(value):
    return 'NOT VERIFIED' if value is None else ('PASS' if value else 'FAIL')


def lifecycle_summary(data):
    records = [row for row in data.get('records', []) if not row['warmup']]
    result = {key: data.get(key) for key in ['cycles', 'execution_passed', 'frame_ms_p95',
              'frame_ms_p99', 'frame_ms_max', 'rss_peak_bytes', 'texture_peak_bytes',
              'source_files_unchanged_during_run']}
    for source, label in [('decode_ms', 'decode'), ('upload_submit_ms', 'submit'),
                          ('gpu_sync_wait_ms', 'sync_wait')]:
        values = [value for row in records for value in row['loading'][source]]
        result[label + '_p95_ms'] = quantile(values, .95)
        result[label + '_p99_ms'] = quantile(values, .99)
        result[label + '_max_ms'] = max(values) if values else None
    loads = [row['loading']['load_wall_ms'] for row in records]
    result['load_wall_p95_ms'] = quantile(loads, .95)
    result['pages'] = records[0]['pages'] if records else None
    result['rgba_bytes'] = records[0]['loading']['rgba_bytes'] if records else None
    static = [row['after_unload']['godot_static_bytes'] for row in records]
    warm = data.get('warm_baseline', {}).get('godot_static_bytes')
    gates = {'execution': data.get('execution_passed'),
             'input_integrity': data.get('source_files_unchanged_during_run'),
             'texture_handles': data.get('all_texture_handles_released'),
             'engine_texture_recovery': data.get('engine_texture_recovery_passed'),
             'rss_recovery': data.get('ram_recovery', {}).get('passed'),
             'static_recovery': max(static) <= warm + max(8*MIB, warm*.1) if static and warm else None,
             'loading_frame': data['frame_ms_p99'] <= 40 and data['frame_ms_max'] <= 66.7
                              if data.get('frame_ms_p99') is not None and data.get('frame_ms_max') is not None else None,
             'gpu_submit': result['submit_p95_ms'] <= 4 and result['submit_p99_ms'] <= 8 and result['submit_max_ms'] <= 16
                           if result['submit_p95_ms'] is not None else None,
             'thirty_cycles': len(records) >= 30}
    result['ram_recovery'] = data.get('ram_recovery')
    result['gates'] = {key: status(value) for key, value in gates.items()}
    return result


def multiplayer_summary(data):
    rendered = data.get('rendered') or {}
    samples = data.get('samples', [])
    result = {'scenario': data.get('scenario'), 'rendered': rendered}
    for index, label in [(0, 'server'), (1, 'native_client')]:
        group = [row[index] for row in samples if len(row) > index]
        result[label + '_rss_peak_bytes'] = max((row['rss_bytes'] for row in group), default=None)
        result[label + '_cpu_median_one_core_percent'] = statistics.median(
            row['cpu_one_core_percent'] for row in group) if group else None
    gates = {'execution': data.get('execution_passed'),
             'input_integrity': data.get('source_files_unchanged_during_run'),
             'frame_p95': rendered['frame_ms_p95'] <= 25 if 'frame_ms_p95' in rendered else None,
             'frame_p99': rendered['frame_ms_p99'] <= 40 if 'frame_ms_p99' in rendered else None,
             'client_ram': result['native_client_rss_peak_bytes'] <= 200*MIB
                           if result['native_client_rss_peak_bytes'] is not None else None,
             'client_cpu': result['native_client_cpu_median_one_core_percent'] <= 100
                           if result['native_client_cpu_median_one_core_percent'] is not None else None,
             'server_cpu': result['server_cpu_median_one_core_percent'] <= 30
                           if result['server_cpu_median_one_core_percent'] is not None else None,
             'tick_work': None, 'physical_driver_vram': None,
             'fps_background_isolation': None, 'runtime_owner_lru_logout': None}
    result['gates'] = {key: status(value) for key, value in gates.items()}
    return result


def collect(root):
    rows = []
    for folder in [root, *sorted(path for path in root.iterdir() if path.is_dir())]:
        row = {'candidate': 'current' if folder == root else folder.name, 'inputs': {}}
        for name, summarize in [('lifecycle', lifecycle_summary), ('multiplayer', multiplayer_summary)]:
            path = folder/(name+'.json')
            if path.exists():
                data = json.loads(path.read_text(encoding='utf-8'))
                row[name] = summarize(data)
                row['inputs'][name] = {'path':path.as_posix(), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                                      'measurement_provenance':data.get('measurement_provenance')}
        if len(row) > 2:
            rows.append(row)
    return {'runs': rows, 'selection': 'NOT SELECTED: baseline only; mixed builder and runtime store pending'}


def number(value, scale=1):
    return 'N/V' if value is None else f'{value/scale:.2f}'


def markdown(report):
    lines = ['# 資源 Runtime 可重現基準（PERF001）', '',
        '本報告由 `tools/report_runtime_baseline.py` 從實際 JSON 產生。執行成功與達到效能門檻分開；未測項目不標完成。', '',
        '## 測試界線', '',
        '- 本機 Windows／RTX3050、Godot Compatibility、1440×900、60FPS cap；版本／檔案 SHA／素材頁 SHA 見摘要 JSON 的 provenance。',
        '- Lifecycle 是同步 PNG decode＋ImageTexture 建立＋基準專用 force_sync，2 次暖機後 30 次載入／卸載；不是正式 Runtime store。GPU submit 是 CPU 呼叫耗時；sync wait 不是獨立 GPU timestamp。',
        '- 多人為 20 個實際 Client（1 原生視窗＋19 headless）／300 怪／1000 掉落，40 秒執行、暖機 2 秒後取樣 35 秒；不等於 20 個 GPU 視窗，也不是掛機多開驗收。',
        '- 候選保留同一批 Map13 九種來源。512／1024／2048／4096 是偏好尺寸／必要時 fallback，PNG 會裁至實際矩形；不是每張都配置完整正方形。direction 是整個來源方向分組的舊候選，不是最終 action×direction 混合策略。',
        '- 快取為熱 OS；冷磁碟、實體 driver VRAM、真正 map/logout owner/LRU、時鐘／背景隔離與戰鬥完整負載仍 NOT VERIFIED。', '',
        '## 載入／卸載（毫秒，RAM 為程序樹 RSS）', '',
        '| 候選 | 頁 | RGBA MiB | load P95 | decode P95 | submit P95 | sync P95 | frame P99 / max | RSS MiB | 畫面門檻 | 卸載 RSS / texture |',
        '|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|']
    for row in report['runs']:
        d = row.get('lifecycle')
        if d:
            g = d['gates']
            lines.append(f"| {row['candidate']} | {d['pages']} | {number(d['rgba_bytes'],MIB)} | {number(d['load_wall_p95_ms'])} | {number(d['decode_p95_ms'])} | {number(d['submit_p95_ms'])} | {number(d['sync_wait_p95_ms'])} | {number(d['frame_ms_p99'])} / {number(d['frame_ms_max'])} | {number(d['rss_peak_bytes'],MIB)} | {g['loading_frame']} | {g['rss_recovery']} / {g['engine_texture_recovery']} |")
    lines += ['', '## 多人場景', '',
        '| 候選 | FPS | frame P95 / P99 | Draw calls P95 | Client / Server RSS MiB | CPU Client / Server（一核心%） | RAM門檻 |',
        '|---|---:|---:|---:|---:|---:|---|']
    for row in report['runs']:
        d = row.get('multiplayer')
        if d:
            r = d['rendered']
            lines.append(f"| {row['candidate']} | {number(r.get('mean_fps'))} | {number(r.get('frame_ms_p95'))} / {number(r.get('frame_ms_p99'))} | {number(r.get('draw_calls_p95'))} | {number(d['native_client_rss_peak_bytes'],MIB)} / {number(d['server_rss_peak_bytes'],MIB)} | {number(d['native_client_cpu_median_one_core_percent'])} / {number(d['server_cpu_median_one_core_percent'])} | {d['gates']['client_ram']} |")
    lines += ['', '## 判讀與後續', '',
        '這是當前同步載入與舊分組的基準，尚未選定最終 Atlas 尺寸或排法。檢查各列 FAIL；不能只因 load 小於 1 秒便宣稱玩家無感，亦不能以平均 60FPS 排除角色位移頓挫。原始記錄、輸入 SHA 與各 gate 詳見 [摘要](../reports/runtime_baseline_summary.json)。', '',
        '一般遊玩支援 **30～90FPS**；這只改呈現，不改 Client／Server 傳輸或固定邏輯頻率。5／10／15FPS 與停止世界繪製是另列的低功耗掛機模式。尚未跑完同 trace／背景／2／4／8 開，不能宣稱已支援。', '',
        '下一步順序：ASSET201 標準動畫定義 → ASSET202 clip 原子／map 共現／hot-warm-cold 混合候選 → ASSET203 同場量測及 ADR 選型 → ASSET204 有界非同步資源 store → ASSET205 新怪物流程及完整回歸。新方案還須同素材像素／八方向／pivot／ACT 時間與原版對照；30 次 raw texture 釋放通過不能代替遊戲切圖／登出回收。', '',
        '## 重跑', '', '在儲存庫根目錄、原版只讀且 private generated assets 已建立時執行：', '', '```powershell',
        '.cache/dotnet/dotnet.exe build server/SGTest.Server.csproj -c Release',
        "python tools/build_baseline_candidates.py --source .cache/client",
        "python tools/benchmark_runtime_baseline.py 'C:\\SGO\\Godot\\Godot_v4.7.2-stable_win64_console.exe' --case all",
        '# 各候選依序執行，禁止同時跑 GPU A/B：',
        "python tools/benchmark_runtime_baseline.py 'C:\\SGO\\Godot\\Godot_v4.7.2-stable_win64_console.exe' --case all --monster-assets reports/atlas_candidates/direction_1024 --output reports/runtime_baseline/direction_1024",
        'python tools/report_runtime_baseline.py', '```', '',
        '其他候選目錄為 entity、direction_512、direction_2048、direction_4096、readable_auto。執行工具回傳成功只代表程序／資料收集成功；門檻判定由摘要 gate 呈現。提交的報告只含量測與指紋，原始／生成圖像不公開。', '']
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, default=Path('reports/runtime_baseline'))
    parser.add_argument('--json', type=Path, default=Path('reports/runtime_baseline_summary.json'))
    parser.add_argument('--markdown', type=Path, default=Path('docs/RESOURCE_BASELINE.md'))
    args = parser.parse_args()
    report = collect(args.input)
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    args.markdown.write_text(markdown(report), encoding='utf-8')
    print(f'{len(report["runs"])} measured candidate sets; final policy NOT SELECTED')


if __name__ == '__main__':
    main()
