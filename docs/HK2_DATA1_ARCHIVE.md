# HK2 Data1.dat 群組檔與 loose overlay 證據

日期：2026-11-14。狀態：DIRECT 格式實證 + 本機真檔驗證。原始／解包媒體只存放於使用者本機 local-assets，不提交公開 Git。

## 原版讀檔順序與 archive 格式

- CELApplication::FileOpenGroup .text:00506677 → CELFileIO::CELFileOpenGroup .text:00506BC0 → xnFileOpenGroup .text:00552980。
- xnFileOpenGroup 從檔案最後 4 bytes 讀 index offset；index 起點依序是解壓後大小、壓縮大小、LZSS 壓縮索引。
- 解壓後索引是 folder_count；每 folder 有 file_count + NUL folder name；每 file 為 12 bytes 數值 + NUL filename。
- FileData::ReadBuf .text:00551810 證實 12 bytes 為 uncompressed_size / compressed_size / data_offset；compressed size=0 直接讀原始 bytes，否則 LZSS 解壓。
- LzssDecompress .text:0055A0E0：4096-byte ring buffer、初始空白、起點 0xFEE；flag bit=1 literal，bit=0 使用 12-bit back-reference，長度 low_nibble+3。
- xnFileRead .text:00552090 先搜尋 _pathList loose 檔，再搜尋 _groupList。因此正式資源解析順序是 loose override > archive base。

tools/legacy_group_archive.py 實作只讀 parser／LZSS／選擇性抽取；tools/materialize_legacy_overlay.py 依原版 precedence 建立私有工作集。

## HK2 真檔

C:\SGO\Stargate Online IE HK2\Data1.dat：
- bytes：585,022,714
- SHA-256：9ae08a8a7bb784720c83bfdd608688c045d717dee3868c13d7b8f444562bfc42
- index offset：584,491,046
- index raw size：1,251,425
- index compressed size：531,656
- entries：41,687

主要 archive 分布：Player 31,717 entries、Map 4,295、MS 1,668、Enemy 1,376、Effect 775、Interface 758、NPC 536、Audio 210、MA 177、SR 173、Item 2。副檔名以 SPR 37,422、SIF 981、STH 980、BLG 901、ACT 444、ITF 140 為主。

這代表 loose 目錄盤點不是完整 Client 虛擬檔案系統；Data1 是大量基底素材來源。

## 真地圖驗證

Old Server Map13 Enemy.dat 有 78 個出生點，只使用 9 種 visual：EN009A/EN010A/EN024A/EN025A/EN029A/EN030A/EN050A/EN058A/EN064A。這些在 HK2 loose Enemy 都沒有實體檔，但 Data1 內各找到 1 ACT + 15 SPR，共 144 files。抽出後 ACT audit 9/9 完整、0 缺圖、0 frame index 越界。

17 張具有 Enemy.dat 的已知舊 Server 地圖合計只用 29 種怪物 visual。對這 29 種建立工作集時得到 464 files：461 archive base、1 loose override、2 loose-only；實際 override 是 Enemy/EN061A.ACT，loose-only 是 EN061A_ND1.spr、EN061A_ND3.spr。29/29 ACT 群組 audit 完整。

## 全安裝目錄 group archive 掃描

新增 `tools/scan_group_archives.py`，對乾淨 HK2 目錄的**全部 41,741 個實體檔**先做 fail-closed footer 預篩：最後 4 bytes 必須能指向合法 index header，且 `index_offset + 8 + compressed_index_size == file_size - 4` 完全成立；只有命中者才交給 `GroupArchive` 做 LZSS index 完整解析與 entry bounds 驗證。這不抽取媒體內容。

2026-11-14 真檔掃描：41,741 files、footer candidates=1、valid archives=1、rejected=0，唯一命中為 `Data1.dat`。因此對**目前提供的 HK2 安裝目錄**，沒有發現第二個採用相同 xnFile group archive 格式的檔案。這個結論不等於證明原始發行安裝包從未有其他 archive；它只界定目前研究工作集的虛擬檔案來源。

可重跑：
```powershell
py tools/scan_group_archives.py 'C:\SGO\Stargate Online IE HK2' reports/hk2-group-archives.json
```

## 尚未完成

HK2 `Client.bin`／`SGOClient.bin` 已用 PE timestamp 排除與 2004 Paradise binary-identical；但安裝包／更新來源 provenance 仍未建立。原版媒體授權／公開發布權也未確認，因此 parser 與 metadata 可進 Git，原始／衍生媒體不可進公開 repo。
