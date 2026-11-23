extends VBoxContainer
## View-only player-shop controls; all ownership, UID, wallet, and settlement remain server authoritative.
const State = preload("res://world/world_state.gd")
const PLAYER_SHOP_MAX_LISTINGS := 16 # SGTest safety bound; exact HK capacity is NOT VERIFIED.

var _latest: Dictionary = {}
var active := false
var session
var item_catalog
var inventory_picker := OptionButton.new()
var price_input := SpinBox.new()
var open_button := Button.new()
var own_listing_label := Label.new()
var own_listing_picker := OptionButton.new()
var close_button := Button.new()
var seller_picker := OptionButton.new()
var inspect_button := Button.new()
var inspected_listing_label := Label.new()
var inspected_listing_picker := OptionButton.new()
var buy_button := Button.new()
var status_label := Label.new()
var _display_key := ""

func _ready() -> void:
	price_input.min_value = 1
	price_input.max_value = 9000000000000000
	price_input.step = 1
	price_input.value = 1
	open_button.text = "新增商品到玩家商店"
	own_listing_label.text = "我的商店：—"
	close_button.text = "下架選中商品"
	inspect_button.text = "查看選中玩家商店"
	inspected_listing_label.text = "查看中的商店：—"
	inspected_listing_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	buy_button.text = "購買選中商品"
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for control in [inventory_picker,price_input,open_button,own_listing_label,own_listing_picker,close_button,seller_picker,inspect_button,inspected_listing_label,inspected_listing_picker,buy_button,status_label]:
		control.visible = false
	for control in [inventory_picker,price_input,open_button,own_listing_label,own_listing_picker,close_button,seller_picker,inspect_button,inspected_listing_label,inspected_listing_picker,buy_button,status_label]:
		add_child(control)
	open_button.pressed.connect(_open_shop)
	close_button.pressed.connect(_close_shop)
	inspect_button.pressed.connect(_inspect_shop)
	buy_button.pressed.connect(_buy_listing)

func reset() -> void:
	_latest = {}
	active = false
	_display_key = ""
	status_label.text = ""
	inventory_picker.clear()
	own_listing_picker.clear()
	seller_picker.clear()
	inspected_listing_picker.clear()


func apply_snapshot(snapshot: Dictionary, connection_active: bool, session_ref, catalog_ref) -> void:
	_latest = snapshot
	active = connection_active
	session = session_ref
	item_catalog = catalog_ref
	_refresh()


func _open_shop() -> void:
	if open_button.disabled or inventory_picker.selected < 0:
		return
	var uid := int(inventory_picker.get_item_metadata(inventory_picker.selected))
	var price := int(price_input.value)
	if session.open_player_shop(uid,price):
		status_label.text = "開店請求已送出；物品仍由伺服器保有權威，售出前不會從背包移除"


func _close_shop() -> void:
	if close_button.disabled or own_listing_picker.selected < 0:
		return
	var listing_id := int(own_listing_picker.get_item_metadata(own_listing_picker.selected))
	if session.close_player_shop(listing_id):
		status_label.text = "下架請求已送出，等待伺服器確認…"


func _inspect_shop() -> void:
	if inspect_button.disabled or seller_picker.selected < 0:
		return
	var seller := int(seller_picker.get_item_metadata(seller_picker.selected))
	if session.inspect_player_shop(seller):
		status_label.text = "正在向伺服器查詢玩家 %d 的商店…" % seller


func _buy_listing() -> void:
	if buy_button.disabled or inspected_listing_picker.selected < 0:
		return
	var listing_id := int(inspected_listing_picker.get_item_metadata(inspected_listing_picker.selected))
	var listings: Dictionary = _latest.get("player_shop_inspected_listings",{})
	var listing: Dictionary = listings.get(listing_id,{})
	var seller := int(listing.get("seller_actor_id",0))
	if session.buy_player_shop(seller,listing_id):
		status_label.text = "購買請求已送出；UID、價格與雙方錢包只接受伺服器原子結算結果"


func _format_listing(prefix: String, listing: Dictionary) -> String:
	if listing.is_empty():
		return prefix + "—"
	var item_name: String = item_catalog.name_for(int(listing.get("item_definition_id",0)))
	var durability := int(listing.get("durability_raw",-1))
	var durability_text := "" if durability < 0 else "｜耐久 raw %d" % durability
	var level := int(listing.get("enhancement_level",0))
	var enhancement_text := "" if level <= 0 else "｜+%d" % level
	return "%s玩家 %d｜%s ×%d%s｜UID %d｜售價 %d%s" % [
		prefix,int(listing.get("seller_actor_id",0)),item_name,
		int(listing.get("quantity",0)),enhancement_text,
		int(listing.get("item_uid",0)),int(listing.get("price",0)),durability_text]

