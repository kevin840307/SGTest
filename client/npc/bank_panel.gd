extends VBoxContainer
## NPC bank UI owner. Displays Server snapshots and submits existing authenticated intents only.
const State = preload("res://world/world_state.gd")

var active := false
var _latest: Dictionary = {}
var _open_action: Callable
var _deposit_action: Callable
var _withdraw_action: Callable
var bank_npc_input := SpinBox.new()
var bank_open_button := Button.new()
var bank_scope_label := Label.new()
var bank_wallet_label := Label.new()
var bank_balance_label := Label.new()
var bank_amount_input := SpinBox.new()
var bank_deposit_button := Button.new()
var bank_withdraw_button := Button.new()
var bank_status := Label.new()
var _bank_display_key := ""

func _ready() -> void:
	bank_npc_input.min_value = 1
	bank_npc_input.max_value = 2147483647
	bank_npc_input.step = 1
	bank_npc_input.value = 1
	bank_open_button.text = "開啟附近 NPC 銀行"
	bank_amount_input.min_value = 1
	bank_amount_input.max_value = 9000000000000000
	bank_amount_input.step = 1
	bank_amount_input.value = 1
	bank_deposit_button.text = "存入金錢"
	bank_withdraw_button.text = "領出金錢"
	bank_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [bank_npc_input,bank_open_button,bank_scope_label,bank_wallet_label,bank_balance_label,bank_amount_input,bank_deposit_button,bank_withdraw_button,bank_status]:
		control.visible = false
		add_child(control)
	bank_open_button.pressed.connect(_open_bank_from_input)
	bank_deposit_button.pressed.connect(_deposit_bank_amount)
	bank_withdraw_button.pressed.connect(_withdraw_bank_amount)
	reset()

func set_actions(open_action: Callable, deposit_action: Callable, withdraw_action: Callable) -> void:
	_open_action = open_action
	_deposit_action = deposit_action
	_withdraw_action = withdraw_action

func reset() -> void:
	active = false
	_latest = {}
	_bank_display_key = ""
	bank_status.text = ""
	bank_scope_label.text = "銀行範圍：—"
	bank_wallet_label.text = "角色錢包：—"
	bank_balance_label.text = "銀行餘額：—"

func apply_snapshot(snapshot: Dictionary, connection_active: bool) -> void:
	_latest = snapshot
	active = connection_active
	_update_bank_ui()

func _open_bank_from_input() -> void:
	if bank_open_button.disabled:
		return
	if _open_action.call(int(bank_npc_input.value)):
		bank_status.text = "銀行請求已送出，等待伺服器核准範圍與餘額…"
		bank_open_button.disabled = true
	else:
		bank_status.text = "目前無法開啟該銀行"


func _deposit_bank_amount() -> void:
	if bank_deposit_button.disabled:
		return
	var amount := int(bank_amount_input.value)
	if _deposit_action.call(amount):
		bank_status.text = "存入請求已送出，等待伺服器結算…"
		bank_deposit_button.disabled = true
		bank_withdraw_button.disabled = true
	else:
		bank_status.text = "目前無法存入該金額"


func _withdraw_bank_amount() -> void:
	if bank_withdraw_button.disabled:
		return
	var amount := int(bank_amount_input.value)
	if _withdraw_action.call(amount):
		bank_status.text = "領出請求已送出，等待伺服器結算…"
		bank_deposit_button.disabled = true
		bank_withdraw_button.disabled = true
	else:
		bank_status.text = "目前無法領出該金額"


func _bank_scope_text(scope: int) -> String:
	match scope:
		1:
			return "角色專屬（伺服器規則）"
		2:
			return "帳號共用（伺服器規則）"
		_:
			return "—"


func _update_bank_ui() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [bank_npc_input,bank_open_button,bank_scope_label,bank_wallet_label,bank_balance_label,bank_amount_input,bank_deposit_button,bank_withdraw_button,bank_status]:
		control.visible = in_world
	if not in_world:
		_bank_display_key = ""
		return
	var active_npc := int(_latest.get("active_bank_npc",0))
	var scope := int(_latest.get("bank_scope",0))
	var wallet := int(_latest.get("bank_wallet_money",0))
	var balance := int(_latest.get("bank_money",0))
	var pending := int(_latest.get("pending_bank_sequence",0)) != 0
	var usable := bool(_latest.get("world_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0))
	bank_open_button.disabled = not usable or pending
	bank_scope_label.text = "銀行範圍：%s" % _bank_scope_text(scope)
	bank_wallet_label.text = "角色錢包：%d" % wallet if active_npc > 0 else "角色錢包：—"
	bank_balance_label.text = "銀行餘額：%d" % balance if active_npc > 0 else "銀行餘額：—"
	var amount := int(bank_amount_input.value)
	bank_deposit_button.disabled = not usable or pending or active_npc <= 0 or scope not in [1,2] or amount <= 0 or amount > wallet
	bank_withdraw_button.disabled = not usable or pending or active_npc <= 0 or scope not in [1,2] or amount <= 0 or amount > balance
	var open_status := int(_latest.get("last_bank_open_status",-1))
	if open_status > 0 and active_npc == 0:
		bank_status.text = "銀行無法開啟（伺服器代碼 %d）" % open_status
	var result: Dictionary = _latest.get("last_bank_result",{})
	if not result.is_empty():
		if int(result.get("status",-1)) in [0,1]:
			bank_status.text = "%s完成：%d｜錢包 %d｜銀行 %d" % ["存入" if int(result.get("kind",0)) == 1 else "領出",int(result.get("amount",0)),int(result.get("wallet_money",0)),int(result.get("bank_money",0))]
		else:
			bank_status.text = "銀行操作失敗（伺服器代碼 %d）" % int(result.get("status",-1))
