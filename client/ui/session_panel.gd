extends PanelContainer
## UI adapter only. ClientSession owns transport/reduction on its own threads.
signal connection_changed(active: bool)
signal world_ready(snapshot: Dictionary)
signal world_snapshot(snapshot: Dictionary)

const Session = preload("res://network/client_session.gd")
const State = preload("res://network/world_state.gd")
const ItemCatalog = preload("res://ui/item_catalog.gd")
var session = Session.new()
var item_catalog = ItemCatalog.new()
var active := false
var trusted_chain: X509Certificate
var tls_name := ""
var host_input := LineEdit.new()
var port_input := SpinBox.new()
var username_input := LineEdit.new()
var password_input := LineEdit.new()
var login_button := Button.new()
var logout_button := Button.new()
var characters := OptionButton.new()
var choose_button := Button.new()
var respawn_button := Button.new()
var skill_hint := Label.new()
var skill_id_input := SpinBox.new()
var skill_target_mode := CheckButton.new()
var skill_area_button := Button.new()
var skill_status := Label.new()
var pickup_status := Label.new()
var inventory_button := Button.new()
var inventory_preview := TextEdit.new()
var inventory_search := LineEdit.new()
var inventory_previous := Button.new()
var inventory_next := Button.new()
var inventory_page_label := Label.new()
var recovery_picker := OptionButton.new()
var recovery_button := Button.new()
var recovery_status := Label.new()
var discard_button := Button.new()
var discard_quantity := SpinBox.new()
var discard_all_button := Button.new()
var discard_confirm := ConfirmationDialog.new()
var discard_status := Label.new()
const INVENTORY_PAGE_SIZE := 80
var _inventory_page := 0
var _inventory_display_key := ""
var _pending_discard_confirmation := {}
var status := Label.new()
var _characters: Array = []
var _chosen := false
var _world_epoch := 0
var _poll_elapsed := 0.0
var _latest := {}
var _respawn_queued_after := -1
var _skill_queued_after := -1
var _skill_queued_at_ms := 0
const SKILL_RESULT_NAMES = ["成功","未命中","目標不存在","目標死亡","超出距離","障礙阻擋","冷卻中","無法戰鬥","地圖已變更","重複序號","伺服器未核准技能","MP 不足","範圍目標數超限"]


