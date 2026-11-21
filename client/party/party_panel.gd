extends VBoxContainer
## Party invitation, membership and team chat UI; Server remains party authority.
const State = preload("res://world/world_state.gd")

var session
var active := false
var _latest: Dictionary = {}
var target_input := LineEdit.new()
var invite_button := Button.new()
var invite_label := Label.new()
var accept_button := Button.new()
var reject_button := Button.new()
var members_label := Label.new()
var leave_button := Button.new()
var kick_button := Button.new()
var promote_button := Button.new()
var chat_input := LineEdit.new()
var chat_send_button := Button.new()
var status_label := Label.new()

func _ready() -> void:
	target_input.placeholder_text = "隊伍目標角色名或 ActorId（線上玩家）"
	invite_button.text = "邀請加入隊伍"
	invite_label.text = "待處理邀請：—"
	invite_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	accept_button.text = "接受隊伍邀請"
	reject_button.text = "拒絕隊伍邀請"
	members_label.text = "隊伍：—"
	members_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	leave_button.text = "離開隊伍"
	kick_button.text = "踢除目標"
	promote_button.text = "移交隊長給目標"
	chat_input.placeholder_text = "隊伍聊天（可跨地圖）"
	chat_input.max_length = 256
	chat_send_button.text = "隊伍送出"
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [target_input,invite_button,invite_label,accept_button,reject_button,members_label,leave_button,kick_button,promote_button,chat_input,chat_send_button,status_label]:
		add_child(control)
	invite_button.pressed.connect(_party_invite_target)
	accept_button.pressed.connect(_party_accept_invite)
	reject_button.pressed.connect(_party_reject_invite)
	leave_button.pressed.connect(_party_leave)
	kick_button.pressed.connect(_party_kick_target)
	promote_button.pressed.connect(_party_promote_target)
	chat_send_button.pressed.connect(_send_party_chat)
	chat_input.text_submitted.connect(func(_text): _send_party_chat())

func reset() -> void:
	_latest = {}
	active = false
	target_input.clear()
	invite_label.text = "待處理邀請：—"
	members_label.text = "隊伍：—"
	chat_input.clear()
	status_label.text = ""

func apply_snapshot(snapshot: Dictionary, connection_active: bool, session_ref) -> void:
	_latest = snapshot
	active = connection_active
	session = session_ref
	_refresh()

func _party_target_actor()->int:
	var raw:=target_input.text.strip_edges()
	return int(raw) if raw.is_valid_int() else 0


func _party_invite_target()->void:
	var raw:=target_input.text.strip_edges()
	if raw.is_empty():
		status_label.text="請輸入線上角色名稱或 ActorId"
		return
	var sent:=false
	if raw.is_valid_int():
		sent=session.party_invite(int(raw))
	else:
		sent=session.party_invite_name(raw)
	if sent:
		status_label.text="隊伍邀請已送出，等待伺服器解析目前在線角色…"
	else:
		status_label.text="目前無法送出隊伍邀請"


func _party_accept_invite()->void:
	if session.party_accept():
		status_label.text="已送出接受邀請；等待伺服器 PartySnapshot"
	else:
		status_label.text="目前沒有可接受的隊伍邀請"


func _party_reject_invite()->void:
	if session.party_reject():
		status_label.text="已送出拒絕邀請"
	else:
		status_label.text="目前沒有可拒絕的隊伍邀請"


func _party_leave()->void:
	if session.party_leave():
		status_label.text="離隊請求已送出"
	else:
		status_label.text="目前不在隊伍內"


func _party_kick_target()->void:
	var target:=_party_target_actor()
	if target>0 and session.party_kick(target):
		status_label.text="踢除請求已送出"
	else:
		status_label.text="只有隊長可踢除目前隊員"


func _party_promote_target()->void:
	var target:=_party_target_actor()
	if target>0 and session.party_promote(target):
		status_label.text="隊長移交請求已送出"
	else:
		status_label.text="只有隊長可將隊長移交給目前隊員"


func _send_party_chat()->void:
	if chat_send_button.disabled:
		return
	var message:=chat_input.text.strip_edges()
	if message.is_empty():
		return
	if session.send_party_chat(message,0):
		chat_input.clear()
		status_label.text="隊伍訊息已送出，等待伺服器可靠 echo…"
	else:
		status_label.text="目前無法送出隊伍聊天"


func _party_result_text(status_code:int)->String:
	var names:=PackedStringArray([
		"成功","目標不在線","不能選自己","目標已在隊伍","目前不在隊伍",
		"只有隊長可操作","隊伍已滿","找不到邀請","邀請已失效",
		"目標不是隊員","目標已有待處理邀請"])
	return names[status_code] if status_code>=0 and status_code<names.size() else "未知結果"


func _refresh() -> void:
	var in_world:=active and int(_latest.get("state",-1))==State.STATE_IN_WORLD
	for control in [target_input,invite_button,invite_label,accept_button,reject_button,members_label,leave_button,kick_button,promote_button,chat_input,chat_send_button,status_label]:
		control.visible = in_world
	if not in_world:
		return
	var usable:=bool(_latest.get("world_ready",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0))==0 and int(_latest.get("last_world_ready_ack_epoch",0))==int(_latest.get("map_epoch",0))
	var whisper_pending:=int(_latest.get("pending_whisper_sequence",0))!=0
	var party_pending:=int(_latest.get("pending_party_sequence",0))!=0
	var current_party:=int(_latest.get("party_id",0))
	var is_party_leader:=current_party>0 and int(_latest.get("party_leader_actor_id",0))==int(_latest.get("actor_id",0))
	target_input.editable=usable and not party_pending
	invite_button.disabled=not usable or party_pending or (current_party>0 and not is_party_leader)
	var invite:Dictionary=_latest.get("pending_party_invite",{})
	accept_button.disabled=not usable or party_pending or invite.is_empty() or current_party>0
	reject_button.disabled=not usable or party_pending or invite.is_empty() or current_party>0
	leave_button.disabled=not usable or party_pending or current_party<=0
	kick_button.disabled=not usable or party_pending or not is_party_leader
	promote_button.disabled=not usable or party_pending or not is_party_leader
	chat_input.editable=usable and not whisper_pending and current_party>0
	chat_send_button.disabled=not usable or whisper_pending or current_party<=0
	if invite.is_empty():
		invite_label.text="待處理邀請：—"
	else:
		invite_label.text="待處理邀請：%s（Actor %d｜Party %d）" % [
			str(invite.get("inviter_name","")),
			int(invite.get("inviter_actor_id",0)),
			int(invite.get("party_id",0))]
	var party_members:Dictionary=_latest.get("party_members",{})
	if current_party<=0:
		members_label.text="隊伍：—"
	else:
		var ids:=party_members.keys()
		ids.sort()
		var member_rows:=PackedStringArray()
		for id in ids:
			var member:Dictionary=party_members[id]
			var prefix:="★ " if int(id)==int(_latest.get("party_leader_actor_id",0)) else "・ "
			member_rows.append("%s%s（Actor %d）" % [
				prefix,str(member.get("name","玩家")),int(id)])
		members_label.text="隊伍 %d｜Rev %d\n%s" % [
			current_party,int(_latest.get("party_revision",0)),
			"\n".join(member_rows)]
	var party_result:Dictionary=_latest.get("last_party_result",{})
	if not party_result.is_empty() and not party_pending:
		status_label.text="隊伍操作：%s" % _party_result_text(
			int(party_result.get("status",-1)))
