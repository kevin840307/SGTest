extends VBoxContainer
## Renders server-owned quest dialogue; command validation stays at SessionPanel boundary.
const State = preload("res://world/world_state.gd")

var _latest: Dictionary = {}
var active := false
var _open_quest: Callable
var _choose_quest: Callable
var npc_input := SpinBox.new()
var open_button := Button.new()
var title_label := Label.new()
var text_display := TextEdit.new()
var progress_label := Label.new()
var choice_picker := OptionButton.new()
var choose_button := Button.new()
var status_label := Label.new()
var _display_key := ""

func _ready() -> void:
	npc_input.min_value = 1
	npc_input.max_value = 2147483647
	npc_input.step = 1
	npc_input.value = 1
	open_button.text = "開啟附近 NPC 任務"
	title_label.text = "任務：—"
	title_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text_display.editable = false
	text_display.custom_minimum_size = Vector2(350,100)
	text_display.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	progress_label.text = "進度：—"
	choose_button.text = "選擇伺服器提供的選項"
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [npc_input,open_button,title_label,text_display,progress_label,choice_picker,choose_button,status_label]:
		control.visible = false
	for control in [npc_input,open_button,title_label,text_display,progress_label,choice_picker,choose_button,status_label]:
		add_child(control)
	open_button.pressed.connect(_open_quest_from_input)
	choose_button.pressed.connect(_choose_selected_quest_option)

func set_actions(open_action: Callable, choose_action: Callable) -> void:
	_open_quest = open_action
	_choose_quest = choose_action


func reset() -> void:
	_latest = {}
	active = false
	_display_key = ""
	title_label.text = "任務：—"
	text_display.text = ""
	progress_label.text = "進度：—"
	choice_picker.clear()
	status_label.text = ""


func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_refresh()


func _open_quest_from_input()->void:
	if open_button.disabled:
		return
	if _open_quest.is_valid() and _open_quest.call(int(npc_input.value)):
		status_label.text = "任務開啟請求已送出，等待伺服器 View…"
	else:
		status_label.text = "目前無法開啟該 NPC 任務"


func _choose_selected_quest_option()->void:
	if choose_button.disabled or choice_picker.selected<0:
		return
	var choice_id:=int(
		choice_picker.get_item_metadata(choice_picker.selected))
	if _choose_quest.is_valid() and _choose_quest.call(choice_id):
		status_label.text = "任務選項已送出；進度只接受伺服器結果"
	else:
		status_label.text = "目前無法送出該任務選項"


func _quest_state_text(value:int)->String:
	match value:
		0:
			return "未開始"
		1:
			return "進行中"
		2:
			return "已完成"
		_:
			return "未知"


func _quest_status_text(value:int)->String:
	var names:=PackedStringArray([
		"就緒","已套用","重播已套用","地圖／連線世代已變更",
		"找不到任務","距離過遠","無效選項","任務已完成",
		"重複／過期序號","角色已死亡","任務規則版本不符"])
	return names[value] if value>=0 and value<names.size() else "未知結果"


func _refresh()->void:
	var in_world:=active and int(_latest.get("state",-1))==State.STATE_IN_WORLD
	for control in [npc_input,open_button,title_label,text_display,progress_label,choice_picker,choose_button,status_label]:
		control.visible=in_world
	if not in_world:
		_display_key=""
		return
	var pending:=int(_latest.get("pending_quest_sequence",0))!=0
	var usable:=(
		bool(_latest.get("world_ready",false)) and
		not bool(_latest.get("player_dead",false)) and
		not bool(_latest.get("reconnecting",false)) and
		not bool(_latest.get("resync_pending",false)) and
		not bool(_latest.get("waiting_for_new_map",false)) and
		int(_latest.get("pending_world_ready_ack_epoch",0))==0 and
		int(_latest.get("last_world_ready_ack_epoch",0))==
			int(_latest.get("map_epoch",0))
	)
	npc_input.editable=usable and not pending
	open_button.disabled=not usable or pending
	var view:Dictionary=_latest.get("quest_view",{})
	var key:="%d/%d/%d/%d/%d/%d" % [
		int(_latest.get("map_epoch",0)),
		int(_latest.get("last_quest_response_sequence",0)),
		int(_latest.get("last_quest_status",-1)),
		int(view.get("quest_id",0)),int(view.get("progress_revision",0)),
		(view.get("choices",[]) as Array).size()]
	if key!=_display_key:
		_display_key=key
		choice_picker.clear()
		if view.is_empty():
			title_label.text="任務：—"
			text_display.text=""
			progress_label.text="進度：—"
		else:
			title_label.text="任務：%s（ID %d）" % [
				str(view.get("title","")),int(view.get("quest_id",0))]
			text_display.text=str(view.get("text",""))
			progress_label.text="進度：%s｜階段 %d｜Revision %d" % [
				_quest_state_text(int(view.get("state",-1))),
				int(view.get("stage",0)),int(view.get("progress_revision",0))]
			for raw in view.get("choices",[]):
				var choice:Dictionary=raw
				choice_picker.add_item(str(choice.get("label","")))
				choice_picker.set_item_metadata(
					choice_picker.item_count-1,
					int(choice.get("choice_id",0)))
			if choice_picker.item_count>0:
				choice_picker.select(0)
	var result:Dictionary=_latest.get("last_quest_result",{})
	if not result.is_empty():
		status_label.text="伺服器任務結果：%s（代碼 %d）" % [
			_quest_status_text(int(result.get("status",-1))),
			int(result.get("status",-1))]
	choice_picker.disabled=(
		not usable or pending or view.is_empty() or
		choice_picker.item_count==0
	)
	choose_button.disabled=choice_picker.disabled
