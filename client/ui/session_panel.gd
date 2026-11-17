extends PanelContainer
## UI adapter only. ClientSession owns transport/reduction on its own threads.
signal connection_changed(active: bool)
signal world_ready(snapshot: Dictionary)
signal world_snapshot(snapshot: Dictionary)

const Session = preload("res://network/client_session.gd")
const State = preload("res://network/world_state.gd")
var session = Session.new()
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
var pickup_status := Label.new()
var status := Label.new()
var _characters: Array = []
var _chosen := false
var _world_epoch := 0
var _poll_elapsed := 0.0
var _latest := {}
var _respawn_queued_after := -1


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
	respawn_button.text = "復活"
	for control in [login_button,characters,choose_button,respawn_button,logout_button,status,pickup_status]:
		column.add_child(control)
	login_button.pressed.connect(login)
	password_input.text_submitted.connect(func(_text): login())
	choose_button.pressed.connect(choose)
	respawn_button.pressed.connect(request_respawn)
	logout_button.pressed.connect(disconnect_session)
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
	pickup_status.text = ""
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


func submit_attack_monster(entity_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and entity_id > 0 and session.attack_monster(entity_id)


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
	pickup_status.text = ""
	_characters = []
	characters.clear()
	password_input.clear()
	_set_controls()
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


func _process(delta: float) -> void:
	if not active:
		return
	_poll_elapsed += delta
	if _poll_elapsed < 0.05:
		return
	_poll_elapsed = 0.0
	_latest = session.snapshot() # Reads published state; never pumps packets.
	_set_respawn_controls()
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
			status.text += "｜HP %d/%d" % [int(_latest.get("player_current_hp",0)),int(_latest.get("player_max_hp",0))]
			if bool(_latest.get("player_dead",false)):
				status.text += "｜復活處理中" if respawn_button.disabled else "｜已死亡，請復活"
		if bool(_latest.world_ready) and int(_latest.map_epoch) != _world_epoch:
			_world_epoch = int(_latest.map_epoch)
			world_ready.emit(_latest)
		if bool(_latest.world_ready):
			world_snapshot.emit(_latest)


func _exit_tree() -> void:
	session.stop()
