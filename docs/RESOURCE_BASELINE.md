# 資源 Runtime 可重現基準（PERF001）

本報告由 `tools/report_runtime_baseline.py` 從實際 JSON 產生。執行成功與達到效能門檻分開；未測項目不標完成。

## 測試界線

- 本機 Windows／RTX3050、Godot Compatibility、1440×900、60FPS cap；版本／檔案 SHA／素材頁 SHA 見摘要 JSON 的 provenance。
- Lifecycle 是同步 PNG decode＋ImageTexture 建立＋基準專用 force_sync，2 次暖機後 30 次載入／卸載；不是正式 Runtime store。GPU submit 是 CPU 呼叫耗時；sync wait 不是獨立 GPU timestamp。
- 多人為 20 個實際 Client（1 原生視窗＋19 headless）／300 怪／1000 掉落，40 秒執行、暖機 2 秒後取樣 35 秒；不等於 20 個 GPU 視窗，也不是掛機多開驗收。
- 候選保留同一批 Map13 九種來源。512／1024／2048／4096 是偏好尺寸／必要時 fallback，PNG 會裁至實際矩形；不是每張都配置完整正方形。direction 是整個來源方向分組的舊候選，不是最終 action×direction 混合策略。
- 快取為熱 OS；冷磁碟、實體 driver VRAM、真正 map/logout owner/LRU、時鐘／背景隔離與戰鬥完整負載仍 NOT VERIFIED。

## 載入／卸載（毫秒，RAM 為程序樹 RSS）

| 候選 | 頁 | RGBA MiB | load P95 | decode P95 | submit P95 | sync P95 | frame P99 / max | RSS MiB | 畫面門檻 | 卸載 RSS / texture |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| current | 13 | 10.87 | 56.36 | 5.60 | 0.18 | 0.00 | 60.24 / 61.33 | 161.22 | FAIL | PASS / PASS |
| direction_1024 | 45 | 9.33 | 39.21 | 1.83 | 0.07 | 0.00 | 43.23 / 51.99 | 160.43 | FAIL | PASS / PASS |
| direction_2048 | 45 | 9.21 | 38.88 | 1.74 | 0.06 | 0.00 | 42.19 / 42.35 | 160.60 | FAIL | PASS / PASS |
| direction_4096 | 45 | 9.21 | 42.09 | 1.75 | 0.07 | 0.00 | 47.43 / 57.12 | 159.45 | FAIL | PASS / PASS |
| direction_512 | 45 | 9.77 | 41.05 | 1.77 | 0.07 | 0.00 | 44.29 / 45.11 | 160.11 | FAIL | PASS / PASS |
| entity | 9 | 11.42 | 55.31 | 6.62 | 0.27 | 0.00 | 58.72 / 61.30 | 161.67 | FAIL | PASS / PASS |
| readable_auto | 13 | 14.78 | 58.66 | 6.51 | 0.26 | 0.00 | 60.55 / 78.24 | 166.26 | FAIL | PASS / PASS |

## 多人場景

| 候選 | FPS | frame P95 / P99 | Draw calls P95 | Client / Server RSS MiB | CPU Client / Server（一核心%） | RAM門檻 |
|---|---:|---:|---:|---:|---:|---|
| current | 60.00 | 22.32 / 24.93 | 435.00 | 207.73 / 79.78 | 43.80 / 4.65 | FAIL |
| direction_1024 | 59.99 | 22.40 / 24.18 | 446.00 | 202.65 / 79.32 | 42.65 / 6.20 | FAIL |
| entity | 59.99 | 21.92 / 23.23 | 427.00 | 202.71 / 79.22 | 43.10 / 9.20 | FAIL |
| readable_auto | 60.00 | 21.63 / 22.76 | 427.00 | 203.00 / 79.62 | 41.50 / 5.45 | FAIL |

## 判讀與後續

這是當前同步載入與舊分組的基準，尚未選定最終 Atlas 尺寸或排法。檢查各列 FAIL；不能只因 load 小於 1 秒便宣稱玩家無感，亦不能以平均 60FPS 排除角色位移頓挫。原始記錄、輸入 SHA 與各 gate 詳見 [摘要](../reports/runtime_baseline_summary.json)。

一般遊玩支援 **30～90FPS**；這只改呈現，不改 Client／Server 傳輸或固定邏輯頻率。5／10／15FPS 與停止世界繪製是另列的低功耗掛機模式。尚未跑完同 trace／背景／2／4／8 開，不能宣稱已支援。

下一步順序：ASSET201 標準動畫定義 → ASSET202 clip 原子／map 共現／hot-warm-cold 混合候選 → ASSET203 同場量測及 ADR 選型 → ASSET204 有界非同步資源 store → ASSET205 新怪物流程及完整回歸。新方案還須同素材像素／八方向／pivot／ACT 時間與原版對照；30 次 raw texture 釋放通過不能代替遊戲切圖／登出回收。

## 重跑

在儲存庫根目錄、原版只讀且 private generated assets 已建立時執行：

```powershell
.cache/dotnet/dotnet.exe build server/SGTest.Server.csproj -c Release
python tools/build_baseline_candidates.py --source .cache/client
python tools/benchmark_runtime_baseline.py 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --case all
# 各候選依序執行，禁止同時跑 GPU A/B：
python tools/benchmark_runtime_baseline.py 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --case all --monster-assets reports/atlas_candidates/direction_1024 --output reports/runtime_baseline/direction_1024
python tools/report_runtime_baseline.py
```

其他候選目錄為 entity、direction_512、direction_2048、direction_4096、readable_auto。執行工具回傳成功只代表程序／資料收集成功；門檻判定由摘要 gate 呈現。提交的報告只含量測與指紋，原始／生成圖像不公開。
