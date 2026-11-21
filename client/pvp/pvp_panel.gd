extends VBoxContainer
## PvP mode, target selection and GM controls; rules and settlement remain Server-owned.
const State = preload("res://world/world_state.gd")

const PVP_ATTACK_STATUS_NAMES = [
	"攻擊成功","地圖／連線世代已變更","重複／過期攻擊序號","不可攻擊自己",
	"此地圖禁止玩家 PvP","攻擊者為受保護藍名","目標為受保護藍名","目標不存在",
	"攻擊者已死亡","目標已死亡","距離過遠","障礙物阻擋","攻擊冷卻中",
	"戰鬥狀態不可用","伺服器忙碌，請稍後重試"]

var active := false
var _latest: Dictionary = {}
var _mode_action: Callable
var _attack_action: Callable
var _skill_attack_action: Callable
var _gm_action: Callable
var _skill_id_action: Callable
var _skill_result_names: Array = []
var pvp_query_button := Button.new()
var pvp_blue_button := Button.new()
var pvp_white_button := Button.new()
var pvp_status := Label.new()
var pvp_target_picker := OptionButton.new()
var pvp_attack_button := Button.new()
var pvp_skill_button := Button.new()
var pvp_attack_status := Label.new()
var pvp_skill_status := Label.new()
var gm_pvp_state_picker := OptionButton.new()
var gm_pvp_apply_button := Button.new()
var gm_pvp_status := Label.new()
var _pvp_target_display_key := ""

func _ready() -> void:
	pvp_query_button.text = "查詢 PvP 名稱模式"
	pvp_blue_button.text = "設為藍名（保護）"
	pvp_white_button.text = "設為白名（允許 PvP 地圖）"
	pvp_attack_button.text = "攻擊選中玩家（Server 權威）"
	pvp_skill_button.text = "技能攻擊選中玩家（使用上方技能 ID）"
	gm_pvp_state_picker.add_item("GM：設為藍名",0)
	gm_pvp_state_picker.add_item("GM：設為白名",1)
	gm_pvp_state_picker.add_item("GM：設為紅名",2)
	gm_pvp_state_picker.add_item("GM：設為粉紅名",3)
	gm_pvp_state_picker.add_item("GM：設為閃爍粉紅名",4)
	gm_pvp_apply_button.text = "GM 套用選中玩家名稱狀態"
	pvp_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pvp_attack_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pvp_skill_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [pvp_query_button,pvp_blue_button,pvp_white_button,pvp_status,pvp_target_picker,pvp_attack_button,pvp_skill_button,pvp_attack_status,pvp_skill_status,gm_pvp_state_picker,gm_pvp_apply_button,gm_pvp_status]:
		control.visible = false
		add_child(control)
	pvp_query_button.pressed.connect(func(): _request_pvp_mode(0))
	pvp_blue_button.pressed.connect(func(): _request_pvp_mode(1))
	pvp_white_button.pressed.connect(func(): _request_pvp_mode(2))
	pvp_attack_button.pressed.connect(_attack_selected_player)
	pvp_skill_button.pressed.connect(_skill_attack_selected_player)
	gm_pvp_apply_button.pressed.connect(_gm_apply_selected_player)
	reset()

func set_actions(mode_action: Callable, attack_action: Callable, skill_attack_action: Callable,
		gm_action: Callable, skill_id_action: Callable, skill_result_names: Array) -> void:
	_mode_action = mode_action
	_attack_action = attack_action
	_skill_attack_action = skill_attack_action
	_gm_action = gm_action
	_skill_id_action = skill_id_action
	_skill_result_names = skill_result_names

func reset() -> void:
	active = false
	_latest = {}
	_pvp_target_display_key = ""
	pvp_target_picker.clear()
	pvp_status.text = ""
	pvp_attack_status.text = ""
	pvp_skill_status.text = ""
	gm_pvp_status.text = ""

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_update_pvp_ui()

func _request_pvp_mode(action: int) -> void:
	if _mode_action.call(action):
		pvp_status.text = "PvP 模式請求已送出，等待伺服器權威回覆…"
	else:
		pvp_status.text = "無法送出：請確認角色、地圖及其他 PvP 請求"


