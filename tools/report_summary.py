"""Regenerate the reviewable report from measured JSON outputs."""
import json
from pathlib import Path
import statistics


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def main():
    inventory = read('reports/asset_inventory.json')
    validation = read('reports/asset_validation.json')
    network = read('reports/network_load.json')
    rendered = read('reports/rendered_load.json')
    layouts = read('reports/atlas_benchmark/rendered.json')['rows']
    samples = network['samples'][3:]
    server_cpu = statistics.median(s[0]['cpu_one_core_percent'] for s in samples)
    client_cpu = statistics.median(p['cpu_one_core_percent'] for s in samples for p in s[1:])
    server_rss = max(s[0]['rss_bytes'] for s in samples)/1024**2
    client_rss = max(p['rss_bytes'] for s in samples for p in s[1:])/1024**2
    lines = ['# 實測與素材驗證（2026-11-14）', '',
             '## 素材覆蓋', '',
             f"原版 Data1.dat SHA-256：`{inventory['archive']['archive_sha256']}`。解包後套用原版散檔覆蓋。", '',
             f"{inventory['source_sprites']:,} 個 SPR → {inventory['entities_or_parts']:,} 個實體／部件資源組、"
             f"{inventory['frames']:,} 影格、{inventory['textures']:,} 張紋理，另有 {inventory['support_assets']:,} 個音效／圖片／資料檔。", '',
             f"所有 {validation['pixel_frames_checked']:,} 個影格均從生成圖集重建並比對原 SPR 解碼結果；"
             f"來源覆蓋、原點、方向區塊與 alias 均通過。錯誤數：{len(validation['errors'])}。", '',
             '## 原生 Godot 載入', '',
             'RTX 3050、Compatibility/OpenGL；熱 OS 快取，計時不含程式啟動。每列為一次原生載入，'
             '較完整的 15 次 CPU 解碼樣本見 atlas_benchmark/results.json。RGBA 為紋理像素容量，未計驅動額外配置。', '',
             '| 素材 | 排法 | 張數 | RGBA MiB | Godot 解碼／建立紋理 ms |',
             '| --- | --- | ---: | ---: | ---: |']
    for row in layouts:
        lines.append(f"| {row['entity']} | {row['layout']} | {row['pages']} | {row['rgba_bytes']/1024**2:.2f} | {row['decode_upload_ms']:.2f} |")
    lines += ['', '因此採混合預設：小素材（唯一裁切 RGBA ≤ 1.5 MiB）合成一張，較大素材依方向分張。'
              '兩種方式都維持相同方向的動作在同一區塊；可用 --layout 明確覆寫。門檻是這批樣本的初始策略，並非所有硬體的最優保證。', '',
              '## 多人與實際畫面', '',
              f"ENet：Map{network.get('map_id',0)}、{network['clients']} 個 headless 客戶端、{network['monsters']:,} 隻怪、{network['drops']:,} 個掉落，持續 {network['seconds']} 秒。"
              f"通過：{network['passed']}；每位客戶端均收到位置、動作與背包更新。", '',
              f'略過前三個一秒樣本後，伺服器 CPU 中位數 {server_cpu:.1f}%，客戶端 {client_cpu:.1f}%；'
              f'尖峰 RSS 分別 {server_rss:.1f}／{client_rss:.1f} MiB。CPU 百分比以一個核心為 100%，'
              '含 Windows console 啟動器及其實際 Godot 子程序，不能解讀為整台電腦百分比。', '',
              f"原生單客戶端 1440×900、伺服器同樣 1000 怪／5000 掉落，RTX 3050、上限 60 FPS；暖機 2 秒後測 10 秒："
              f"平均 {rendered['mean_fps']:.2f} FPS，影格間隔 P50 {rendered['frame_ms_p50']:.2f}ms、P95 {rendered['frame_ms_p95']:.2f}ms。"
              f"末影格 draw calls {int(rendered['draw_calls_last_frame'])}。使用單調時鐘量測，非 Godot 平滑 delta。", '',
              '這些是本機開發版測試，非冷磁碟或多種硬體測試；受控 UDP 丟包測試另列如下。完整遊戲功能、角色裝備與外觀自訂、其他地圖及持久化仍待開發。', '',
              '原始 JSON：asset_inventory.json、asset_validation.json、network_load.json、rendered_load.json、rendered_probe.json、atlas_benchmark/results.json、atlas_benchmark/rendered.json。']
    Path('reports/README.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    combat_path = Path('reports/combat_network.json')
    if combat_path.exists():
        combat = read(combat_path)
        with Path('reports/README.md').open('a',encoding='utf-8') as report:
            report.write(f"\n## 雙向戰鬥同步\n\n{combat['clients']} 位獨立 ENet 觀察客戶端、{combat['seconds']} 秒，"
                         f"扣血／同一角色倒地／復活均收到：{combat['health_received_by_all']}／"
                         f"{combat['death_received_by_all']}／{combat['respawn_received_by_all']}。整體通過：{combat['passed']}。"
                         "此情境沒有掉落，因此不以背包 marker 作通過條件；背包另外由壓測及原子拾取測試驗證。\n")
    map_path = Path('reports/map_validation.json')
    if map_path.exists():
        maps = read(map_path)
        with Path('reports/README.md').open('a',encoding='utf-8') as report:
            report.write('\n## 原版地圖重建\n\n')
            for entry in maps['maps']:
                report.write(f"Map{entry['map_id']}：{entry['tile_references']:,} 個地磚索引、{entry['tiles']} 種共用地磚、"
                             f"{entry['objects']} 個物件，其中 {entry['animated_objects']} 個有多影格。"
                             f"地磚紋理 {entry['terrain_rgba_bytes']/1024**2:.2f} MiB；"
                             f"重建與地磚像素還原比對耗時 {entry['seconds']:.3f} 秒，通過：{entry['pixel_round_trip']}。"
                             "索引檔逐位元保留、碰撞資料與地圖尺寸互相驗證；伺服器不載入紋理。\n")
    render_path = Path('reports/map_render.json')
    if render_path.exists():
        scene = read(render_path)
        with Path('reports/README.md').open('a',encoding='utf-8') as report:
            report.write(f"\n原生地圖物件對照畫面：總計 {scene['total_terrain_cells']:,} 格中，僅建立 {scene['visible_terrain_instances']:,} 個可見地磚實例；"
                         f"{scene['visible_objects']} 個物件共用 {scene['resident_object_types']} 組紋理，物件 RGBA 合計 {scene['object_rgba_bytes']/1024**2:.2f} MiB，"
                         f"含角色的畫面 draw calls 為 {int(scene['draw_calls'])}。此為局部場景證據，不代表整張地圖同時載入。\n")

    for filename in ['impaired_network.json','impaired_combat.json']:
        path = Path('reports')/filename
        if not path.exists():
            continue
        test = read(path)
        impairment = test['impairment']
        metrics = test.get('prediction_metrics',[])
        rows = [row for client in metrics for row in client]
        with Path('reports/README.md').open('a',encoding='utf-8') as report:
            report.write(f"\n## 受控 UDP 測試：{filename}\n\n"
                         f"{test['clients']} 客戶端、{test['seconds']} 秒，實際 ENet 經 UDP 代理：每向 "
                         f"{impairment['one_way_delay_ms']:.0f}±{impairment['jitter_ms']:.0f}ms、"
                         f"{impairment['configured_loss']:.0%} 丟包及亂序。"
                         f"已轉送 {impairment['forwarded']:,} 包，丟棄 {impairment['dropped']:,} 包；通過：{test['passed']}。")
            if rows:
                report.write(f" 最大待確認輸入 {max(r['max_pending'] for r in rows)}／64，"
                             f"最大校正 {max(r['max_correction_px'] for r in rows):.2f}px。")
            report.write(" 這是可重跑的本機網路退化情境，非真實跨國連線或所有網路情況的保證。\n")


if __name__ == '__main__':
    main()
