extends PanelContainer
## UI adapter only. ClientSession owns transport/reduction on its own threads.
signal connection_changed(active: bool)
signal world_ready(snapshot: Dictionary)
signal world_snapshot(snapshot: Dictionary)

const Session = preload("res://session/client_session.gd")
const SessionLayout = preload("res://ui/session_layout.gd")
const GameplayHud = preload("res://ui/gameplay_hud.gd")
const SessionArtwork = preload("res://ui/session_artwork.gd")
const CreationOptions = preload("res://player/character_creation_options.gd")
const AppearancePicker = preload("res://ui/character_appearance_picker.gd")
const State = preload("res://world/world_state.gd")
const TradePanel = preload("res://trade/trade_panel.gd")
const InventoryPanel = preload("res://inventory/inventory_panel.gd")
const PlayerShopPanel = preload("res://player_shop/player_shop_panel.gd")
const QuestPanel = preload("res://quest/quest_panel.gd")
const ChatPanel = preload("res://chat/chat_panel.gd")
const PartyPanel = preload("res://party/party_panel.gd")
const ItemCatalog = preload("res://inventory/item_catalog.gd")
const BankPanel = preload("res://npc/bank_panel.gd")
const NpcShopPanel = preload("res://npc/npc_shop_panel.gd")
const StoragePanel = preload("res://npc/storage_panel.gd")
const RepairPanel = preload("res://npc/repair_panel.gd")
const EnhancementPanel = preload("res://npc/enhancement_panel.gd")
const PvpPanel = preload("res://pvp/pvp_panel.gd")
const PetPanel = preload("res://pet/pet_panel.gd")
const MechaPanel = preload("res://mecha/mecha_panel.gd")
var session = Session.new()
var creation_options := CreationOptions.new()
var appearance_picker := AppearancePicker.new()
var _layout := SessionLayout.new()
var _artwork := SessionArtwork.new()
var _scroll := ScrollContainer.new()
var gameplay_hud := GameplayHud.new()
var new_character_button := Button.new()
var back_to_character_button := Button.new()
var _show_create := false
var inventory_panel := InventoryPanel.new()
var trade_panel := TradePanel.new()
var player_shop_panel := PlayerShopPanel.new()
var quest_panel := QuestPanel.new()
var chat_panel := ChatPanel.new()
var party_panel := PartyPanel.new()
var item_catalog = ItemCatalog.new()
var bank_panel := BankPanel.new()
var npc_shop_panel := NpcShopPanel.new()
var storage_panel := StoragePanel.new()
var repair_panel := RepairPanel.new()
var enhancement_panel := EnhancementPanel.new()
var pvp_panel := PvpPanel.new()
var pet_panel := PetPanel.new()
var mecha_panel := MechaPanel.new()
var active := false
## Deployment-only connection parameters; never Godot GUI widgets.
var server_host := "localhost"
var server_port := 7777
var username_input := LineEdit.new()
var password_input := LineEdit.new()
var login_button := Button.new()
var register_button := Button.new()
var logout_button := Button.new()
var characters := OptionButton.new()
var choose_button := Button.new()
var character_name_input := LineEdit.new()
var character_slot_input := SpinBox.new()
var character_strength_input := SpinBox.new()
var character_dexterity_input := SpinBox.new()
var character_constitution_input := SpinBox.new()
var character_wisdom_input := SpinBox.new()
var character_password_input := LineEdit.new()
var character_password_confirm_input := LineEdit.new()
var character_choose_password_input := LineEdit.new()
var create_character_button := Button.new()
var character_create_status := Label.new()
var respawn_button := Button.new()
var skill_hint := Label.new()
var skill_id_input := SpinBox.new()
var skill_target_mode := CheckButton.new()
var skill_area_button := Button.new()
var skill_status := Label.new()
var pickup_status := Label.new()
var status := Label.new()
var _characters: Array = []
var _chosen := false
var _world_epoch := 0
var _poll_elapsed := 0.0
var _latest := {}
var _last_publication_revision := -1
var _create_queued_after := -1
var _last_manage_revision := -1
var _respawn_queued_after := -1
var _skill_queued_after := -1
var _skill_queued_at_ms := 0
const SKILL_RESULT_NAMES = ["成功","未命中","目標不存在","目標死亡","超出距離","障礙阻擋","冷卻中","無法戰鬥","地圖已變更","重複序號","伺服器未核准技能","MP 不足","範圍目標數超限","此地圖禁止玩家 PvP","攻擊者為受保護藍名","目標為受保護藍名","不可對自己施放","伺服器忙碌，請稍後重試"]