func _ready() -> void:
	custom_minimum_size.x = 360
	var column := VBoxContainer.new()
	add_child(column)
	var title := Label.new()
	title.text = "星夢 Online — 登入"
	column.add_child(title)
	var grid := GridContainer.new()
	grid.columns = 2
	column.add_child(grid)
	host_input.text = "localhost"
	port_input.min_value = 1
	port_input.max_value = 65535
	port_input.value = 7777
	username_input.max_length = 32
	password_input.secret = true
	password_input.max_length = 256
	for input in [host_input,port_input,username_input,password_input]:
		input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for pair in [["伺服器",host_input],["連接埠",port_input],["帳號",username_input],["密碼",password_input]]:
		var label := Label.new()
		label.text = pair[0]
		grid.add_child(label)
		grid.add_child(pair[1])
	login_button.text = "登入"
	logout_button.text = "登出／取消"
	choose_button.text = "進入世界"
	inventory_button.text = "背包"
	inventory_button.visible = false
	inventory_button.focus_mode = Control.FOCUS_NONE
	inventory_preview.editable = false
	inventory_preview.visible = false
	inventory_preview.custom_minimum_size = Vector2(350,160)
	inventory_preview.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	inventory_search.placeholder_text = "搜尋道具名稱／ID／類別"
	inventory_search.visible = false
	inventory_search.max_length = 64
	inventory_previous.text = "上一頁"
	inventory_next.text = "下一頁"
	inventory_page_label.text = "第 1 / 1 頁"
	recovery_button.text = "使用選中道具（Server 核實回復效果）"
	recovery_button.disabled = true
	recovery_status.text = ""
	discard_button.text = "丟棄選中數量（永久銷毀）"
	discard_button.disabled = true
	discard_quantity.min_value = 1
	discard_quantity.max_value = 1
	discard_quantity.value = 1
	discard_quantity.step = 1
	discard_all_button.text = "全部數量"
	discard_all_button.disabled = true
	discard_confirm.title = "確認銷毀背包物品"
	discard_confirm.ok_button_text = "確認銷毀"
	add_child(discard_confirm)
	discard_status.text = ""
	for control in [inventory_previous, inventory_next, inventory_page_label,recovery_picker,recovery_button,recovery_status,discard_quantity,discard_all_button,discard_button,discard_status]:
		control.visible = false
	respawn_button.text = "復活"
	skill_hint.text = "技能 ID（需伺服器已核准規則）"
	skill_id_input.min_value = 1
	skill_id_input.max_value = 100000000
	skill_id_input.step = 1
	skill_id_input.value = 99001
	skill_target_mode.text = "點怪施放單體技能（關閉則普攻）"
	skill_target_mode.focus_mode = Control.FOCUS_NONE
	skill_area_button.text = "施放自身中心範圍技能"
	skill_area_button.focus_mode = Control.FOCUS_NONE
	skill_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [login_button,characters,choose_button,respawn_button,skill_hint,skill_id_input,skill_target_mode,skill_area_button,skill_status,logout_button,status,pickup_status,inventory_button,inventory_search,inventory_preview,inventory_previous,inventory_page_label,inventory_next,recovery_picker,recovery_button,recovery_status,discard_quantity,discard_all_button,discard_button,discard_status]:
		column.add_child(control)
	login_button.pressed.connect(login)
	password_input.text_submitted.connect(func(_text): login())
	choose_button.pressed.connect(choose)
	respawn_button.pressed.connect(request_respawn)
	skill_area_button.pressed.connect(_cast_area_skill)
	logout_button.pressed.connect(disconnect_session)
	inventory_button.pressed.connect(_toggle_inventory)
	inventory_search.text_changed.connect(_on_inventory_search_changed)
	inventory_previous.pressed.connect(func(): _set_inventory_page(_inventory_page-1))
	inventory_next.pressed.connect(func(): _set_inventory_page(_inventory_page+1))
	recovery_button.pressed.connect(_use_selected_recovery)
	recovery_picker.item_selected.connect(func(_index): _update_discard_selection())
	discard_all_button.pressed.connect(func(): discard_quantity.value = discard_quantity.max_value)
	discard_button.pressed.connect(_discard_selected_item)
	discard_confirm.confirmed.connect(_confirm_inventory_discard)
	var style := StyleBoxFlat.new()
	style.bg_color = Color("211c15")
	style.border_color = Color("c8a452")
	style.set_border_width_all(2)
	for side in ["left","right","top","bottom"]:
		style.set("content_margin_"+side,16)
	add_theme_stylebox_override("panel",style)
	add_theme_color_override("font_color",Color("f2d995"))
	_set_controls()
	status.text = "請登入伺服器"
	pickup_status.text = ""


func login() -> void:
	if active:
		return
	var username := username_input.text.strip_edges()
	var password := password_input.text
	password_input.clear()
	if username.to_utf8_buffer().size() < 3 or username.to_utf8_buffer().size() > 32 or password.to_utf8_buffer().size() < 8 or password.to_utf8_buffer().size() > 256 or host_input.text.strip_edges().is_empty():
		status.text = "帳號需 3–32 bytes，密碼需 8–256 bytes"
		return
	var error: int = session.connect_secure(host_input.text.strip_edges(),int(port_input.value),username,password,tls_name,trusted_chain)
	password = ""
	if error != OK:
		status.text = "無法開始連線（"+str(error)+"）"
		return
	active = true
	_chosen = false
	_world_epoch = 0
	_latest = {}
	_respawn_queued_after = -1
	_skill_queued_after = -1
	_skill_queued_at_ms = 0
	skill_target_mode.button_pressed = false
	skill_status.text = ""
	_pending_discard_confirmation.clear()
	discard_confirm.hide()
	pickup_status.text = ""
	discard_status.text = ""
	_inventory_display_key = ""
	_inventory_page = 0
	inventory_search.clear()
	inventory_preview.text = ""
	_show_inventory_preview(false)
	characters.clear()
	_characters = []
	_set_controls()
	status.text = "安全連線中…"
	connection_changed.emit(true)


