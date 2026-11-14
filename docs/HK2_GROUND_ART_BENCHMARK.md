# HK2 地上物品圖：逐圖與局部圖集比較

日期：2026-11-14。原版參考 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 的 `CSGOItemDrawLogic::InitItem`，`.text:004CCD17`～`004CCDA9`，在 `.text:004CCD62` 引用 `Interface\ground.spr`，並在 `.text:004CCD75`～`004CCDA3` 將 item 物件的 `+0xCC` 欄位交給繪圖物件。這是參考版本的 DIRECT 反編譯證據；`+0xCC` 與正式物品定義 ID 的對照尚未驗證。乾淨 HK2 的 `Interface/ground.spr` SHA-256 為 `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6`，可解出 500 個非空影格（最大 82×54）。前一份混合負載中的 `MS` 小圖是機甲類代理，**不是**經證實的掉落物圖。

## 重跑

```powershell
py tools/build_ground_candidates.py 'C:\SGO\Stargate Online IE HK2' local-assets/ground-candidates --sample-count 25
py tools/build_ground_candidates.py 'C:\SGO\Stargate Online IE HK2' local-assets/ground-all-candidates --sample-count 500
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_ground_render.gd -- local-assets/ground-candidates/1024/catalog.json 1440 900
py -m unittest discover -s tools/tests -q
```

`--sample-count 25` 固定取原始影格 0、20、…、480；每種候選都用**相同 25 張解碼像素**和 500 個實例，逐圖候選保留每張來源尺寸，Atlas 候選用 2px 邊緣延展。`--sample-count 500` 則測所有影格同時可見的高 unique 壓力，不代表單一地圖實際會掉出全部 500 種。轉換腳本逐影格驗證輸出與解碼來源像素一致；原始及衍生 PNG 都在 git 忽略的 `local-assets/`。

Godot 4.7.2 OpenGL Compatibility／RTX 3050，視窗 1440×900，固定 500 個按 Y 排序的可見實例。每次 160 幀，前 40 暖機，後 120 取統計；每候選交錯跑三次。下表載入與 frame p95 是三次中位，Video Mem monitor 含引擎／視窗開銷，不是純貼圖 VRAM。

| 同屏圖種 | 候選 | 頁數 | 實際 RGBA 面積 bytes | PNG bytes | 載入中位 ms | Draw calls p50 | Frame p95 中位 ms | Video Mem monitor bytes |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 25 | 逐圖 | 25 | 104,696 | 42,585 | 7.051 | 572 | 7.989 | 18,848,431 |
| 25 | 1024 | 1 | 179,584 | 37,358 | 1.141 | 73 | 8.012 | 18,949,283 |
| 25 | 2048 | 1 | 179,584 | 37,358 | 1.174 | 73 | 8.003 | 18,949,283 |
| 25 | 4096 | 1 | 179,584 | 37,358 | 1.105 | 73 | 8.009 | 18,949,283 |
| 500 | 逐圖 | 500 | 2,006,004 | 848,054 | 161.763 | 572 | 7.972 | 21,347,039 |
| 500 | 1024 | 1 | 3,605,052 | 738,532 | 36.151 | 73 | 7.993 | 23,513,803 |
| 500 | 2048 | 1 | 3,870,300 | 737,662 | 37.433 | 73 | 8.000 | 23,865,823 |
| 500 | 4096 | 1 | 3,981,312 | 733,564 | 37.638 | 73 | 8.006 | 24,010,623 |

500 種的逐圖第一次載入曾量到 1566ms，後兩次為 160.125／161.763ms；表格使用三次中位，不把首次結果當穩定值。載入受 OS 檔案快取影響，**沒有**量到 GPU commit 尖峰。800×600 另跑逐圖／1024 各一次，實際視窗正確，draw calls 573／74；不是完整多尺寸驗收。

**目前選擇**：已知會同時出現的一批地上物品，優先用 1024 局部圖集，按地圖工作集載入；稀有或不共現的單一圖可維持獨立貼圖，不能因此常駐整張 500 影格圖。此場景的 2048／4096 沒有載入或 draw calls 收益，500 種時還增加記憶體監測值。逐圖的實際 RGBA 面積較小，影格 p95 差異在試跑波動內，因此不能稱逐圖渲染「會 lag」；圖集的明確收益是更少 texture 切換／draw calls 和高 unique 情況較低的載入中位。下一步要建立 item 定義→ground 影格映射、實際地圖掉落共現集合、GPU upload／資源卸載和與怪物／玩家混合的真場景基準。

後續的 [HK2 物品定義稽核](HK2_ITEM_LIBRARY_MAPPING.md) 已查出 `ground1.spr` 另有 500 幀、1,047 筆定義的圖號超過 500。本頁的 500 種負載僅代表第一檔 `ground.spr` 的全部影格，不能當成所有 HK2 物品圖的完整集合。
