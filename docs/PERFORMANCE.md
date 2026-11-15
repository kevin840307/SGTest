# SGTest — 新版效能與體感基準

**狀態：驗收指標，非實測 PASS。** 首要目標是玩家看到更流暢的移動、戰鬥與方向，並讓 RAM／Texture／CPU 維持合理負擔。

## 比較原則

Recovered Model1、SGTest 新方案必須以**對應原生 Renderer、同素材、同觀察對象、同地圖、相同解析度／縮放／VSync、相同 AI／AOI 工作負載**比較。不要把簡化 Sprite 測試當完整 Client FPS。

記錄：
- 使用者輸入到下一次可見畫面變化的延遲、移動位置誤差與外圍順移；
- Frame P50 / P95 / P99 / Max、>16.7ms 長幀數、CPU frame、GPU 渲染開銷與 Draw calls；
- Windows process-tree Peak RSS、Godot Texture / Video memory 指標、GPU 物理 dedicated VRAM（測不到就填 N/A）；
- 首次／快取後 Map/Entity 載入、換圖與登出／AFK 資源回收；
- Server World Tick P95/P99、網路 inbound/outbound queue age、可靠事件延遲。

## 歷史參考（不是新版 Gate）

原型 benchmark曾實測：
- Recovered Model1 在 **12 種 Enemy ×2000** 隔離比較的 P95 6.576ms、Peak RSS 219.8MiB；
- 舊 SGTest Height-P0 Compressed + Sprite：P95 5.602ms、Peak RSS 178.7MiB；
- Map13 混合視覺負載 **12 種×2000**：Recovered P95 7.470ms/P99 10.361ms/RSS 219.8MiB，舊 SGTest Sprite P95 6.900ms/P99 7.728ms/RSS 176.6MiB。

這些數值是過往 isolated 渲染／假人／假掉落測試，沒有完整多人伺服器、完整人物紙娃娃或 Windows/macOS release。原始方法與數據見 [research/ASSET_RENDERER_EVIDENCE](research/ASSET_RENDERER_EVIDENCE.md)。

## 新版 Gate（先建立基準，再做優化）

| 場景 | 驗收重點 |
|---|---|
| Idle、走路、攻擊／受擊 | 動畫播放速率固定、方向與多部位同步、P95/P99 |
| 20/100 人 + 300/1000 Enemy + 多掉落 | input-to-render、外圍順移、CPU/GPU/網路負載 |
| 30/45/60/90FPS 與 Render 0 | Client reducer、事件順序、Server State 相同 |
| AFK/Unfocus、縮小／還原、登出 | 30 次循環，Texture／Node／RSS 不持續上升 |
| 1280×720 與多解析度 | AOI、UI、camera、原圖像素品質，無縮放 artifacts |
| Windows/macOS Release | PCK remap、Texture 規格、完整可執行流程 |

做決策時先排除畫面錯誤、事件遺失與過量資源，**接著優先使用者感知的 P95/P99 與動作平順性**；不為了單次合圖 0.x ms 增加複雜分桶。

不要降低 Server Tick、Reliable events、Client reducer 來換取假 FPS。