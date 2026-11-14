# HK2 素材逐格檢視器

`tools/view_legacy_asset.py` 可直接打開乾淨客戶端的一個 `.spr`，或 `tools/convert_assets.py` 輸出的 SPR `.json`。它會把 atlas 裁切與原始透明邊界還原成完整幀，因此可並排核對原始 SPR 與轉換結果。提供上一幀、下一幀、固定間隔預覽、水平鏡像、raw origin 十字線與 1～4 倍像素放大。

```powershell
py -m pip install -r tools/requirements.txt
py tools/view_legacy_asset.py 'C:\SGO\Stargate Online IE HK2\Enemy\EN023A_NS1.spr'
py tools/view_legacy_asset.py local-assets/hk2-atlas-512/Enemy/EN023A_NS1.json
py -m unittest tools.tests.test_view_legacy_asset
```

播放鍵的 100 ms 是**人工預覽間隔**，並非原版動作速度；真實 ACT 更新頻率仍在研究。鏡像勾選是視覺檢查工具，不會自動判定該動作是否應鏡像；規則見 [方位映射](HK2_DIRECTION_MAPPING.md)。十字線畫在 raw origin 欄位表示的像素座標，實際世界錨點及紙娃娃疊圖仍需與原版畫面比對。尚未支援 ACT 動作選單、多層裝備合成或批次匯出，因此 AS006 仍為部分完成。

已用自造 atlas 裁切 fixture 驗證原始透明邊界、raw origin 與鏡像像素；也用 HK2 的 `Enemy/EN023A_NS1` 分幀與 512 atlas JSON 各還原 10 幀、首幀 227×202、origin (107,138)。GUI 肉眼比對尚待進行。

## 怪物 ACT 動作／方位預覽

`tools/view_enemy_animation.py` 讀本機私有的 `enemy_catalog.json`，可選怪物、`S/W/D/A/H` 動作、1～8 邏輯方位，逐步顯示來源 SPR 幀索引、原始 origin、鏡像、ACT raw_time、預設 Update 呼叫次數、事件 raw frame 位置及 `H` 特殊路徑註記。只解碼**目前選中動作／方位**引用的圖集頁；換選項時釋放舊頁引用。視窗上限 1440×900，播放間隔固定 100ms，明確標示不是原版毫秒。

```powershell
py tools/view_enemy_animation.py local-assets/enemy-all-complete/1024/enemy_catalog.json
py -m unittest tools.tests.test_view_enemy_animation
```

本機用乾淨 HK2 `EN023A` 的 15 個來源 SPR 對照 catalog：5 動作、40 邏輯方位、272 播放步的還原**像素與 raw origin 逐步相等**；39 項 Python 測試包含自造圖集裁切／鏡像 fixture。原版實際畫面與 GUI 人工肉眼比對尚未完成；`EN060A`、`EN061A`、`EN115A` 被完整性稽核隔離，這個 Viewer 不會假造其未解動作。人物紙娃娃、多層裝備及正式客戶端動畫仍未驗證。
