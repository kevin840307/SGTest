# Client Runtime Isolation Verification — RUNTIME206

日期：2026-11-14。狀態：**RUNTIME206 PASS / physical driver VRAM 與 120FPS 額外壓測仍非正式 Gate**。

## 1. 目標

Render FPS、低功耗、失焦、最小化與多開只能改變呈現成本，不能改變：

- Client fixed logic/input cadence
- socket RX/TX 與 packet reducer cadence
- ServerSimClock
- authoritative state reduction
- reliable action ordering
- Server simulation

正式一般遊玩 Render cap：30～90FPS，預設60FPS；標準選項30/45/60/75/90，自訂上限允許30～90。低功耗5/10/15是 **VisualClock**，不是 Engine/main-loop FPS。

## 2. Before：直接把 Engine 壓到5FPS會壞

最初 probe 直接 `--max-fps 5`：

- physics 平均仍約29.96Hz，但 Godot 在每個 render frame 內補跑多個 physics catch-up。
- socket→main reducer queue P95/P99 約180/181ms。
- max incoming depth 47。
- TX 約50.7Hz，因 input command 也被 catch-up physics burst 一起送出。

這正是低FPS時「畫面一格一格、遠端角色/怪物跳一下」的結構性原因。平均 physics=30Hz 並不能證明 fixed logic 與 Render 已隔離。

## 3. After：固定 Logic 30Hz，Visual 獨立節流

### 3.1 Engine/main-loop 下限

- `client/project.godot` 預設 `application/run/max_fps=60`。
- Runtime 強制：若外部/project cap 落到1～29，會拉回30。
- `--render-fps=N` 支援30～90。
- `--visual-fps=5|10|15` 只改世界呈現頻率，Engine/logic保持30。
- 直接再次故意傳 `--max-fps 5`：實際 physics/engine/visual均≈30Hz，input≈30Hz，queue P99 33.1ms，不再回到180ms burst。

### 3.2 ServerSimClock / ping

- `ServerSimClock.advance()` 已從 `_process()` 移到 `_physics_process()` fixed logic path。
- ping 也由 fixed logic排程，不再依 Render callback。
- Render只讀 `server_time`，不再推進模擬時鐘。

### 3.3 Presentation policy

- 正常前景：依 Render cap呈現。
- 手動 low-power：Visual 5/10/15，logic/network維持30Hz。
- Unfocus預設：世界呈現降到15FPS，不卸載資源。
- `--unfocus-low-power=0`：失焦仍維持正常呈現。
- Minimize：不論Unfocus開關，世界呈現停止；asset store、network、state reducer、fixed logic仍運作。
- `--no-world-render`：顯式停止世界 projection/redraw，不斷包、不卸載。

## 4. Runtime Trace

`--runtime-trace` 僅測試使用，記錄：

- physics / engine frame / visual update Hz
- input command Hz
- RX/TX packet rate
- socket receive→reducer drain queue age P95/P99/max與max depth
- snapshot/owner/action counts
- prediction seq/tick/pending
- authoritative own relative position
- window focus/mode
- Godot texture/video/static memory
- 每150 Server tick deterministic authoritative `state_checkpoint` hash

state hash只包含排序後的 authoritative players/monsters/drops；不含 render interpolation、VisualClock或GPU狀態。

## 5. 正式30/45/60/75/90：同 Server / 同 input / 同 state

同一台 Server 同時掛五個 Client，全部 `bot-index=1`，deterministic input sequence相同；Render cap 使用正式 Client `--render-fps=30/45/60/75/90` 路徑。共同 Server tick 150 / 300 / 450 的 authoritative hash **全部完全一致**。

| Cap | Physics Hz | 實際 Engine frame Hz | Input Hz | RX Hz | TX Hz | Queue P99 ms |
|---:|---:|---:|---:|---:|---:|---:|
| 30 | 29.997 | 29.997 | 29.997 | 20.998 | 30.998 | 27.894 |
| 45 | 30.004 | 45.009 | 30.004 | 20.999 | 31.005 | 20.287 |
| 60 | 29.999 | 59.995 | 29.999 | 20.998 | 30.999 | 33.024 |
| 75 | 30.003 | 59.988 | 30.003 | 20.999 | 31.003 | 22.742 |
| 90 | 30.003 | 59.992 | 30.003 | 20.999 | 31.003 | 21.270 |

