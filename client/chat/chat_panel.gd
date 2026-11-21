extends VBoxContainer
## Owns nearby/whisper controls and the read-only chat history (including party messages).
const State = preload("res://world/world_state.gd")

var session
var active := false
var _latest: Dictionary = {}
var history := TextEdit.new()
var nearby_input := LineEdit.new()
var nearby_send_button := Button.new()
var nearby_status := Label.new()
var whisper_target_input := LineEdit.new()
var whisper_input := LineEdit.new()
var whisper_send_button := Button.new()
var whisper_status := Label.new()
var _display_key := ""

func _ready() -> void:
	history.editable = false
	history.custom_minimum_size = Vector2(350,120)
	history.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	nearby_input.placeholder_text = "附近聊天（Server AOI 可見玩家）"
	nearby_input.max_length = 256
	nearby_send_button.text = "附近送出"
	nearby_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	whisper_target_input.placeholder_text = "私訊目標角色名稱或 ActorId（線上玩家，可跨地圖）"
	whisper_input.placeholder_text = "私訊內容"
	whisper_input.max_length = 256
	whisper_send_button.text = "私訊送出"
	whisper_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [history,nearby_input,nearby_send_button,nearby_status,whisper_target_input,whisper_input,whisper_send_button,whisper_status]:
		add_child(control)
	nearby_send_button.pressed.connect(_send_nearby_chat)
	nearby_input.text_submitted.connect(func(_text): _send_nearby_chat())
	whisper_send_button.pressed.connect(_send_whisper)
	whisper_input.text_submitted.connect(func(_text): _send_whisper())

func reset() -> void:
	_latest = {}
	active = false
	_display_key = ""
	history.text = ""
	nearby_input.clear()
	nearby_status.text = ""
	whisper_target_input.clear()
	whisper_input.clear()
	whisper_status.text = ""

func apply_snapshot(snapshot: Dictionary, connection_active: bool, session_ref) -> void:
	_latest = snapshot
	active = connection_active
	session = session_ref
	_refresh()

func _send_nearby_chat() -> void:
	if nearby_send_button.disabled:
		return
	var message:=nearby_input.text.strip_edges()
	if message.is_empty():
		return
	if session.send_nearby_chat(message,0):
		nearby_input.clear()
		nearby_status.text = "已送出，等待伺服器 nearby echo…"
	else:
		nearby_status.text = "目前無法送出附近聊天"


func _send_whisper() -> void:
	if whisper_send_button.disabled:
		return
	var target_text:=whisper_target_input.text.strip_edges()
	var message:=whisper_input.text.strip_edges()
	if target_text.is_empty() or message.is_empty():
		whisper_status.text = "請輸入線上角色名稱或 ActorId 與私訊內容"
		return
	var sent: bool = session.send_whisper(int(target_text),message,0) if target_text.is_valid_int() else session.send_whisper_name(target_text,message,0)
	if sent:
		whisper_input.clear()
		whisper_status.text = "私訊已送出，等待伺服器解析目前在線角色…"
		whisper_send_button.disabled = true
	else:
		whisper_status.text = "目前無法送出私訊"


func _refresh() -> void:
	var in_world:=active and int(_latest.get("state",-1))==State.STATE_IN_WORLD
	for control in [history,nearby_input,nearby_send_button,nearby_status,whisper_target_input,whisper_input,whisper_send_button,whisper_status]:
		control.visible = in_world
	if not in_world:
		_display_key=""
		return
	var usable:=bool(_latest.get("world_ready",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0))==0 and int(_latest.get("last_world_ready_ack_epoch",0))==int(_latest.get("map_epoch",0))
	var whisper_pending:=int(_latest.get("pending_whisper_sequence",0))!=0
	nearby_input.editable=usable and not whisper_pending
	nearby_send_button.disabled=not usable or whisper_pending
	whisper_target_input.editable=usable and not whisper_pending
	whisper_input.editable=usable and not whisper_pending
	whisper_send_button.disabled=not usable or whisper_pending
	var messages:Array=_latest.get("chat_messages",[])
	var tail:Dictionary={} if messages.is_empty() else messages[messages.size()-1]
	var key:="%d/%d/%d/%d/%s/%s/%d/%d/%d/%d/%d" % [
		messages.size(),int(tail.get("sender_actor_id",0)),
		int(tail.get("sender_connection_epoch",0)),
		int(tail.get("chat_sequence",0)),str(tail.get("message","")),
		str(tail.get("direction","")),
		int(_latest.get("last_whisper_result_sequence",0)),
		int(_latest.get("last_whisper_result_status",-1)),
		int(_latest.get("party_id",0)),int(_latest.get("party_revision",0)),
		int(_latest.get("last_party_result_sequence",0))]
	if key!=_display_key:
		_display_key=key
		var rows:=PackedStringArray()
		for value in messages:
			var entry:Dictionary=value
			var channel:=int(entry.get("channel",-1))
			if channel==0:
				rows.append("[附近] %s：%s" % [
					str(entry.get("sender_name","玩家 %d" % int(entry.get("sender_actor_id",0)))),
					str(entry.get("message",""))])
			elif channel==3:
				rows.append("[隊伍] %s：%s" % [
					str(entry.get("sender_name","玩家 %d" % int(entry.get("sender_actor_id",0)))),
					str(entry.get("message",""))])
			elif channel==4 and str(entry.get("direction",""))=="outgoing":
				rows.append("[私訊] 你 → %s：%s" % [
					str(entry.get("target_name","玩家 %d" % int(entry.get("target_actor_id",0)))),
					str(entry.get("message",""))])
			elif channel==4:
				rows.append("[私訊] %s → 你：%s" % [
					str(entry.get("sender_name","玩家 %d" % int(entry.get("sender_actor_id",0)))),
					str(entry.get("message",""))])
		history.text="\n".join(rows)
		if not messages.is_empty():
			history.set_v_scroll(history.get_line_count())
		nearby_status.text="聊天訊息均由伺服器可靠投遞｜已顯示 %d 則" % messages.size()
		var whisper_result:Dictionary=_latest.get("last_whisper_result",{})
		if not whisper_result.is_empty():
			match int(whisper_result.get("status",-1)):
				0:
					whisper_status.text="私訊已送達 %s（Actor %d）" % [
						str(whisper_result.get("target_name","")),
						int(whisper_result.get("target_actor_id",0))]
				1:
					whisper_status.text="私訊失敗：目標目前不在線或連線已變更"
				2:
					whisper_status.text="私訊失敗：不能私訊自己"
				_:
					whisper_status.text="私訊結果未知"