func _ready() -> void:
	custom_minimum_size.x = 360
	# The authenticated gameplay controls exceed the viewport height.
	# Clip and scroll the ONE existing form rather than letting trade/PvP
	# actions extend off-screen or constructing a parallel UI.
	var scroll := _scroll
	scroll.name = "SessionOptionsScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroll.follow_focus = true
	scroll.custom_minimum_size.y = 0.0
	add_child(scroll)
	get_viewport().size_changed.connect(func():
		_scroll.custom_minimum_size.y = maxf(220.0,get_viewport_rect().size.y-96.0) if _layout.current_page == SessionLayout.Page.WORLD else 0.0)
	_layout.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_layout)
	var title := Label.new()
	title.text = "星夢 Online — 登入"
	_layout.login_page.add_child(title)
	var grid := GridContainer.new()
	grid.columns = 2
	_layout.login_page.add_child(grid)
	username_input.max_length = 32
	password_input.secret = true
	password_input.max_length = 256
	for input in [username_input,password_input]:
		input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	# Server endpoint is launch/deployment configuration. Only account
	# credentials appear in this presentation layer.
	for pair in [["帳號",username_input],["密碼",password_input]]:
		var label := Label.new()
		label.text = pair[0]
		grid.add_child(label)
		grid.add_child(pair[1])
	login_button.text = "登入"
	register_button.text = "建立帳號並登入"
	logout_button.text = "登出／取消"
	choose_button.text = "進入世界"
	character_name_input.placeholder_text = "新角色名稱"
	character_name_input.max_length = 24
	character_slot_input.min_value = 0
	character_slot_input.max_value = 7
	character_slot_input.step = 1
	character_slot_input.value = 0
	for stat in [
		character_strength_input,character_dexterity_input,
		character_constitution_input,character_wisdom_input
	]:
		stat.min_value = 1
		stat.max_value = 5
		stat.step = 1
		stat.value = 1
	character_password_input.secret = true
	character_password_confirm_input.secret = true
	character_choose_password_input.secret = true
	character_choose_password_input.placeholder_text = "角色密碼（4 字元，需要時輸入）"
	for secret_input in [
		character_password_input,character_password_confirm_input,
		character_choose_password_input
	]:
		secret_input.max_length = 4
	create_character_button.text = "建立角色"
	new_character_button.text = "建立新角色"
	back_to_character_button.text = "返回角色選單"
	new_character_button.pressed.connect(func():
		_show_create = true
		_set_controls())
	back_to_character_button.pressed.connect(func():
		_show_create = false
		_set_controls())
	character_create_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	character_create_status.text = ""
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
	for control in [login_button,register_button]:
		_layout.login_page.add_child(control)
	var selection_heading := Label.new()
	selection_heading.text = "選擇角色"
	_layout.select_page.add_child(selection_heading)
	for control in [characters,character_choose_password_input,choose_button,new_character_button]:
		_layout.select_page.add_child(control)
	var creation_heading := Label.new()
	creation_heading.text = "建立角色"
	_layout.create_page.add_child(creation_heading)
	var create_grid := GridContainer.new()
	_layout.create_page.add_child(create_grid)
	for pair in [
		["角色名稱",character_name_input],["角色欄位",character_slot_input],
		["力量",character_strength_input],["速度",character_dexterity_input],
		["體質",character_constitution_input],["智慧",character_wisdom_input],
		["角色密碼",character_password_input],
		["確認密碼",character_password_confirm_input]
	]:
		var label := Label.new()
		label.text = pair[0]
		create_grid.add_child(label)
		create_grid.add_child(pair[1])
	for control in [create_character_button,character_create_status,back_to_character_button]:
		_layout.create_page.add_child(control)
	_layout.create_page.add_child(appearance_picker)
	_layout.create_page.move_child(appearance_picker,2)
	appearance_picker.bind_options(creation_options)
	_layout.bind_forms(grid,create_grid)
	# Persisting controls remain in one owner and are only reparented when a
	# skin switches gameplay layout; no gameplay event or Session is rebuilt.
	_layout.add_child(status)
	_layout.add_child(logout_button)
	npc_shop_panel.set_actions(Callable(self,"submit_shop_open"),Callable(self,"submit_shop_buy"),Callable(self,"submit_shop_sell"),item_catalog)

	storage_panel.set_actions(Callable(self,"submit_storage_open"),Callable(self,"submit_storage_deposit"),Callable(self,"submit_storage_withdraw"),item_catalog)

	repair_panel.set_actions(Callable(self,"submit_repair_open"),Callable(self,"submit_repair_item"),item_catalog)

	enhancement_panel.set_actions(Callable(self,"submit_enhancement_open"),Callable(self,"submit_enhancement_item"),item_catalog)
	pet_panel.set_actions(
		Callable(self,"submit_pet_summon"),Callable(self,"submit_pet_recall"),
		Callable(self,"submit_pet_refresh"),Callable(self,"submit_pet_equip"),
		Callable(self,"submit_pet_unequip"),item_catalog)
	mecha_panel.set_actions(
		Callable(self,"submit_mecha_mount"),Callable(self,"submit_mecha_dismount"),
		Callable(self,"submit_mecha_refresh"),Callable(self,"submit_mecha_equip"),
		Callable(self,"submit_mecha_unequip"),Callable(self,"submit_mecha_charge"),item_catalog)

	login_button.pressed.connect(login)
	register_button.pressed.connect(register_account)
	password_input.text_submitted.connect(func(_text): login())
	choose_button.pressed.connect(choose)
	create_character_button.pressed.connect(create_character)
	character_name_input.text_submitted.connect(func(_text): create_character())
	respawn_button.pressed.connect(request_respawn)
	skill_area_button.pressed.connect(_cast_area_skill)
	logout_button.pressed.connect(disconnect_session)
	bank_panel.set_actions(Callable(self,"submit_bank_open"),Callable(self,"submit_bank_deposit"),Callable(self,"submit_bank_withdraw"))





	quest_panel.set_actions(Callable(self, "submit_quest_open"), Callable(self, "submit_quest_choice"))

	pvp_panel.set_actions(Callable(self,"submit_pvp_mode"),Callable(self,"submit_pvp_attack"),
		Callable(self,"submit_pvp_skill"),Callable(self,"submit_gm_pvp_name"),
		Callable(skill_id_input,"get_value"),SKILL_RESULT_NAMES)
	_layout.world_page.add_child(gameplay_hud)
	_layout.world_page.move_child(gameplay_hud,1)
	_layout.add_group("角色／背包",[inventory_panel,pet_panel,mecha_panel,pickup_status])
	_layout.add_group("戰鬥／技能",[respawn_button,skill_hint,skill_id_input,skill_target_mode,skill_area_button,skill_status,pvp_panel])
	_layout.add_group("NPC 服務",[npc_shop_panel,storage_panel,bank_panel,repair_panel,enhancement_panel,quest_panel])
	_layout.add_group("社交／交易",[chat_panel,party_panel,trade_panel,player_shop_panel])
	_artwork.setup(_layout)
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


