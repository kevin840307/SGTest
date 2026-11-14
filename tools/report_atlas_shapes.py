"""Regenerate the layout decision report from measured and validated outputs."""
import json
from pathlib import Path

def main():
    read=lambda name:json.loads(Path('reports',name).read_text(encoding='utf-8'))
    plan=read('atlas_shapes.json')
    benchmark=read('atlas_shape_benchmark.json')
    inventory=read('asset_inventory.json')
    validation=read('asset_validation.json')
    full=(inventory.get('manifest_version')==8 and validation.get('catalog_version')==8 and
          inventory.get('shape_policies',{}).get('consistent')==inventory.get('entities_or_parts') and
          inventory.get('catalog_sha256')==validation.get('catalog_sha256') and validation.get('all_pixels') and
          validation.get('pixel_frames_checked')==inventory.get('frames') and not validation.get('errors'))
    single=sum(row['stored_images']<=1 for row in plan['rows'])
    lines=['# 圖集排列一致性：原始 SPR → 橫向多影格圖集','',
           '預設 `--shape consistent`：多個實際儲存影格使用寬 ≥ 高的圖集；只有一個儲存影格的原圖保留原生比例。'
           '不旋轉／縮放原始影格，不拆同方向動作區塊，鏡射和方向重用仍由 alias 表示。', '',
           '這讓動畫圖集有一致的觀看方向，同時避免把直式立繪、文字和單張地圖物件補成大面積透明方圖。'
           '圖內座標由 metadata 管理，原 SPR 影格順序保留在 clips；不要手改生成 PNG。','',
           '## 全目錄規劃與實際重建','',
           f"原始幾何規劃 {plan['groups']:,} 組，其中 {single:,} 組只有一張實際儲存圖。"
           f"全部常駐的純 RGBA 面積估算：{plan['legacy_rgba_bytes']/1048576:.2f} → "
           f"{plan['consistent_rgba_bytes']/1048576:.2f} MiB（{(plan['consistent_rgba_bytes']/plan['legacy_rgba_bytes']-1)*100:.2f}%）。"
           '這不是 runtime 常駐記憶體；遊戲只載入當前地圖／部件。', '',
           f"v8 全量來源重建與每影格像素回建：{'PASS' if full else 'NOT VERIFIED'}。"
           f"驗證檢查 {validation.get('pixel_frames_checked',0):,} 影格，錯誤 {len(validation.get('errors',[]))}。", '',
           '## 原始檔重建的代表樣本','',
           '每種排法都直接讀取原始 SPR，再回建每個來源影格比對像素。載入為熱 OS 快取，'
           'CPU 每組15次取中位數，Godot/OpenGL解碼＋上傳每列一次；不把單次差異當作穩定加速保證。','',
           '| 實體／部件 | 舊 compact MiB | consistent MiB | consistent 尺寸 | CPU decode ms | Godot ms |',
           '| --- | ---: | ---: | --- | ---: | ---: |']
    by_key={}
    for row in benchmark['rows']:by_key.setdefault(row['entity'],{})[row['shape']]=row
    for key,shapes in by_key.items():
        old=shapes['compact'];new=shapes['consistent']
        dimensions=', '.join(f'{w}×{h}' for w,h in new['dimensions'])
        lines.append(f"| {key} | {old['rgba_bytes']/1048576:.3f} | {new['rgba_bytes']/1048576:.3f} | {dimensions} | "
                     f"{new['decode_ms_p50']:.3f} | {new.get('native',{}).get('decode_upload_ms',0):.3f} |")
    lines+=['','## 重建／新增方式','', '```powershell',
            "# 從原始 Client 重新提取，再轉換（原始目錄唯讀）",
            "python tools/extract_client.py 'C:/SGO/Stargate Online IE HK2' .cache/client",
            'python tools/build_assets.py .cache/client client/assets/generated --shape consistent',
            '# 只重建指定實體；方向與動作會自動從其全部 SPR 集中，不必手排 PNG',
            'python tools/build_assets.py .cache/client client/assets/generated --entity enemy/en010a --shape consistent',
            '# 不帶 --entity 再跑時使用來源雜湊快取，產生完整 catalog；增量輸出為 partial_catalog',
            'python tools/validate_assets.py .cache/client client/assets/generated --all-pixels',
            '```','',
            '`--shape landscape` 可強制連單張原圖也橫向；`--shape compact` 可回復舊的面積優先排法。'
            '`--layout auto|entity|direction` 與橫直規則分開：前者決定張數，後者決定頁面形狀。', '',
            '重跑 A/B：`python tools/measure_atlas_shapes.py`、'
            '`python tools/benchmark_atlas_shapes.py .cache/client --godot <Godot執行檔>`。'
            '完整JSON在 atlas_shapes.json／atlas_shape_benchmark.json；原版／生成圖片只保留本機。']
    Path('reports/ATLAS_LAYOUT.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')

if __name__=='__main__':main()
