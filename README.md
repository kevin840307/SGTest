# SGTest — 後期《星夢 Online》素材研究優先

**目前階段：M0 / 原版素材來源、格式與載入邏輯研究。** 2026-11-13 按要求清除尚不需要的 Godot、金色 UI Gallery、.NET Server Demo 及相應 CI；保留 Git 歷史，尚未正式實作遊戲。

## 現階段第一目標
找出原版 Client 完整資源：確認原始檔案/封包位置和版本，從 Paradise.exe.lst 分析讀檔及解碼，建立可重現 decoder + Legacy Asset Viewer。保留角色每個方向與每個動作分離的原始語意，驗證攻擊/走路/受擊/死亡影格、偏移與圖層。**完成後**再討論 Godot Atlas、動畫系統和 MMORPG 正式架構。

## 工作入口
- [SPEC.md](SPEC.md)：完整長期需求與第 7 節素材研究 Gate
- [todo.txt](todo.txt)：跨 Session 唯一任務進度，AS001 起
- [docs/LEGACY_ASSET_PIPELINE.md](docs/LEGACY_ASSET_PIPELINE.md)：解析、匯出與驗收流程
- [research/source_manifest.json](research/source_manifest.json)：已知舊 repo 檔案索引（不是完整 Client）
- [AGENTS.md](AGENTS.md)：協作/安全/測試約束
- [docs/LEGACY_ASSET_EVIDENCE_20261113.md](docs/LEGACY_ASSET_EVIDENCE_20261113.md)：實際 Paradise.exe.lst 位址、XN SPR/SIF 解析、四個樣本與缺件矩陣

## 原版資料調查
- 參考（唯讀）：https://github.com/kevin840307/TestGoGo/tree/feature/local-server-net8
- 舊 Client 反編譯：Old/ClientData/Paradise.exe.lst
- 本機完整遊戲資料尚未提供；Git 中零散 .spr/.sif/.itd 不足以宣稱素材齊全。
- 唯讀盤點命令：
  `python tools/legacy_asset_inventory.py --root "C:\\OldGame" --output reports/legacy-assets.json --sha256`
- 執行前需將路徑改為實際遊戲資料夾；reports/ 被 gitignore。勿上傳原版未授權素材或解包產物。

## 後續方向
Godot 4 Client / .NET Server / PostgreSQL / 多人同步與低資源設計均保留於 SPEC，**不在素材確認前先行實作**。

## 2026-11-13 新增設計（尚未實作）

- [Adaptive Atlas / Map-aware Shared Asset Pool / Streaming](docs/ASSET_STREAMING_AND_RENDERING.md)：512/1024/2048/4096 候選頁、相同怪物/掉落圖共用、地圖熱度、資源回收、多解析度。
- [Client ↔ Server 與 UI/Render FPS 隔離](docs/CLIENT_RUNTIME_ISOLATION.md)：FPS、失焦、縮小、AFK 僅影響顯示與音效，不得提高或降低封包傳輸/解析/State/戰鬥/移動/撿物節奏。
- 上述均為 **DESIGN ONLY / NOT VERIFIED**，不得跳過 AS001～AS009 原版素材/授權 Gate。

## 2026-11-13：已建立可測試的素材研究工具（M0 未通過）

經原版 `Paradise.exe.lst` 與四個 NPC SPR 二進位樣本交叉分析，已找到部分 XN SPR 像素列編碼、SIF 描述資料及 group/LZSS/ACT/ITF 呼叫鏈。請參考上述實證文件區分 DIRECT、HYPOTHESIS、BLOCKED。

- `tools/legacy_asset_inventory.py`：唯讀清單，可選 SHA256 + 0～64 bytes 的 header_hex。
- `tools/legacy_spr_decoder.py`：限定已確認的 SPR 變體，輸出逐影格 PNG＋JSON；未知方向、時長、圖層等不造假，RGB565 仍待原版顏色驗證。
- `tools/legacy_asset_viewer.py`：獨立 Tkinter PNG/metadata 檢視器，未有原版幀時長時 100ms 只用於預覽。
- `python -m unittest discover -s tools/tests -p "test_*.py" -v`：使用自行合成 fixture，不會把原版素材加入 GitHub。
- [CI：10 tests PASS（2026-11-13）](https://github.com/kevin840307/SGTest/actions/runs/43430135649)。原版遊戲動畫實測、視覺一致性、完整 Client 與法律 Gate 均尚未完成。

未授權的遊戲安裝檔、原始圖片或匯出 PNG 請只放本機私人目錄，不可 push 至公開 Repo。