func login()->void:
	_begin_connection(false)


func register_account()->void:
	_begin_connection(true)


func _begin_connection(register_first:bool)->void:
	if active:
		return
	var username:=username_input.text.strip_edges()
	var password:=password_input.text
	password_input.clear()
	if (
		username.to_utf8_buffer().size()<3 or
		username.to_utf8_buffer().size()>32 or
		password.to_utf8_buffer().size()<8 or
		password.to_utf8_buffer().size()>256
	):
		status.text="帳號需 3–32 bytes，密碼需 8–256 bytes"
		return
	if server_host.strip_edges().is_empty() or server_port < 1 or server_port > 65535:
		status.text = "連線設定無效，請聯絡管理員"
		return
	var error:int=session.register_secure(
		server_host.strip_edges(),server_port,username,password
	) if register_first else session.connect_secure(
		server_host.strip_edges(),server_port,username,password
	)
	password=""
	if error != OK:
		status.text = "無法開始連線（"+str(error)+"）"
		return
	active = true
	_show_create = false
	_chosen = false
	_world_epoch = 0
	_latest = {}
	gameplay_hud.reset()
	_last_publication_revision = -1
	_last_manage_revision = -1
	_respawn_queued_after = -1
	_skill_queued_after = -1
	_skill_queued_at_ms = 0
	skill_target_mode.button_pressed = false
	skill_status.text = ""
	pickup_status.text = ""
	chat_panel.reset()
	party_panel.reset()
	quest_panel.reset()
	pvp_panel.reset()
	npc_shop_panel.reset()
	storage_panel.reset()
	bank_panel.reset()
	repair_panel.reset()
	enhancement_panel.reset()
	trade_panel.reset()
	player_shop_panel.reset()
	inventory_panel.reset()
	characters.clear()
	_characters = []
	_set_controls()
	status.text = "建立帳號並驗證中…" if register_first else "安全連線中…"
	connection_changed.emit(true)