func _attack_selected_player() -> void:
	if pvp_attack_button.disabled or pvp_target_picker.selected < 0:
		return
	var target := int(pvp_target_picker.get_item_metadata(pvp_target_picker.selected))
	if _attack_action.call(target):
		pvp_attack_status.text = "PvP 攻擊已送出，等待伺服器判定距離、保護狀態與傷害…"
		pvp_attack_button.disabled = true
	else:
		pvp_attack_status.text = "目前無法攻擊該玩家"


func _skill_attack_selected_player() -> void:
	if pvp_skill_button.disabled or pvp_target_picker.selected<0:
		return
	var target:=int(pvp_target_picker.get_item_metadata(pvp_target_picker.selected))
	var skill_id:=int(_skill_id_action.call())
	if _skill_attack_action.call(target,skill_id):
		pvp_skill_status.text="玩家技能已送出，等待伺服器判定規則、MP、距離與傷害…"
		pvp_skill_button.disabled=true
	else:
		pvp_skill_status.text="目前無法對該玩家施放技能"


func _gm_apply_selected_player()->void:
	if (
		gm_pvp_apply_button.disabled or pvp_target_picker.selected<0 or
		gm_pvp_state_picker.selected<0
	):
		return
	var target:=int(pvp_target_picker.get_item_metadata(pvp_target_picker.selected))
	var state:=int(gm_pvp_state_picker.get_item_id(gm_pvp_state_picker.selected))
	if _gm_action.call(target,state):
		gm_pvp_status.text="GM 指令已送出；等待伺服器 audit commit 與 AOI 名稱更新…"
		gm_pvp_apply_button.disabled=true
	else:
		gm_pvp_status.text="GM 指令未送出；權限、目標或連線狀態無效"

func _pvp_attack_status_text(code: int) -> String:
	return PVP_ATTACK_STATUS_NAMES[code] if code >= 0 and code < PVP_ATTACK_STATUS_NAMES.size() else "未知 PvP 結果"


