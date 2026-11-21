extends RefCounted

# Stateless admission policy for the ONE ClientSession FIFO. No socket, mutex,
# queue, WorldState ownership, timers or authoritative gameplay decisions.
# Keep the existing asymmetric blockers: queued NPC-open actions do not block
# recovery/discard, while recovery/discard still block new NPC-open actions.
const ECONOMY_QUEUED := [
	"shop_open", "shop_buy", "shop_sell",
	"storage_open", "storage_deposit", "storage_withdraw",
	"equipment_equip", "equipment_unequip",
	"bank_open", "bank_deposit", "bank_withdraw",
	"repair_open", "repair_item",
	"use_recovery_item", "discard_inventory_item",
]
const ITEM_QUEUED := [
	"use_recovery_item", "discard_inventory_item",
	"shop_buy", "shop_sell", "storage_deposit", "storage_withdraw",
	"equipment_equip", "equipment_unequip",
	"bank_deposit", "bank_withdraw", "repair_open", "repair_item",
]

const SHOP_AND_ITEM_PENDING := [
	"pending_shop_sequence", "pending_storage_sequence",
	"pending_equipment_sequence", "pending_bank_sequence",
	"pending_repair_sequence", "trade_id",
]
const STORAGE_PENDING := [
	"pending_storage_sequence", "pending_shop_sequence",
	"pending_bank_sequence", "pending_repair_sequence", "trade_id",
]
const EQUIPMENT_PENDING := [
	"pending_equipment_sequence", "pending_shop_sequence",
	"pending_storage_sequence", "pending_bank_sequence",
	"pending_repair_sequence", "pending_recovery_sequence",
	"pending_discard_sequence",
]
const BANK_REPAIR_PENDING := [
	"pending_bank_sequence", "pending_repair_sequence",
	"pending_shop_sequence", "pending_storage_sequence",
	"pending_equipment_sequence", "pending_recovery_sequence",
	"pending_discard_sequence",
]

# Does not mutate queued or published. Called under ClientSession's mutex
# after navigation-intent supersession, before appending a new token.
static func allows(kind: String, published: Dictionary, queued: Array[Dictionary]) -> bool:
	var pending: Array = []
	var conflicts: Array = []
	match kind:
		"use_recovery_item", "discard_inventory_item":
			pending = SHOP_AND_ITEM_PENDING
			conflicts = ITEM_QUEUED
		"shop_open", "shop_buy", "shop_sell":
			pending = SHOP_AND_ITEM_PENDING
			conflicts = ECONOMY_QUEUED
		"storage_open", "storage_deposit", "storage_withdraw":
			pending = STORAGE_PENDING
			conflicts = ECONOMY_QUEUED
		"equipment_equip", "equipment_unequip":
			pending = EQUIPMENT_PENDING
			conflicts = ECONOMY_QUEUED
		"bank_open", "bank_deposit", "bank_withdraw":
			pending = BANK_REPAIR_PENDING
			conflicts = ECONOMY_QUEUED
		"repair_open", "repair_item":
			pending = BANK_REPAIR_PENDING
			conflicts = ECONOMY_QUEUED
		"player_shop":
			pending = ["pending_player_shop_sequence", "trade_id"]
			conflicts = ["player_shop"]
		"quest_open", "quest_choose":
			pending = ["pending_quest_sequence"]
			conflicts = ["quest_open", "quest_choose"]
		"pvp_mode":
			pending = ["pending_pvp_sequence"]
			conflicts = ["pvp_mode"]
		"gm_pvp_name":
			if not bool(published.get("gm_authorized", false)):
				return false
			pending = ["pending_gm_sequence"]
			conflicts = ["gm_pvp_name"]
		"pvp_attack":
			pending = ["pending_pvp_attack_sequence"]
			conflicts = ["pvp_attack"]
		"cast_skill":
			pending = ["pending_skill_sequence"]
			conflicts = ["cast_skill"]
		"pickup_ground_drop":
			conflicts = ["pickup_ground_drop"]
		"stop_move":
			conflicts = ["stop_move"]
		_:
			return true

	for key in pending:
		if int(published.get(key, 0)) != 0:
			return false
	for item in queued:
		if str(item.get("type", "")) in conflicts:
			return false
	return true