func create_character() -> void:
	if not active or _chosen or _create_queued_after >= 0:
		return
	var character_name := character_name_input.text.strip_edges()
	if character_name.is_empty():
		character_create_status.text = "請輸入角色名稱"
		return
	var attributes := {
		"strength": int(character_strength_input.value),
		"dexterity": int(character_dexterity_input.value),
		"constitution": int(character_constitution_input.value),
		"wisdom": int(character_wisdom_input.value),
	}
	if int(attributes.strength)+int(attributes.dexterity)+int(attributes.constitution)+int(attributes.wisdom) != 8:
		character_create_status.text = "能力點必須全部分配（四項合計 8）"
		return
	var character_password := character_password_input.text
	if character_password.length() != 4:
		character_create_status.text = "角色密碼必須是 4 個字元"
		return
	if character_password != character_password_confirm_input.text:
		character_create_status.text = "兩次角色密碼不一致"
		return
	var creation := attributes.duplicate()
	creation.password = character_password
	var appearance := creation_options.selection()
	var queued: bool = session.create_character(
		int(character_slot_input.value),character_name,appearance,creation)
	if queued:
		_create_queued_after = int(_latest.get("character_create_result_revision",0))
		character_create_status.text = "建立角色中…"
		character_password_input.clear()
		character_password_confirm_input.clear()
		creation.erase("password")
		_set_controls()
	else:
		creation.erase("password")
		character_create_status.text = "目前無法建立角色，請確認外觀、能力點與角色密碼"

func choose(character_password: String = "") -> void:
	if not active or _chosen or characters.selected < 0 or characters.selected >= _characters.size():
		return
	var password := character_password
	if password.is_empty():
		password = character_choose_password_input.text
	if session.choose_character(int(_characters[characters.selected].character_id),password):
		_chosen = true
		choose_button.disabled = true
		character_choose_password_input.clear()
		status.text = "進入世界中…"
	else:
		status.text = "操作佇列忙碌，請稍後重試"


func change_character_password(character_id: int, old_password: String, new_password: String) -> bool:
	if not active or not _owns_character(character_id):
		return false
	var accepted: bool = session.change_character_password(character_id,old_password,new_password)
	if accepted:
		status.text = "正在更新角色密碼…"
	return accepted

func delete_character(character_id: int, password: String) -> bool:
	if not active or not _owns_character(character_id):
		return false
	var accepted: bool = session.delete_character(character_id,password)
	if accepted:
		status.text = "正在刪除角色…"
	return accepted

func _owns_character(character_id: int) -> bool:
	for entry in _characters:
		if int(entry.character_id) == character_id:
			return true
	return false

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