75/90在本機實際只有約60FPS，是60Hz/VSync測試環境限制；軟體接受75/90 cap，但不把硬體受限的60FPS冒充成90FPS實測。最重要的fixed logic/input/RX/TX/state hash完全不受Cap影響。

## 6. Low-power 5/10/15 / no-world：同 Server state hash

同一 Server 同時掛 normal30、Visual5、Visual10、Visual15、no-world 五個 Client；共同 tick 150 / 300 / 450 state hash全部一致。

| Mode | Physics Hz | Visual Hz | Input Hz | RX Hz | TX Hz | Queue P99 ms |
|---|---:|---:|---:|---:|---:|---:|
| normal30 | 30.002 | 30.003 | 30.002 | 20.999 | 31.002 | 33.618 |
| visual5 | 29.999 | 5.000 | 29.998 | 20.997 | 30.998 | 33.319 |
| visual10 | 30.002 | 10.001 | 30.002 | 20.999 | 31.002 | 32.892 |
| visual15 | 29.999 | 15.000 | 29.999 | 20.997 | 30.999 | 30.141 |
| no-world | 30.003 | 0.000 | 30.003 | 20.999 | 31.003 | 30.202 |

因此低功耗是Presentation downgrade，不是降低Client↔Server同步頻率。

## 7. 真 Windows Unfocus / Minimize / Restore

使用非-console Godot executable取得真HWND，Windows API實際切換視窗狀態；不是只模擬旗標。

### Production預設

- Minimize成功：trace明確 `window_mode=MINIMIZED`、world visual≈0，physics/input≈30Hz；restore後回正常。
- Unfocus成功：`window_focused=false`、world visual≈15FPS，physics/input≈30Hz；restore後回正常。
- 前景baseline與背景Client同Server，tick150/300/450 authoritative hash全部一致。
- minimize/background兩Client queue P99均<34ms；unfocus同樣<34ms。

### Unfocus開關關閉

`--unfocus-low-power=0`：

- 真失焦仍維持約30FPS呈現，state hash一致。
- 真最小化仍停止world render，state hash一致。

因此「Unfocus可關閉、Minimize永遠生效」已有真視窗測試。

## 8. no-world重負載資源

300 monsters + 500 drops，30FPS engine：

| 指標 | 正常世界渲染 | no-world |
|---|---:|---:|
| Physics Hz | 30.000 | 30.041 |
| Visual Hz | 30.000 | 0.000 |
| RX Hz | 123.03 | 124.56 |
| Queue P99 ms | 33.04 | 32.92 |
| Client CPU（單核心%中位數） | 20.15 | 14.50 |
| Client RSS peak MiB | 205.12 | 204.55 |
| Texture monitor MiB | 43.48 | 40.27 |
| Video monitor MiB | 51.68 | 48.42 |

CPU中位數約下降28%，同步指標沒有下降。這符合「省渲染，不靠斷包/降logic省資源」。

## 9. 1/2/4/8原生Client worst-case多開

刻意關閉自動Unfocus，讓所有視窗都走60FPS呈現路徑；300 monsters + 500 drops。這比一般背景多開更嚴格。

| Clients | Physics範圍 Hz | Queue P99 max ms | CPU合計（cores%） | RSS合計 peak MiB | Texture counter sum MiB | Video counter sum MiB |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 30.000 | 33.289 | 37.06 | 203.68 | 42.35 | 50.55 |
| 2 | 29.993～30.004 | 33.307 | 70.06 | 403.99 | 83.16 | 96.03 |
| 4 | 29.989～29.997 | 32.912 | 187.78 | 807.66 | 166.32 | 192.05 |
| 8 | 29.987～30.019 | 33.301 | 333.65 | 1621.84 | 332.63 | 380.09 |

全部Client prediction/snapshot ready，0 Server error，reducer cadence沒有因多開掉速。CPU/RAM/Godot GPU counters近似隨Client數增加；這是實際成本，不隱藏。

## 10. Packet burst / reliable ordering

- synthetic 128-packet完整MessagePack burst：NetworkClient一次drain+decode **0.879ms**，遠低於2秒Gate。
- `test_replica.gd` 已驗證 stale snapshot不覆蓋newer reliable action、duplicate/stale action revision不回滾、death barrier不被舊snapshot復活。
- Transport為WebSocket/TCP ordered reliable path；同Server state-hash與live多人測試未出現packet/order ERROR。

## 11. Gate結論

RUNTIME206正式Gate：

