# HK2 動作／方向 Atlas locality A/B（2026-11-14）

原始／衍生媒體只留在 gitignore 的 local-assets。此文件記錄打包與 GPU 實測，不代表原版視覺／動畫毫秒已完全驗證。

## 問題

舊 dense frame packing 在最初 50 組 loose Enemy 候選的 2,000 個 action×logical-facing 中有 173 個跨頁。單純每個 SPR 一張 texture 可消除大多數跨頁，但 50 組曾達 750 頁，低 unique 場景 draw calls 上升，因此不採用。

## Atomic MaxRects v3

tools/build_atlas_groups.py --source-locality 現在先把每個原始 SPR 的 frames 打成不可拆 atomic block，再用 deterministic no-rotation MaxRects-style placement 把多個完整 block co-pack 到同一頁。方向重用／flip 只存在 catalog metadata，不預先複製鏡像圖。tools/audit_action_page_locality.py --require-single-page 作嚴格 Gate。

對最初 50 組 loose candidate，v3 為 119 pages / 466,182,124 RGBA bytes，2,000/2,000 action-facing 單頁。全部世界 50 種常駐仍比 dense 多約 45 MB Godot Video Memory，因此正式策略必須搭配 map palette，而不是一次載完。

## 真實已知地圖內容

Data1 + loose overlay 的 29 種實際地圖怪物：atomic 1024 為 32 pages / 62,287,496 RGBA bytes / 1,160/1,160 action-facing 單頁；dense 1024 為 32 pages / 63,770,056 RGBA bytes / 9/1,160 action-facing 跨頁。Godot headless 驗證 29 groups / 145 actions / 1,160 directions / 8,792 playback steps PASS。

Map13 詳細 GPU A/B 與 runtime palette 見 HK2_MAP_ASSET_PALETTE.md。正式 StreamingManager、多人網路與原版視覺仍 NOT VERIFIED。