func submit_pickup_drop(drop_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and int(_latest.get("pending_pickup_sequence",0)) == 0 and drop_id > 0 and session.pickup_ground_drop(drop_id)


func submit_npc_interact(spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and spawn_id > 0 and session.npc_interact(spawn_id)




func submit_shop_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and npc_spawn_id > 0 and session.open_shop(npc_spawn_id)


func submit_shop_buy(item_definition_id: int, quantity: int = 1) -> bool:
	return active and int(_latest.get("active_shop_npc",0)) > 0 and int(_latest.get("pending_shop_sequence",0)) == 0 and item_definition_id > 0 and quantity > 0 and session.buy_shop_item(item_definition_id,quantity)


func submit_shop_sell(item_uid: int, quantity: int = 1) -> bool:
	return active and int(_latest.get("active_shop_npc",0)) > 0 and int(_latest.get("pending_shop_sequence",0)) == 0 and item_uid > 0 and quantity > 0 and quantity <= 1000000 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and quantity <= int(((_latest.get("inventory_items",{}) as Dictionary)[item_uid] as Dictionary).get("quantity",0)) and session.sell_shop_item(item_uid,quantity)


func submit_storage_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and npc_spawn_id > 0 and int(_latest.get("pending_storage_sequence",0)) == 0 and session.open_storage(npc_spawn_id)


func submit_storage_deposit(item_uid: int) -> bool:
	return active and bool(_latest.get("storage_ready",false)) and int(_latest.get("active_storage_npc",0)) > 0 and int(_latest.get("pending_storage_sequence",0)) == 0 and item_uid > 0 and (_latest.get("inventory_items",{}) as Dictionary).has(item_uid) and session.deposit_storage_item(item_uid)


func submit_storage_withdraw(item_uid: int) -> bool:
	return active and bool(_latest.get("storage_ready",false)) and int(_latest.get("active_storage_npc",0)) > 0 and int(_latest.get("pending_storage_sequence",0)) == 0 and item_uid > 0 and (_latest.get("storage_items",{}) as Dictionary).has(item_uid) and session.withdraw_storage_item(item_uid)


func submit_bank_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_bank_sequence",0)) == 0 and npc_spawn_id > 0 and session.open_bank(npc_spawn_id)


func submit_bank_deposit(amount: int) -> bool:
	return active and int(_latest.get("active_bank_npc",0)) > 0 and int(_latest.get("bank_scope",0)) in [1,2] and int(_latest.get("pending_bank_sequence",0)) == 0 and amount > 0 and amount <= int(_latest.get("bank_wallet_money",0)) and session.deposit_bank_money(amount)


func submit_bank_withdraw(amount: int) -> bool:
	return active and int(_latest.get("active_bank_npc",0)) > 0 and int(_latest.get("bank_scope",0)) in [1,2] and int(_latest.get("pending_bank_sequence",0)) == 0 and amount > 0 and amount <= int(_latest.get("bank_money",0)) and session.withdraw_bank_money(amount)


func submit_repair_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_repair_sequence",0)) == 0 and npc_spawn_id > 0 and session.open_repair(npc_spawn_id)


func submit_repair_item(item_uid: int) -> bool:
	return active and bool(_latest.get("repair_ready",false)) and int(_latest.get("active_repair_npc",0)) > 0 and int(_latest.get("pending_repair_sequence",0)) == 0 and item_uid > 0 and (_latest.get("repair_items",{}) as Dictionary).has(item_uid) and session.repair_item(item_uid)


func submit_enhancement_open(npc_spawn_id: int) -> bool:
	return active and not bool(_latest.get("reconnecting",false)) and bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_enhancement_sequence",0)) == 0 and int(_latest.get("pending_repair_sequence",0)) == 0 and npc_spawn_id > 0 and session.open_enhancement(npc_spawn_id)


func submit_enhancement_item(item_uid: int) -> bool:
	return active and bool(_latest.get("enhancement_ready",false)) and int(_latest.get("active_enhancement_npc",0)) > 0 and int(_latest.get("pending_enhancement_sequence",0)) == 0 and int(_latest.get("pending_repair_sequence",0)) == 0 and item_uid > 0 and (_latest.get("enhancement_items",{}) as Dictionary).has(item_uid) and session.enhance_item(item_uid)


func submit_pet_summon(pet_uid: int) -> bool:
	return active and pet_uid > 0 and session.pet_summon(pet_uid)


func submit_pet_recall() -> bool:
	return active and session.pet_recall()


func submit_pet_refresh() -> bool:
	return active and session.pet_refresh()


func submit_pet_equip(pet_uid: int,item_uid: int) -> bool:
	return active and pet_uid > 0 and item_uid > 0 and session.pet_equip(pet_uid,item_uid)


func submit_pet_unequip(pet_uid: int,item_uid: int) -> bool:
	return active and pet_uid > 0 and item_uid > 0 and session.pet_unequip(pet_uid,item_uid)


func submit_mecha_mount(mecha_uid: int) -> bool:
	return active and mecha_uid > 0 and session.mecha_mount(mecha_uid)


func submit_mecha_dismount() -> bool:
	return active and session.mecha_dismount()


func submit_mecha_refresh() -> bool:
	return active and session.mecha_refresh()


func submit_mecha_equip(mecha_uid: int,item_uid: int) -> bool:
	return active and mecha_uid > 0 and item_uid > 0 and session.mecha_equip(mecha_uid,item_uid)


func submit_mecha_unequip(mecha_uid: int,item_uid: int) -> bool:
	return active and mecha_uid > 0 and item_uid > 0 and session.mecha_unequip(mecha_uid,item_uid)


func submit_mecha_charge(mecha_uid: int,item_uid: int) -> bool:
	return active and mecha_uid > 0 and item_uid > 0 and session.mecha_charge(mecha_uid,item_uid)


func submit_pvp_attack(target_actor_id: int) -> bool:
	if not active or int(_latest.get("state",-1)) != State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or bool(_latest.get("waiting_for_new_map",false)) or not bool(_latest.get("pvp_known",false)) or not bool(_latest.get("pvp_map_allows",false)) or int(_latest.get("pending_pvp_attack_sequence",0)) != 0:
		return false
	var entities: Dictionary = _latest.get("entities",{})
	if target_actor_id <= 0 or target_actor_id == int(_latest.get("actor_id",0)) or not entities.has(target_actor_id):
		return false
	return session.attack_player(target_actor_id)


func submit_pvp_skill(target_actor_id:int,skill_id:int)->bool:
	if not active or skill_id<=0 or skill_id>100000000 or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or not bool(_latest.get("combat_available",false)) or bool(_latest.get("player_dead",false)) or bool(_latest.get("reconnecting",false)) or bool(_latest.get("resync_pending",false)) or bool(_latest.get("waiting_for_new_map",false)) or not bool(_latest.get("pvp_known",false)) or not bool(_latest.get("pvp_map_allows",false)) or int(_latest.get("pending_skill_sequence",0))!=0:
		return false
	var entities:Dictionary=_latest.get("entities",{})
	if target_actor_id<=0 or target_actor_id==int(_latest.get("actor_id",0)) or not entities.has(target_actor_id):
		return false
	return session.cast_player_skill(skill_id,target_actor_id)


func submit_quest_open(npc_spawn_id:int)->bool:
	return (
		active and npc_spawn_id>0 and
		int(_latest.get("state",-1))==State.STATE_IN_WORLD and
		bool(_latest.get("world_ready",false)) and
		not bool(_latest.get("player_dead",false)) and
		not bool(_latest.get("reconnecting",false)) and
		not bool(_latest.get("resync_pending",false)) and
		not bool(_latest.get("waiting_for_new_map",false)) and
		int(_latest.get("pending_quest_sequence",0))==0 and
		session.open_quest(npc_spawn_id)
	)


func submit_quest_choice(choice_id:int)->bool:
	var view:Dictionary=_latest.get("quest_view",{})
	if (
		not active or choice_id<=0 or view.is_empty() or
		int(_latest.get("pending_quest_sequence",0))!=0 or
		bool(_latest.get("reconnecting",false)) or
		bool(_latest.get("resync_pending",false)) or
		bool(_latest.get("waiting_for_new_map",false)) or
		bool(_latest.get("player_dead",false))
	):
		return false
	for raw in view.get("choices",[]):
		if int((raw as Dictionary).get("choice_id",0))==choice_id:
			return session.choose_quest_choice(choice_id)
	return false


func submit_clan_create(name:String)->bool:
	if not active or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("clan_id",0))!=0 or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_create(name)


func submit_clan_join(clan_id:int)->bool:
	if not active or clan_id<=0 or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("clan_id",0))!=0 or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_join(clan_id)