- 一般30～90 Render cap：PASS（75/90實際render受60Hz/VSync硬體限制，logic/state仍PASS）。
- 預設60FPS：PASS。
- low-power Visual5/10/15：PASS。
- Engine cap<30防誤用：PASS，自動拉回30。
- no-world：PASS。
- 真Unfocus/Minimize/Restore：PASS。
- Unfocus toggle / Minimize always-on：PASS。
- same-server state hash跨FPS/low-power/background：PASS。
- queue P99 <50ms：PASS；正式矩陣最差<38ms，背景<34ms，多開<34ms。
- input fixed≈30Hz：PASS（0怪cadence/state-hash矩陣）。
- 1/2/4/8多開：PASS。
- 128 packet burst<2s：PASS。

所以 **RUNTIME206 可標完成**。

## 12. 保留限制

- Physical driver VRAM沒有直接driver counter；表中GPU是Godot各process的texture/video monitor，明確標示不是實體VRAM。
- 120FPS只保留額外壓測，不是正式支援Gate。
- cold-disk與不同GPU/螢幕refresh rate不是本次矩陣；75/90需要高刷新率/對應VSync策略才能實際觀察到>60 render FPS。
- AS005原版同版本逐格人工視覺與AS008發布權仍未完成，與Runtime isolation是不同Gate。

## 13. 重跑

```powershell
cd C:\SGO\SRC\SGTest

# 正常FPS同Server state hash
py tools/benchmark_runtime_fps_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --seconds 18

# Low-power/no-world同Server state hash
py tools/benchmark_runtime_lowpower_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --seconds 18

# 真Windows background + state hash
py tools/benchmark_runtime_background_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --seconds 18
py tools/benchmark_runtime_background_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --disable-unfocus --seconds 18

# no-world resource A/B
py tools/benchmark_runtime_isolation.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --fps 30 --seconds 15 --monsters 300 --drops 500
py tools/benchmark_runtime_isolation.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --fps 30 --seconds 15 --monsters 300 --drops 500 --no-world

# 1/2/4/8 worst-case native clients
py tools/benchmark_runtime_multiopen.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --counts 1 2 4 8 --seconds 15 --monsters 300 --drops 500

# synthetic queue burst
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_network_queue.gd
```

## 14. Completion rerun（2026-11-14）

在正式標記 RUNTIME206 完成前，於目前 `main` 工作樹重新執行全部 Gate；以下數字是本次重跑，不以較早一次量測取代：

- deterministic regression：Python `test_*.py` 41 PASS；C# server 353 checks PASS；Godot 9 組 headless tests PASS；128-packet burst drain/decode 0.888ms。
- 30/45/60/75/90 同 Server：tick 150/300/450 authoritative hash 全一致；physics/input 29.967～30.001Hz；queue P99 最差 38.992ms。
- Visual 5/10/15/no-world：實際 visual 約 5.066/9.934/15.000/0Hz；physics/input 仍約30Hz；state hash 全一致；queue P99 最差 33.312ms。
- 真 Windows background：production minimize/unfocus 與 `--unfocus-low-power=0` 兩輪皆 PASS，restore 可觀察，state hash 一致；queue P99 最差約33.25ms。
- 故意 `--max-fps 5`：runtime guard 將 engine/physics/visual/input 維持約30.002Hz，queue P99 24.344ms，證明不能用低 Engine FPS 破壞固定邏輯。
- no-world 300 monsters / 500 drops：normal CPU 中位數 18.44% → no-world 10.07%（單核心百分比），texture monitor 43.48→40.27MiB；RSS peak 204.55→207.72MiB，該次 RAM 有噪聲，原樣記錄不宣稱下降。
- worst-case native multi-open（全部 `--unfocus-low-power=0`）：1/2/4/8 client 全 PASS；8開 physics 29.957～30.012Hz、queue P99 33.22ms、incoming depth 35、0 Server error；Client RSS sum peak 1619.04MiB、CPU sum median 384.79%。

### 玩家可用設定

現有 HUD 的「效能」區已直接提供 Render FPS 30/45/60/75/90，預設60；Unfocus 節能 checkbox 可開關。這些控制只改 `Engine.max_fps`／VisualClock presentation policy。Minimize 仍永遠停止世界呈現且不受 Unfocus toggle 影響；physics/input/network reducer/Server 固定30Hz。

上述 completion rerun 的 JSON 保留於本機 `reports/*_verify.json`（reports 為私有量測輸出，不提交原始媒體）。
