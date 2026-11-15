# Runtime Asset Bundle Architecture — SUPERSEDED

日期：2026-11-15
狀態：**SUPERSEDED / DO NOT IMPLEMENT**

這份文件原本主張把 Map13 的 Monster + scenery 合成 map lifecycle bundle，並把 Player base parts 合成 session bundle。

2026-11-15 重新檢查原版素材 identity、Runtime 維護需求與四種 packing 公平 benchmark 後，此政策已被撤銷。

新的正式 source of truth：

- [Asset Architecture](ASSET_ARCHITECTURE.md)
- [Asset Packing Benchmark](ASSET_PACKING_BENCHMARK.md)
- [Client Architecture](CLIENT_ARCHITECTURE.md)
- ADR-040（DECISIONS.md）

## 不再允許的 production 結構

```text
assets/bundles/map/<id>/sprites.json
assets/bundles/session/player-*.json
Map + Monster shared atlas
different Monsters shared atlas
different scenery source assets shared atlas
Player body/head/leg/arms/death shared physical atlas
```

## 現行規則

- 每個素材保持獨立 logical/physical asset identity。
- 同一 asset 內 action/facing/frame 可重新 packing。
- 同 visual 的很多 instance 共用同一 AssetStore entry。
- Map 只引用 scenery；Monster 由 Server population definition 指向 `monsters/<id>/atlas.json`。
- Player parts 可共用 owner lifetime，但不 physical merge。
- 磁碟檔案多可以接受；Runtime working set 才是主要最佳化目標。
- external `assets.pck` 是檔案容器，不是 lifecycle bundle。

舊文件中的 benchmark 數字只保留在 Git history 作歷史研究，不得再當作 production architecture 證據。