func submit_clan_leave()->bool:
	if not active or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("clan_id",0))<=0 or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_leave()


func submit_clan_refresh()->bool:
	if not active or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_refresh()


func submit_clan_set_announcement(text:String)->bool:
	if not active or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("clan_id",0))<=0 or int(_latest.get("clan_leader_actor_id",0))!=int(_latest.get("actor_id",0)) or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_set_announcement(text)


func submit_clan_kick(target_actor_id:int)->bool:
	if not active or target_actor_id<=0 or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("clan_id",0))<=0 or int(_latest.get("clan_leader_actor_id",0))!=int(_latest.get("actor_id",0)) or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_kick(target_actor_id)


func submit_clan_transfer_leadership(target_actor_id:int)->bool:
	if not active or target_actor_id<=0 or int(_latest.get("state",-1))!=State.STATE_IN_WORLD or not bool(_latest.get("world_ready",false)) or int(_latest.get("clan_id",0))<=0 or int(_latest.get("clan_leader_actor_id",0))!=int(_latest.get("actor_id",0)) or int(_latest.get("pending_clan_sequence",0))!=0:
		return false
	return session.clan_transfer_leadership(target_actor_id)


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
	_show_create = false
	_chosen = false
	_world_epoch = 0
	_latest = {}
	gameplay_hud.reset()
	_last_publication_revision = -1
	_create_queued_after = -1
	_respawn_queued_after = -1
	_skill_queued_after = -1
	_skill_queued_at_ms = 0
	skill_target_mode.button_pressed = false
	skill_status.text = ""
	pickup_status.text = ""
	chat_panel.reset()
	party_panel.reset()
	quest_panel.reset()
	pvp_panel.reset()
	npc_shop_panel.reset()
	storage_panel.reset()
	bank_panel.reset()
	repair_panel.reset()
	enhancement_panel.reset()
	trade_panel.reset()
	player_shop_panel.reset()
	inventory_panel.reset()
	pet_panel.reset()
	mecha_panel.reset()
	_characters = []
	characters.clear()
	character_name_input.clear()
	character_create_status.text = ""
	password_input.clear()
	_set_controls()
	status.text = "已登出"
	if was_active:
		connection_changed.emit(false)


