# Runtime wire v2（2026-11-14）

.NET 8 ASP.NET Core WebSocket `/game`，每個 binary message 為 MessagePack `[2, opcode, bodyArray]`。只接受 array、signed integer、finite real、UTF-8 string、binary、bool、nil；無型別反射／物件還原。上限 64 KiB、array 4096、巢狀 8。Server command queue 8192、每 session outbound 512、Client outbound 256／inbound 4096。超載會關閉連線，不能靜默丟可靠事件。

|方向／op|body|
|---|---|
|S→C 0 Hello|peerId|
|S→C 1 World|mapId, contentFingerprint, populationSha256|
|S→C 2 Owner|tick, serverTime, acknowledgedInput, lifeEpoch, x, y, hp, facing, pose, actionStarted, revision|
|S→C 3 Snapshot|tick, serverTime, entityKind, packedRows|
|S→C 4 Action|kind, id, facing, pose, started, hp, revision, x, y, tick, definitionId|
|S→C 5 Ground|addedRows, removedIds|
|S→C 6 Inventory|[[itemId,count], ...]|
|S→C 7 InterestRemove|tick, kind, ids|
|S→C 8 Pong|clientSentTime, serverTime|
|C→S 20 Input|lifeEpoch, packedInput|
|C→S 21 Attack|monsterId|
|C→S 22 Pickup|dropId|
|C→S 23 Ping|clientSentTime|
|C→S 24 WorldReady|mapId, contentFingerprint, populationSha256|

kind 0=player、1=monster；pose 0=idle、1=walk、2=attack、3=hit、4=death。packedRows 每列 28 bytes little endian：id:u32、x:f32、y:f32、hp:u16、facing:u8、pose:u8、actionStarted:f32、revision:u32、definitionId:u32。packedInput 每列 12 bytes little endian：sequence:u32、x:f32、y:f32；每包最多三筆冗餘輸入。每 tick 最多消耗一筆，最多前推 64 序號，死亡／復活切換 lifeEpoch。權威 revision 與生命版本使用 uint32 wrap 比較。

Server 等 Client 確認相同地圖指紋才發送世界狀態；本地預測位置不參與 Server 攻擊／碰撞／拾取。動作與快照都經每實體 revision 邊界，舊 Move 不得復活死亡角色。兩種語言以 `server/tests/Program.cs` 產生 fixture，`client/tests/test_wire.gd` 逐位元驗證編碼。

v2 追加怪物定義與出生資料指紋；v1 及 master 的 Godot high-level ENet RPC，舊 Client 不能直連新 Server。每包最多28列世界快照；每個怪物有明確definitionId，出生／動作事件也保留該ID。`reports/impaired_*.json` 是舊協定歷史，不是目前 TCP／WebSocket 的丟包證據；目前尚缺新協定的跨網路延遲／抖動與重連測試。