func _update_pvp_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [pvp_query_button,pvp_blue_button,pvp_white_button,pvp_status,pvp_target_picker,pvp_attack_button,pvp_skill_button,pvp_attack_status,pvp_skill_status]:
		control.visible = in_world
	var gm_visible:=in_world and bool(_latest.get("gm_authorized",false))
	for control in [gm_pvp_state_picker,gm_pvp_apply_button,gm_pvp_status]:
		control.visible=gm_visible
	if not in_world:
		_pvp_target_display_key = ""
		return
	var ready := bool(_latest.get("world_ready",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))
	var pending := int(_latest.get("pending_pvp_sequence",0)) != 0
	var known := bool(_latest.get("pvp_known",false))
	var dead := bool(_latest.get("player_dead",false))
	var map_allows := bool(_latest.get("pvp_map_allows",false))
	pvp_query_button.disabled = not ready or pending
	pvp_blue_button.disabled = not ready or pending or not known or dead or int(_latest.get("pvp_mode",-1)) == 0
	pvp_white_button.disabled = not ready or pending or not known or dead or not map_allows or int(_latest.get("pvp_mode",-1)) == 1
	if pending:
		pvp_status.text = "等待伺服器 PvP 模式確認；不可重複送出"
	elif known:
		var mode := "白名（PvP 開啟）" if int(_latest.get("pvp_mode",-1)) == 1 else "藍名（保護）"
		var map_text := "此圖允許玩家 PvP" if map_allows else "此圖禁止玩家 PvP"
		var result: Dictionary = _latest.get("last_pvp_result",{})
		pvp_status.text = "%s｜%s｜伺服器結果 %d" % [mode,map_text,int(result.get("status",-1))]
	else:
		pvp_status.text = "PvP 模式尚未查詢；名稱顏色由伺服器判定"

	var entity_ids: Array = (_latest.get("entities",{}) as Dictionary).keys()
	entity_ids.sort()
	var identities:Dictionary=_latest.get("player_identities",{})
	var target_key := "%d/%d/%d" % [int(_latest.get("actor_id",0)),int(_latest.get("map_epoch",0)),entity_ids.size()]
	for raw_actor in entity_ids:
		var target_id:=int(raw_actor)
		var identity:Dictionary=identities.get(target_id,{})
		target_key += ",%d:%d:%s" % [
			target_id,int(identity.get("pvp_updated_unix_ms",0)),
			str(identity.get("display_name",""))]
	if target_key != _pvp_target_display_key:
		_pvp_target_display_key = target_key
		pvp_target_picker.clear()
		for raw_actor in entity_ids:
			var target_id := int(raw_actor)
			if target_id <= 0 or target_id == int(_latest.get("actor_id",0)):
				continue
			var target_identity:Dictionary=identities.get(target_id,{})
			var target_name:=str(target_identity.get("display_name","")).strip_edges()
			pvp_target_picker.add_item(
				("%s（%d）" % [target_name,target_id])
				if not target_name.is_empty() else "可見玩家 %d" % target_id)
			pvp_target_picker.set_item_metadata(pvp_target_picker.item_count-1,target_id)
		if pvp_target_picker.item_count > 0:
			pvp_target_picker.select(0)

	var gm_pending:=int(_latest.get("pending_gm_sequence",0))!=0
	gm_pvp_apply_button.disabled=not gm_visible or not ready or gm_pending or pvp_target_picker.item_count==0
	if gm_visible:
		if gm_pending:
			gm_pvp_status.text="等待伺服器 GM audit commit；不可重複送出"
		else:
			var gm_result:Dictionary=_latest.get("last_gm_result",{})
			if gm_result.is_empty():
				gm_pvp_status.text="GM 權限由伺服器簽發；選擇 AOI 玩家與名稱狀態"
			else:
				var gm_code:=int(gm_result.get("status",-1))
				var gm_text: String = (["已套用","重播：未重複修改","無權限","世代已變更","過期序號","目標已離線"][gm_code] as String) if gm_code>=0 and gm_code<=5 else "未知結果"
				gm_pvp_status.text="%s（代碼 %d）；名稱顯示仍以 Server AOI identity 為準" % [gm_text,gm_code]

	var attack_pending := int(_latest.get("pending_pvp_attack_sequence",0)) != 0
	pvp_attack_button.disabled = not ready or not known or not map_allows or dead or not bool(_latest.get("combat_available",false)) or attack_pending or pvp_target_picker.item_count == 0
	var skill_pending:=int(_latest.get("pending_skill_sequence",0))!=0
	pvp_skill_button.disabled=not ready or not known or not map_allows or dead or not bool(_latest.get("combat_available",false)) or skill_pending or pvp_target_picker.item_count==0 or int(_skill_id_action.call())<=0
	var attack_result: Dictionary = _latest.get("last_pvp_attack_result",{})
	if attack_pending:
		pvp_attack_status.text = "等待伺服器 PvP 攻擊結算；不可重複攻擊"
	elif not attack_result.is_empty():
		var code := int(attack_result.get("status",-1))
		if code == 0:
			pvp_attack_status.text = "%s｜傷害 %d%s" % [_pvp_attack_status_text(code),int(attack_result.get("damage",0)),"｜目標死亡" if bool(attack_result.get("died",false)) else ""]
		else:
			pvp_attack_status.text = "%s（代碼 %d）" % [_pvp_attack_status_text(code),code]
	elif not map_allows:
		pvp_attack_status.text = "此地圖禁止玩家 PvP"
	else:
		pvp_attack_status.text = "選擇 AOI 內玩家後可送出攻擊；傷害與罪名由伺服器判定"

	var skill_result:Dictionary=_latest.get("last_skill_result",{})
	if skill_pending and int(_latest.get("pending_skill_target_kind",0))==1:
		pvp_skill_status.text="等待伺服器玩家技能結算；不可重複施放"
	elif str(skill_result.get("target_kind",""))=="player":
		var code:=int(_latest.get("last_skill_result_status",-1))
		pvp_skill_status.text=("%s｜傷害 %d%s" % [
			_skill_result_names[code] if code>=0 and code<_skill_result_names.size() else "玩家技能結果",
			int(skill_result.get("damage",0)),
			"｜目標死亡" if bool(skill_result.get("died",false)) else ""]) if code==0 else "玩家技能結果（代碼 %d）" % code
	elif not map_allows:
		pvp_skill_status.text="此地圖禁止玩家 PvP"
	else:
		pvp_skill_status.text="使用既有技能 ID 對 AOI 玩家送出；MP／傷害／罪名全由伺服器判定"