func _set_controls() -> void:
	username_input.editable = not active
	password_input.editable = not active
	login_button.disabled = active
	register_button.disabled = active
	logout_button.disabled = not active
	logout_button.visible = active
	var state := int(_latest.get("state",-1))
	var selecting := active and state == State.STATE_SELECTING_CHARACTER
	var selecting_page := active and state in [State.STATE_SELECTING_CHARACTER,State.STATE_MANAGING_CHARACTER] or (active and state == State.STATE_WAIT_CHARACTER_LIST and not _characters.is_empty())
	if _create_queued_after >= 0 and int(_latest.get("character_create_result_revision",0)) > _create_queued_after:
		_create_queued_after = -1
	var can_create := selecting and not _chosen and _create_queued_after < 0
	creation_options.set_choices(_latest.get("character_creation_appearances",[]))
	creation_options.set_editable(can_create)
	var creating := active and state == State.STATE_CREATING_CHARACTER
	characters.visible = selecting_page and not _characters.is_empty()
	choose_button.visible = characters.visible
	choose_button.disabled = _chosen or not selecting or _characters.is_empty() or _create_queued_after >= 0
	new_character_button.visible = selecting_page
	new_character_button.disabled = not can_create
	back_to_character_button.visible = selecting and not _characters.is_empty()
	back_to_character_button.disabled = not can_create
	for control in [character_name_input,character_slot_input,create_character_button,character_create_status]:
		control.visible = active and state in [State.STATE_SELECTING_CHARACTER,State.STATE_CREATING_CHARACTER]
	character_name_input.editable = can_create
	character_slot_input.editable = can_create
	create_character_button.disabled = not can_create
	_set_respawn_controls()
	_set_skill_controls()
	if not active or state not in [State.STATE_SELECTING_CHARACTER,State.STATE_MANAGING_CHARACTER,State.STATE_WAIT_CHARACTER_LIST,State.STATE_CREATING_CHARACTER,State.STATE_IN_WORLD] or (state == State.STATE_WAIT_CHARACTER_LIST and _characters.is_empty()):
		_layout.show_page(SessionLayout.Page.LOGIN)
	elif state == State.STATE_IN_WORLD:
		_layout.show_page(SessionLayout.Page.WORLD)
	elif state == State.STATE_CREATING_CHARACTER or (_show_create and selecting) or (_characters.is_empty() and selecting):
		_layout.show_page(SessionLayout.Page.CHARACTER_CREATE)
	else:
		_layout.show_page(SessionLayout.Page.CHARACTER_SELECT)
	_scroll.custom_minimum_size.y = maxf(220.0,get_viewport_rect().size.y-96.0) if _layout.current_page == SessionLayout.Page.WORLD else 0.0


func set_ui_layout(id: String) -> bool:
	return _layout.set_layout(id)

func apply_skin_art(profile: Dictionary) -> void:
	_artwork.apply(profile)


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



func _update_nearby_chat_ui() -> void:
	chat_panel.apply_snapshot(_latest, active, session)
	party_panel.apply_snapshot(_latest, active, session)


func _update_quest_ui() -> void:
	quest_panel.apply_snapshot(_latest, active)


func submit_pvp_mode(action: int) -> bool:
	return session.request_pvp_mode(action)


func submit_gm_pvp_name(target_actor_id:int,name_state:int)->bool:
	if (
		not active or not bool(_latest.get("gm_authorized",false)) or
		int(_latest.get("state",-1))!=State.STATE_IN_WORLD or
		not bool(_latest.get("world_ready",false)) or
		bool(_latest.get("reconnecting",false)) or
		bool(_latest.get("resync_pending",false)) or
		bool(_latest.get("waiting_for_new_map",false)) or
		int(_latest.get("pending_gm_sequence",0))!=0
	):
		return false
	var entities:Dictionary=_latest.get("entities",{})
	if (
		target_actor_id<=0 or target_actor_id==int(_latest.get("actor_id",0)) or
		not entities.has(target_actor_id) or name_state<0 or name_state>4
	):
		return false
	return session.gm_set_pvp_name(target_actor_id,name_state)


func _update_player_shop_ui() -> void:
	player_shop_panel.apply_snapshot(_latest, active, session, item_catalog)