func _refresh() -> void:
	var in_world := active and int(_latest.get("state",-1)) == State.STATE_IN_WORLD
	for control in [inventory_picker,price_input,open_button,own_listing_label,own_listing_picker,close_button,seller_picker,inspect_button,inspected_listing_label,inspected_listing_picker,buy_button,status_label]:
		control.visible = in_world
	if not in_world:
		_display_key = ""
		return
	var usable := bool(_latest.get("world_ready",false)) and bool(_latest.get("inventory_ready",false)) and not bool(_latest.get("player_dead",false)) and not bool(_latest.get("reconnecting",false)) and not bool(_latest.get("resync_pending",false)) and not bool(_latest.get("waiting_for_new_map",false)) and int(_latest.get("pending_world_ready_ack_epoch",0)) == 0 and int(_latest.get("last_world_ready_ack_epoch",0)) == int(_latest.get("map_epoch",0)) and int(_latest.get("pending_player_shop_sequence",0)) == 0 and int(_latest.get("trade_id",0)) == 0
	var own: Dictionary = _latest.get("player_shop_own_listing",{})
	var own_listings: Dictionary = _latest.get("player_shop_own_listings",{})
	var inspected: Dictionary = _latest.get("player_shop_inspected",{})
	var inspected_listings: Dictionary = _latest.get("player_shop_inspected_listings",{})
	var entities: Dictionary = _latest.get("entities",{})
	var entity_ids: Array = entities.keys()
	entity_ids.sort()
	var entity_key := ""
	for raw_actor in entity_ids:
		entity_key += "," + str(int(raw_actor))
	var key := "%d/%d/%d/%d/%d/%d/%d/%d/%s" % [
		int(_latest.get("inventory_revision",0)),int(_latest.get("player_shop_sequence",0)),
		int(_latest.get("last_player_shop_result_sequence",0)),
		int(_latest.get("last_player_shop_sold_listing_id",0)),
		own_listings.size(),inspected_listings.size(),
		int(own.get("listing_id",0)),int(inspected.get("listing_id",0)),entity_key]
	if key != _display_key:
		_display_key = key
		var listed_uids := {}
		own_listing_picker.clear()
		var own_ids: Array = own_listings.keys()
		own_ids.sort()
		for raw_listing_id in own_ids:
			var listing_id := int(raw_listing_id)
			var listing: Dictionary = own_listings[raw_listing_id]
			listed_uids[int(listing.get("item_uid",0))] = true
			own_listing_picker.add_item(_format_listing("",listing))
			own_listing_picker.set_item_metadata(own_listing_picker.item_count-1,listing_id)
		if own_listing_picker.item_count > 0:
			own_listing_picker.select(0)

		inventory_picker.clear()
		var inventory: Dictionary = _latest.get("inventory_items",{})
		var uids: Array = inventory.keys()
		uids.sort()
		for raw_uid in uids:
			var uid := int(raw_uid)
			if listed_uids.has(uid):
				continue
			var item: Dictionary = inventory[raw_uid]
			inventory_picker.add_item(item_catalog.offer_item_text(item))
			inventory_picker.set_item_metadata(inventory_picker.item_count-1,uid)
		if inventory_picker.item_count > 0:
			inventory_picker.select(0)

		seller_picker.clear()
		for raw_actor in entity_ids:
			var actor_id := int(raw_actor)
			if actor_id <= 0 or actor_id == int(_latest.get("actor_id",0)):
				continue
			seller_picker.add_item("玩家 %d" % actor_id)
			seller_picker.set_item_metadata(seller_picker.item_count-1,actor_id)
		if seller_picker.item_count > 0:
			seller_picker.select(0)

		inspected_listing_picker.clear()
		var inspected_ids: Array = inspected_listings.keys()
		inspected_ids.sort()
		for raw_listing_id in inspected_ids:
			var listing_id := int(raw_listing_id)
			var listing: Dictionary = inspected_listings[raw_listing_id]
			inspected_listing_picker.add_item(_format_listing("",listing))
			inspected_listing_picker.set_item_metadata(
				inspected_listing_picker.item_count-1,listing_id)
		if inspected_listing_picker.item_count > 0:
			inspected_listing_picker.select(0)

	own_listing_label.text = "我的商店：%d / %d 件" % [own_listings.size(),PLAYER_SHOP_MAX_LISTINGS]
	inspected_listing_label.text = "查看中的商店：%d 件" % inspected_listings.size()
	open_button.disabled = not usable or own_listings.size() >= PLAYER_SHOP_MAX_LISTINGS or inventory_picker.item_count == 0
	price_input.editable = usable and own_listings.size() < PLAYER_SHOP_MAX_LISTINGS
	close_button.disabled = not usable or own_listing_picker.item_count == 0
	inspect_button.disabled = not usable or seller_picker.item_count == 0
	var inspected_seller := int(inspected.get("seller_actor_id",0))
	buy_button.disabled = not usable or inspected_listing_picker.item_count == 0 or inspected_seller <= 0 or not entities.has(inspected_seller)
	if int(_latest.get("pending_player_shop_sequence",0)) > 0:
		status_label.text = "等待伺服器玩家商店結果…"
	else:
		var result: Dictionary = _latest.get("last_player_shop_result",{})
		if not result.is_empty():
			var code := int(result.get("status",-1))
			var action := int(result.get("action",0))
			if code in [0,1]:
				status_label.text = "玩家商店操作完成｜動作 %d｜伺服器代碼 %d｜錢包 %d" % [action,code,int(_latest.get("player_shop_wallet",0))]
			elif code == 16:
				status_label.text = "玩家商店已達目前 SGTest 安全上限 %d 件" % PLAYER_SHOP_MAX_LISTINGS
			else:
				status_label.text = "玩家商店操作未完成（伺服器代碼 %d）" % code
	var sold: Dictionary = _latest.get("last_player_shop_sold",{})
	if not sold.is_empty() and int(_latest.get("last_player_shop_sold_listing_id",0)) > 0:
		status_label.text = "商店物品已由伺服器售出｜買家 %d｜UID %d｜錢包 %d" % [int(sold.get("buyer_actor_id",0)),int(sold.get("item_uid",0)),int(_latest.get("player_shop_wallet",0))]
