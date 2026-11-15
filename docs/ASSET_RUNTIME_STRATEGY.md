# Runtime Asset Strategy

日期：2026-11-15
狀態：**ACCEPTED STRUCTURE / PACKING MODE PENDING FINAL USER CHOICE**

本文件是 Runtime 資產邊界的正式規則。重點不是減少磁碟檔案數，而是降低執行期 RAM / VRAM、載入延遲、draw call、CPU/GPU 成本，並讓 AFK 可以可靠釋放世界渲染資源。

## 1. 每個素材必須保持獨立 identity

正式 Runtime 不允許為了方便或減少檔案數，將不同素材綁成一個生命週期資產。

正確：

```text
Enemy/en023a/atlas.json
Enemy/en089a/atlas.json
npcs/npc001/atlas.json
maps/visuals/tree001/atlas.json
maps/visuals/house014/atlas.json
players/b/base/body/atlas.json
players/b/base/head/atlas.json
equipment/b/weapon/sword014/atlas.json
```

不允許：

```text
bundles/map/13/sprites.json       # map + monster + scenery
bundles/session/player-b-base     # body/head/arm/death 合成一份 physical asset
all_monsters.pck                  # 若它被當成共同載入生命週期
```

同一素材內可以有多 page，也可以將它自己的多動作／多方向影格重新 packing；**不能跨 monster / NPC / scenery / player part 合圖**。

## 2. PCK 只是磁碟容器，不是 Runtime Bundle

Windows / macOS 共用：

```text
assets/assets.pck
```

PCK 可以包含很多獨立檔案，這不表示 Runtime 會一起載入。

```text
external PCK
    ↓ mount
res://assets/...
    ↓
AssetCatalog
    ↓
AssetStore
    ↓
只 decode / upload 目前需要的 individual assets
```

磁碟檔多可以接受；不能為了減少檔案數把 Runtime ownership 綁在一起。

## 3. Map 與 Monster 完全分離

`assets/maps/<id>/` 只包含地圖本身：

```text
maps/13/
├── world.json
├── terrain.png
├── tiles.bin
└── blocking.dat
```

`world.json` 的 scenery object 只參考：

```text
maps/visuals/<asset>/atlas.json
```

Map 不包含 Monster image。

Monster 出生與 appearance 由 Server population / entity definition 決定：

```text
definition.sprite = EN023A
definition.atlas  = Enemy/en023a/atlas.json
```

同一 monster 可以跨多張地圖共用，不因 map 重包。

## 4. Collision 使用原版 Blocking.dat

碰撞不建立大量 Godot `CollisionShape2D`。

原版：

```text
Blocking.dat
uint32 width
uint32 height
byte[width*height]
```

Runtime 保留原 byte grid 為：

```text
maps/<id>/blocking.dat
```

Client / Server 都用 cell index O(1) 查詢；目前 walkability 規則維持原驗證邏輯 `(cell & 7) == 0`。

優點：

- 不建立數十萬 Node / Shape。
- Server / Client 可使用相同 source hash。
- 原始碰撞不因 Godot scene 轉換產生誤差。
- 路徑、預測與權威碰撞容易對帳。

## 5. Runtime Loading

仍然只有一套 `AssetStore`：

- bounded async decode
- bounded GPU commit
- refcount / owner epoch
- LRU eviction
- cache byte limit
- cancel stale load
- AFK / map transition resource release

不因素材獨立而建立：

- MonsterLoader
- NPCLoader
- MapObjectLoader
- EquipmentLoader

Renderer 可以不同，但實體資源載入機制共用。

## 6. AFK

AFK：

```text
Network               ON
Reducer / WorldState  ON
Reliable events       ON
World rendering       OFF
World textures        可釋放
External PCK          保持掛載
```

PCK 掛載本身不代表 Texture 常駐；真正 RAM/VRAM 成本由 AssetStore loaded entries 決定。

## 7. Packing Mode 公平測試規則

任何 Atlas Mode 比較必須：

1. 使用**完全相同的 monster IDs / source SPR**。
2. Map asset 固定且與 monster asset 分開。
3. 每一個 Mode 使用獨立 Godot process。
4. 不把 Map + Monster / Monster + NPC 合成後再宣稱是 monster packing 結果。
5. 同一實例數、相同 action/facing deterministic workload。
6. 至少重複三次，以中位數比較。
7. 報告分開列 map cost 與 monster cost。

2026-11-15 發現舊 Mode4 12-kind benchmark 不符合第1點：Mode1/2/3 root 只有固定12隻，但舊 Mode4 root 有更多怪，舊腳本排序後取前12隻，因此舊的 Mode4 12-kind 優勢數字 **INVALID FOR CROSS-MODE COMPARISON**。

新版 benchmark：

```text
Map13 + 2 kinds  x 1000 monsters
Map13 + 12 kinds x 1000 monsters
4 modes
3 independent repeats
repeat 2 反轉 mode 順序
```

Map13 terrain/blocking/scenery 固定；monster root 才依 Mode 改變。每個 trial 都驗證 Map/Monster asset root 分離。

完整私有結果：

```text
local-assets/four-mode-benchmark/map_separated_trials/summary.json
```

目前結果顯示 Mode1 Dense 在這組固定怪物上具有最低或接近最低的 Runtime texture footprint、低 page count 與較低 load cost；最新版 Adaptive Atomic 並未在這組大型怪物上勝過 Dense。因此正式 packing mode **不得再以舊 benchmark 直接指定 Mode4**，需依這次公平結果重新決策。

## 8. 目前實際驗證

全量來源：

- 75,588 SPR
- 7,890 independent sprite groups
- 7,890 unique logical asset IDs
- 0 logical path collision
- 0 export error
- 8,094 atlas pages
- 282,968 frames

Map13 runtime contract：

- 367 placed scenery objects
- 45 independent scenery visual assets
- 9 monster definitions
- 9 independent monster assets
- 7 independent Player-B base parts
- cross-asset bundle = false
- collision = original Blocking byte grid

## 9. 後續規則

- 新增怪物：只新增／更新該 monster asset。
- 地圖換怪：只改 Server population / definition，不重包 map。
- scenery 修圖：只更新該 scenery asset。
- Player 換裝：body / hair / equipment layer 分離。
- 磁碟檔案數不是 optimization target。
- RAM / VRAM / load latency / draw calls / frame P95/P99 / AFK release 才是 Runtime optimization target。