func _process(delta: float) -> void:
	if not active:
		return
	_poll_elapsed += delta
	if _poll_elapsed < 0.05:
		return
	_poll_elapsed = 0.0
	# Skip full snapshot cloning and UI refresh when the logic thread has not
	# published a new state. WorldView animation continues on its own clock.
	var update: Dictionary = session.snapshot_since(_last_publication_revision)
	if update.is_empty():
		# Skill pending has a local 10-second status reminder even without
		# network changes; keep this cheap UI timer independent of snapshots.
		if _skill_queued_after >= 0:
			_set_skill_controls()
		return
	_last_publication_revision = int(update.revision)
	_latest = update.snapshot
	gameplay_hud.apply_snapshot(_latest)
	_set_respawn_controls()
	_set_skill_controls()
	_refresh_inventory_panel()
	pet_panel.apply_snapshot(_latest, active)
	mecha_panel.apply_snapshot(_latest, active)
	npc_shop_panel.apply_snapshot(_latest, active)
	storage_panel.apply_snapshot(_latest, active)
	bank_panel.apply_snapshot(_latest, active)
	repair_panel.apply_snapshot(_latest, active)
	enhancement_panel.apply_snapshot(_latest, active)
	_refresh_trade_panel()
	_update_player_shop_ui()
	_update_nearby_chat_ui()
	_update_quest_ui()
	pvp_panel.apply_snapshot(_latest, active)
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
		var registration_code:=int(_latest.get("registration_status",-1))
		disconnect_session()
		status.text=_registration_status_text(registration_code) if registration_code>0 else "連線或登入失敗，請確認帳號與伺服器憑證後重試"
		return
	# Refresh controls on every published state transition, even when the
	# authenticated account's character list is still empty. Otherwise a
	# first-time player never sees the creation fields or button.
	_set_controls()
	if state == State.STATE_SELECTING_CHARACTER:
		if _chosen and int(_latest.choose_status) >= 0:
			var choose_code := int(_latest.choose_status)
			if choose_code == 1:
				disconnect_session()
				status.text = "角色不屬於此帳號，請重新登入"
				return
			_chosen = false
			choose_button.disabled = false
			match choose_code:
				3:
					status.text = "請先設定或輸入 4 位角色密碼"
				4:
					status.text = "角色密碼錯誤"
				5:
					status.text = "角色密碼錯誤次數過多，暫時鎖定"
				_:
					status.text = "角色暫時無法使用"
			_set_controls()
		if _latest.characters != _characters:
			_characters = _latest.characters
			characters.clear()
			var created_index := -1
			var created_id := int(_latest.get("last_created_character_id",0))
			var used_slots := {}
			for entry in _characters:
				characters.add_item(str(entry.name))
				used_slots[int(entry.slot)] = true
				if int(entry.character_id) == created_id:
					created_index = characters.item_count - 1
			if created_index >= 0:
				characters.select(created_index)
				character_name_input.clear()
				_show_create = false
			if used_slots.has(int(character_slot_input.value)):
				for slot in range(8):
					if not used_slots.has(slot):
						character_slot_input.value = slot
						break
			_set_controls()
		var create_code := int(_latest.get("character_create_status",-1))
		if create_code >= 0:
			character_create_status.text = _character_create_status_text(
				create_code,str(_latest.get("last_created_name","")))
		if not _chosen and int(_latest.get("choose_status",-1)) < 0 and int(_latest.get("character_manage_result_revision",0)) == 0:
			status.text = "請選擇角色或建立新角色"
		var manage_revision := int(_latest.get("character_manage_result_revision",0))
		if manage_revision > 0 and manage_revision > _last_manage_revision:
			_last_manage_revision = manage_revision
			var manage_status := int(_latest.get("character_manage_status",-1))
			var action := int(_latest.get("character_manage_action",0))
			status.text = _character_manage_status_text(manage_status,action)
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




func _registration_status_text(code:int)->String:
	match code:
		1:
			return "伺服器未開放公開帳號註冊"
		2:
			return "帳號名稱已被使用"
		3:
			return "帳號或密碼不符合伺服器規則"
		4:
			return "建立帳號請求過於頻繁，請稍後重試"
		_:
			return "建立帳號失敗"


func _character_manage_status_text(code: int, action: int) -> String:
	match code:
		0: return "角色密碼設定成功" if action == 1 else "角色已刪除"
		1: return "此角色不屬於目前帳號"
		2: return "第二組密碼錯誤"
		3: return "第二組密碼錯誤次數過多，暫時鎖定"
		4: return "請先設定角色第二組密碼，或檢查輸入格式"
		5: return "角色資料已更新，請重新操作"
		6: return "角色含交易或管理歷史，為保護資料暫不允許刪除"
		_: return "角色操作失敗"

func _character_create_status_text(code: int, character_name: String) -> String:
	match code:
		0:
			return "角色「%s」建立成功，可直接進入世界" % character_name
		1:
			return "這個角色欄位已被使用，請換一個欄位"
		2:
			return "角色名稱已被使用"
		3:
			return "角色數量已達上限"
		4:
			return "角色名稱格式不合法"
		5:
			return "角色欄位不合法"
		6:
			return "伺服器尚未開放遊戲內創角"
		7:
			return "角色外觀不符合伺服器創角規則，請重新選擇"
		8:
			return "能力點或 4 位角色密碼不符合創角規則"
		_:
			return ""

func _exit_tree() -> void:
	session.stop()


func _refresh_trade_panel() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	trade_panel.apply_snapshot(_latest, active, in_world, session, item_catalog)


func _refresh_inventory_panel() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	inventory_panel.apply_snapshot(_latest, active, in_world, session, item_catalog)