func choose() -> void:
	if not active or _chosen or characters.selected < 0 or characters.selected >= _characters.size():
		return
	if session.choose_character(int(_characters[characters.selected].character_id)):
		_chosen = true
		choose_button.disabled = true
		status.text = "進入世界中…"
	else:
		status.text = "操作佇列忙碌，請稍後重試"


func submit_move(point: Vector2) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and point.is_finite() and session.move_target(int(floor(point.x)),int(floor(point.y)))


func submit_stop_movement() -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and session.stop_movement()


func submit_attack_monster(entity_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and entity_id > 0 and session.attack_monster(entity_id)


# Client chooses only skill identity and target; the server owns rules, cost and damage.
func submit_monster_action(entity_id: int) -> bool:
	if skill_target_mode.button_pressed:
		return submit_target_skill(entity_id)
	return submit_attack_monster(entity_id)


func _skill_can_submit() -> bool:
	return active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD and bool(_latest.get("world_ready",false)) and bool(_latest.get("combat_available",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))


func _skill_queued() -> void:
	_skill_queued_after = int(_latest.get("skill_sequence",0))
	_skill_queued_at_ms = Time.get_ticks_msec()
	skill_status.text = "技能請求已排入佇列，等待伺服器結果"
	_set_skill_controls()


func submit_target_skill(entity_id: int) -> bool:
	if not _skill_can_submit() or _skill_queued_after >= 0 or int(_latest.get("pending_skill_sequence",0)) != 0 or entity_id <= 0:
		return false
	if not session.cast_skill(int(skill_id_input.value),entity_id):
		return false
	_skill_queued()
	return true


func submit_area_skill(skill_id: int) -> bool:
	if not _skill_can_submit() or _skill_queued_after >= 0 or int(_latest.get("pending_skill_sequence",0)) != 0 or skill_id <= 0 or skill_id > 100000000:
		return false
	if not session.cast_area_skill(skill_id):
		return false
	_skill_queued()
	return true


func _cast_area_skill() -> void:
	if not skill_area_button.disabled:
		if not submit_area_skill(int(skill_id_input.value)):
			skill_status.text = "技能未送出；請檢查角色狀態或佇列"


func submit_recovery_use(item_uid: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and bool(_latest.get("combat_available",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_recovery_sequence",0)) == 0 and int(_latest.get("pending_discard_sequence",0)) == 0 and item_uid > 0 and session.use_recovery_item(item_uid)


func submit_inventory_discard(item_uid: int, quantity: int = 1) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_discard_sequence",0)) == 0 and int(_latest.get("pending_recovery_sequence",0)) == 0 and item_uid > 0 and quantity > 0 and quantity <= 1000000 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and quantity <= int(((_latest.get("inventory_items",{}) as Dictionary)[item_uid] as Dictionary).get("quantity",0)) and session.discard_inventory_item(item_uid,quantity)


func submit_pickup_drop(drop_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_pickup_sequence",0)) == 0 and drop_id > 0 and session.pickup_ground_drop(drop_id)


func submit_npc_interact(spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and spawn_id > 0 and session.npc_interact(spawn_id)


func request_respawn() -> void:
	if respawn_button.disabled or not respawn_button.visible or not active:
		return
	if session.respawn():
		# The command first enters the bounded ClientSession intent queue.
		# Guard the UI until WorldState has observed the sent sequence/result.
		_respawn_queued_after = int(_latest.get("respawn_sequence",0))
		respawn_button.disabled = true
		status.text = "復活請求已送出…"
	else:
		status.text = "復活請求尚未送出，請重試"


func disconnect_session() -> void:
	session.stop()
	var was_active := active
	active = false
	_chosen = false
	_world_epoch = 0
	_latest = {}
	_respawn_queued_after = -1
	_skill_queued_after = -1
	_skill_queued_at_ms = 0
	skill_target_mode.button_pressed = false
	skill_status.text = ""
	_pending_discard_confirmation.clear()
	discard_confirm.hide()
	pickup_status.text = ""
	discard_status.text = ""
	_inventory_display_key = ""
	_inventory_page = 0
	inventory_search.clear()
	inventory_preview.text = ""
	_show_inventory_preview(false)
	_characters = []
	characters.clear()
	password_input.clear()
	_set_controls()
	inventory_button.visible = false
	inventory_button.disabled = true
	inventory_preview.visible = false
	status.text = "已登出"
	if was_active:
		connection_changed.emit(false)


func _set_controls() -> void:
	host_input.editable = not active
	username_input.editable = not active
	password_input.editable = not active
	port_input.editable = not active
	login_button.disabled = active
	logout_button.disabled = not active
	characters.visible = active and not _characters.is_empty()
	choose_button.visible = characters.visible
	choose_button.disabled = _chosen or _characters.is_empty()
	_set_respawn_controls()
	_set_skill_controls()


func _set_respawn_controls() -> void:
	var dead: bool = active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD and bool(_latest.get("world_ready",false)) and bool(_latest.get("combat_available",false)) and bool(_latest.get("player_dead",false))
	if not dead:
		_respawn_queued_after = -1
	var sent_sequence := int(_latest.get("respawn_sequence",0))
	var result_sequence := int(_latest.get("last_respawn_result_sequence",0))
	if _respawn_queued_after >= 0 and sent_sequence > _respawn_queued_after and result_sequence >= sent_sequence:
		_respawn_queued_after = -1
	respawn_button.visible = dead
	respawn_button.disabled = not dead or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or bool(_latest.get("waiting_for_new_map",false)) or int(_latest.get("pending_world_ready_ack_epoch",0)) != 0 or int(_latest.get("last_world_ready_ack_epoch",0)) != int(_latest.get("map_epoch",0)) or sent_sequence > result_sequence or _respawn_queued_after >= 0


# Presentation-only pending guard; no gameplay timer or local skill settlement.
func _set_skill_controls() -> void:
	var in_world: bool = active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	# Only a new world/connection generation or the matching server result
	# clears an unsettled skill. Same-epoch resync does not settle MP.
	if not in_world or bool(_latest.get("reconnecting",false)) or bool(_latest.get("waiting_for_new_map",false)):
		_skill_queued_after = -1
	if _skill_queued_after >= 0:
		var sent := int(_latest.get("skill_sequence",0))
		var received := int(_latest.get("last_skill_result_sequence",0))
		if sent > _skill_queued_after and received >= sent and int(_latest.get("pending_skill_sequence",0)) == 0:
			_skill_queued_after = -1
			var code := int(_latest.get("last_skill_result_status",-1))
			skill_status.text = "伺服器技能結果：%s（代碼 %d）" % [_skill_result_name(code),code]
		elif Time.get_ticks_msec() - _skill_queued_at_ms >= 10000:
			skill_status.text = "技能結果尚未確認，請勿重複施放；等待伺服器回覆或重新連線"
	for control in [skill_hint,skill_id_input,skill_target_mode,skill_area_button,skill_status]:
		control.visible = in_world
	var enabled: bool = _skill_can_submit() and _skill_queued_after < 0 and int(_latest.get("pending_skill_sequence",0)) == 0
	skill_id_input.editable = enabled
	skill_target_mode.disabled = not enabled
	skill_area_button.disabled = not enabled


# Display-only mapping of existing Server SkillUseStatus codes.
func _skill_result_name(code: int) -> String:
	return SKILL_RESULT_NAMES[code] if code >= 0 and code < SKILL_RESULT_NAMES.size() else "未知結果"


func _combat_status_suffix() -> String:
	if not bool(_latest.get("combat_available",false)):
		return ""
	return "｜HP %d/%d｜MP %d/%d" % [int(_latest.get("player_current_hp",0)),int(_latest.get("player_max_hp",0)),int(_latest.get("player_current_mp",0)),int(_latest.get("player_max_mp",0))]


func _show_inventory_preview(show: bool) -> void:
	inventory_preview.visible = show
	recovery_picker.visible = show
	recovery_button.visible = show
	recovery_status.visible = show
	discard_quantity.visible = show
	discard_all_button.visible = show
	discard_button.visible = show
	discard_status.visible = show
	inventory_search.visible = show
	inventory_previous.visible = show
	inventory_next.visible = show
	inventory_page_label.visible = show


func _toggle_inventory() -> void:
	if inventory_button.disabled or not inventory_button.visible:
		return
	_show_inventory_preview(not inventory_preview.visible)
	if inventory_preview.visible:
		_update_inventory_ui()


func _on_inventory_search_changed(_query: String) -> void:
	_inventory_page = 0
	_inventory_display_key = ""
	_update_inventory_ui()


func _set_inventory_page(page: int) -> void:
	_inventory_page = maxi(0,page)
	_inventory_display_key = ""
	_update_inventory_ui()


func _use_selected_recovery() -> void:
	if recovery_button.disabled or recovery_picker.selected < 0:
		return
	var uid := int(recovery_picker.get_item_metadata(recovery_picker.selected))
	if uid <= 0:
		return
	if submit_recovery_use(uid):
		recovery_button.disabled = true
		discard_button.disabled = true
		recovery_status.text = "道具使用請求已送出…"
	else:
		recovery_status.text = "目前無法使用該物品"


func _update_discard_selection() -> void:
	var items: Dictionary = _latest.get("inventory_items",{})
	var uid := 0
	if recovery_picker.selected >= 0:
		uid = int(recovery_picker.get_item_metadata(recovery_picker.selected))
	var count := int((items.get(uid,{}) as Dictionary).get("quantity",0))
	discard_quantity.max_value = maxi(1,mini(1000000,count))
	discard_quantity.value = 1
	_update_discard_controls()


func _update_discard_controls() -> void:
	var enabled: bool = not discard_button.disabled and recovery_picker.selected >= 0
	discard_quantity.editable = enabled
	discard_all_button.disabled = not enabled


func _discard_selected_item() -> void:
	if discard_button.disabled or recovery_picker.selected < 0 or not _pending_discard_confirmation.is_empty():
		return
	var uid := int(recovery_picker.get_item_metadata(recovery_picker.selected))
	var quantity := int(discard_quantity.value)
	var items: Dictionary = _latest.get("inventory_items",{})
	if uid <= 0 or not items.has(uid) or quantity < 1 or quantity > mini(1000000,int((items[uid] as Dictionary).get("quantity",0))):
		discard_status.text = "數量或選取物品已失效"
		return
	_pending_discard_confirmation = {
		"actor_id":int(_latest.get("actor_id",0)),
		"reconnect_count":int(_latest.get("reconnect_count",0)),
		"map_epoch":int(_latest.get("map_epoch",0)),
		"inventory_revision":int(_latest.get("inventory_revision",0)),
		"uid":uid,"quantity":quantity,
	}
	discard_confirm.dialog_text = "確定永久銷毀 %s ×%d（UID %d）？\n這不是丟到地上，無法復原。" % [
		item_catalog.name_for(int((items[uid] as Dictionary).item_definition_id)),quantity,uid]
	discard_confirm.popup_centered()


func _confirm_inventory_discard() -> void:
	if _pending_discard_confirmation.is_empty():
		return
	var requested: Dictionary = _pending_discard_confirmation.duplicate()
	_pending_discard_confirmation.clear()
	# Confirmation is not authorization. Re-check the exact actor, epoch,
	# inventory revision, selected UID and stack size before queuing.
	var uid := int(requested.uid)
	var quantity := int(requested.quantity)
	var items: Dictionary = _latest.get("inventory_items",{})
	if discard_button.disabled or int(_latest.get("actor_id",0)) != int(requested.actor_id) or int(_latest.get("reconnect_count",0)) != int(requested.reconnect_count) or int(_latest.get("map_epoch",0)) != int(requested.map_epoch) or int(_latest.get("inventory_revision",0)) != int(requested.inventory_revision) or recovery_picker.selected < 0 or int(recovery_picker.get_item_metadata(recovery_picker.selected)) != uid or int(discard_quantity.value) != quantity or not items.has(uid) or quantity < 1 or quantity > int((items.get(uid,{}) as Dictionary).get("quantity",0)):
		discard_status.text = "背包或角色狀態已變更，請重新選取並確認"
		return
	if submit_inventory_discard(uid,quantity):
		discard_button.disabled = true
		recovery_button.disabled = true
		_update_discard_controls()
		discard_status.text = "丟棄 ×%d 請求已送出，等待伺服器結算…" % quantity
	else:
		discard_status.text = "目前無法丟棄該物品"


func _update_inventory_ui() -> void:
	var in_world: bool = active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	var loaded: bool = in_world and bool(_latest.get("inventory_ready",false))
	inventory_button.visible = in_world
	inventory_button.disabled = not loaded or bool(_latest.get("reconnecting",false))
	if not in_world or bool(_latest.get("reconnecting",false)):
		_pending_discard_confirmation.clear()
		discard_confirm.hide()
		_show_inventory_preview(false)
		_inventory_display_key = ""
		return
	if not loaded:
		_pending_discard_confirmation.clear()
		discard_confirm.hide()
		inventory_button.text = "背包（讀取中）"
		_show_inventory_preview(false)
		_inventory_display_key = ""
		return
	var items: Dictionary = _latest.get("inventory_items",{})
	inventory_button.text = "背包（%d 件）" % items.size()
	recovery_button.disabled = not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_recovery_sequence",0)) != 0 or int(_latest.get("pending_discard_sequence",0)) != 0 or recovery_picker.item_count == 0
	discard_button.disabled = bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_discard_sequence",0)) != 0 or int(_latest.get("pending_recovery_sequence",0)) != 0 or recovery_picker.item_count == 0
	_update_discard_controls()
	var discard_result: Dictionary = _latest.get("last_discard_result",{})
	if not discard_result.is_empty():
		if int(discard_result.status) in [0,1]:
			discard_status.text = "丟棄結算：已移除 %d；剩餘 %d" % [
				int(discard_result.discarded_quantity),
				int(discard_result.remaining_quantity)]
		else:
			discard_status.text = "丟棄失敗（伺服器代碼 %d）" % int(discard_result.status)
	var use_result: Dictionary = _latest.get("last_recovery_result",{})
	if not use_result.is_empty():
		if int(use_result.status) in [0,1]:
			recovery_status.text = "回復結算：HP %d / %d；剩餘 %d 件" % [
				int(use_result.current_hp),int(use_result.max_hp),
				int(use_result.remaining_quantity)]
		else:
			recovery_status.text = "道具無法使用（伺服器代碼 %d）" % int(use_result.status)
	var revision := int(_latest.get("inventory_revision",0))
	var query := inventory_search.text.strip_edges()
	# Most UI polls see an unchanged inventory. Do not allocate/sort/scan
	# up to 4095 items every 50ms; refresh only on visible state changes.
	var cached_key := "%d/%d/%d/%d/%s" % [
		int(_latest.get("actor_id",0)),
		int(_latest.get("reconnect_count",0)),revision,_inventory_page,query]
	if cached_key == _inventory_display_key:
		return
	var uids: Array = items.keys()
	uids.sort()
	var filtered: Array = []
	for raw_uid in uids:
		var id := int(raw_uid)
		var item: Dictionary = items[raw_uid]
		var item_id := int(item.item_definition_id)
		if query.is_empty() or item_catalog.name_for(item_id).findn(query) >= 0 or str(item_id).findn(query) >= 0 or item_catalog.category_for(item_id).findn(query) >= 0:
			filtered.append(raw_uid)
	var pages := maxi(1,ceili(float(filtered.size())/float(INVENTORY_PAGE_SIZE)))
	_inventory_page = clampi(_inventory_page,0,pages-1)
	inventory_previous.disabled = _inventory_page == 0
	inventory_next.disabled = _inventory_page >= pages-1
	inventory_page_label.text = "第 %d / %d 頁（符合 %d 件）" % [
		_inventory_page+1,pages,filtered.size()]
	# Revision can reset to 1 on reconnect/new character. Include all view
	# dimensions to avoid caching another character's or filter's item list.
	var display_key := "%d/%d/%d/%d/%s" % [
		int(_latest.get("actor_id",0)),
		int(_latest.get("reconnect_count",0)),revision,_inventory_page,query]
	if display_key == _inventory_display_key:
		return
	_inventory_display_key = display_key
	var rows := PackedStringArray()
	recovery_picker.clear()
	var start := _inventory_page*INVENTORY_PAGE_SIZE
	for index in range(start,mini(start+INVENTORY_PAGE_SIZE,filtered.size())):
		var uid := int(filtered[index])
		var item: Dictionary = items[filtered[index]]
		var display_name := item_catalog.name_for(int(item.item_definition_id))
		rows.append("%s ×%d  ·  UID %d" % [
			display_name,int(item.quantity),uid])
		# Allow selection without the optional local item catalog; the server
		# rejects all items without admitted, source-audited recovery rules.
		recovery_picker.add_item("%s ×%d (UID %d)" % [
			display_name,int(item.quantity),uid])
		recovery_picker.set_item_metadata(recovery_picker.item_count-1,uid)
	if recovery_picker.item_count > 0:
		recovery_picker.select(0)
	_update_discard_selection()
	recovery_button.disabled = not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_recovery_sequence",0)) != 0 or int(_latest.get("pending_discard_sequence",0)) != 0 or recovery_picker.item_count == 0
	discard_button.disabled = bool(_latest.get("resync_pending",false)) or int(_latest.get("pending_discard_sequence",0)) != 0 or int(_latest.get("pending_recovery_sequence",0)) != 0 or recovery_picker.item_count == 0
	_update_discard_controls()
	if rows.is_empty():
		rows.append("沒有符合條件的物品" if not query.is_empty() else "背包目前沒有物品")
	inventory_preview.text = "\n".join(rows)


func _process(delta: float) -> void:
	if not active:
		return
	_poll_elapsed += delta
	if _poll_elapsed < 0.05:
		return
	_poll_elapsed = 0.0
	_latest = session.snapshot() # Reads published state; never pumps packets.
	_set_respawn_controls()
	_set_skill_controls()
	_update_inventory_ui()
	var pickup_result: Dictionary = _latest.get("last_pickup_result",{})
	if not pickup_result.is_empty():
		if int(pickup_result.status) in [0,1]:
			pickup_status.text = "本次拾取：道具 %d ×%d（UID %d）" % [int(pickup_result.item_definition_id),int(pickup_result.quantity),int(pickup_result.item_uid)]
		else:
			pickup_status.text = "拾取失敗，代碼 %d" % int(pickup_result.status)
	if bool(_latest.get("reconnecting",false)):
		status.text = "連線中斷，正在重新連線（%d/%d）…" % [int(_latest.reconnect_attempt),int(_latest.max_reconnect_attempts)]
		return
	var state := int(_latest.state)
	if state == State.STATE_FAILED:
		disconnect_session()
		status.text = "連線或登入失敗，請確認帳號與伺服器憑證後重試"
		return
	if state == State.STATE_SELECTING_CHARACTER:
		if _chosen and int(_latest.choose_status) >= 0:
			disconnect_session()
			status.text = "角色無法使用，請重新登入"
			return
		if _latest.characters != _characters:
			_characters = _latest.characters
			characters.clear()
			for entry in _characters:
				characters.add_item(str(entry.name))
			_set_controls()
		if not _chosen:
			status.text = "請選擇角色" if not _characters.is_empty() else "帳號尚無角色；創角功能待開放"
	elif state == State.STATE_IN_WORLD:
		characters.hide()
		choose_button.hide()
		status.text = "已連線・角色 " + str(_latest.actor_id)
		if bool(_latest.get("combat_available",false)):
			status.text += _combat_status_suffix()
			if bool(_latest.get("player_dead",false)):
				status.text += "｜復活處理中" if respawn_button.disabled else "｜已死亡，請復活"
		if bool(_latest.world_ready) and int(_latest.map_epoch) != _world_epoch:
			_world_epoch = int(_latest.map_epoch)
			world_ready.emit(_latest)
		if bool(_latest.world_ready):
			world_snapshot.emit(_latest)


func _exit_tree() -> void:
	session.stop()
