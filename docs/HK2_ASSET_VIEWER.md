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
