extends RefCounted

# Bare Boy composition. All player instances share these immutable atlases.
# The production path can acquire them from RuntimeAssetStore without blocking
# startup/network. open() remains the deterministic synchronous fixture path.
const ATLAS = preload("res://scripts/entity_atlas.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const PARTS := {"body":"body", "leg":"leg", "head":"head", "hair":"hair00", "l-arm":"l-arm", "r-arm":"r-arm"}
var parts: Dictionary = {}
var orders: Dictionary = {}
var death: RefCounted

var store: RefCounted
var store_owner := ""
var store_epoch := 0
var root := ""
var pending: Dictionary = {}
var loaded: Dictionary = {}
var death_path := ""

func _load_orders(asset_root: String, available: Dictionary) -> Error:
	var result: Dictionary = {}
	for line in FileAccess.get_file_as_string(asset_root.path_join("data/player/chardraworder.dat")).split("\n"):
		var separator := line.find(":")
		if separator < 0:
			continue
		var order: Array[String] = []
		for token in line.substr(separator+1).strip_edges().split(">"):
			var part: String = token.split("+")[0].strip_edges().to_lower()
			if available.has(part):
				order.append(part)
		result[line.substr(0,separator).strip_edges()] = order
	if result.is_empty():
		return ERR_FILE_CORRUPT
	orders = result
	return OK

func reset(force_purge: bool = false) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	parts.clear()
	orders.clear()
	death = null
	pending.clear()
	loaded.clear()
	death_path = ""
	store_owner = ""
	store_epoch = 0
	root = ""

func open(asset_root: String) -> Error:
	reset(false)
	var sync_loaded: Dictionary = {}
	for key in PARTS:
		var atlas := ATLAS.new()
		var error: Error = atlas.open(asset_root.path_join("player/b/standard/b-%s.json" % PARTS[key]))
		if error != OK:
			return error
		sync_loaded[key] = atlas
	var death_atlas := ATLAS.new()
	var death_error: Error = death_atlas.open(asset_root.path_join("player/b/standard/b-death.json"))
	if death_error != OK:
		return death_error
	var order_error := _load_orders(asset_root, sync_loaded)
	if order_error != OK:
		return order_error
	parts = sync_loaded
	death = death_atlas
	return OK

func begin_async(value: RefCounted, asset_root: String, owner: String = "player-core") -> Error:
	if value == null or asset_root.is_empty() or owner.is_empty():
		return ERR_INVALID_PARAMETER
	reset(false)
	store = value
	store_owner = owner
	store_epoch = store.begin_owner(owner)
	root = asset_root
	var available: Dictionary = {}
	for key in PARTS:
		available[key] = true
	var order_error := _load_orders(asset_root, available)
	if order_error != OK:
		return order_error
	for key in PARTS:
		var path := asset_root.path_join("player/b/standard/b-%s.json" % PARTS[key])
		var error: Error = store.acquire(path, store_owner, store_epoch, 1000)
		if error != OK:
			return error
		pending[key] = path
	death_path = asset_root.path_join("player/b/standard/b-death.json")
	var death_error: Error = store.acquire(death_path, store_owner, store_epoch, 1000)
	if death_error != OK:
		return death_error
	return OK

func refresh() -> void:
	if store == null or store_owner.is_empty():
		return
	for key in pending.keys().duplicate():
		var path := str(pending[key])
		if store.is_ready(path):
			var atlas: RefCounted = store.get_atlas(path)
			if atlas != null:
				loaded[key] = atlas
				pending.erase(key)
	if death == null and not death_path.is_empty() and store.is_ready(death_path):
		death = store.get_atlas(death_path)
	if pending.is_empty() and death != null and parts.is_empty():
		parts = loaded.duplicate()

func first_error() -> Error:
	if store == null:
		return OK
	for path in pending.values():
		if store.is_failed(str(path)):
			return store.error(str(path))
	if death == null and not death_path.is_empty() and store.is_failed(death_path):
		return store.error(death_path)
	return OK

func is_ready() -> bool:
	return not parts.is_empty() and death != null and pending.is_empty()

func draw(canvas: CanvasItem, at: Vector2, pose: int, facing: int, seconds: float) -> void:
	if parts.is_empty():
		return
	if pose == 4:
		if death != null:
			# BaseEntity's original death policy always uses the first source view.
			death.draw_frame(canvas,at,"B-DEATH_ND1",seconds,8.0,ACTION.DEATH_SECONDS)
		return
	var action: String = ["NS","NR","AA","AH","NS"][clampi(pose,0,4)]
	var requested := "%s%d" % [action,facing]
	var body: Dictionary = parts.body.sample("B-BODY_"+requested, seconds)
	if body.is_empty():
		return
	var suffix: String = str(body.clip).get_slice("_",1)
	var order: Array = orders.get(suffix, ["leg","r-arm","body","l-arm","head","hair"])
	canvas.draw_set_transform(at,0,Vector2(1,0.3))
	canvas.draw_circle(Vector2.ZERO,16,Color(0,0,0,0.3))
	canvas.draw_set_transform(Vector2.ZERO)
	for part in order:
		var prefix: String = ("B-"+str(PARTS[part])).to_upper()
		parts[part].draw_frame(canvas,at,prefix+"_"+requested,seconds,8.0,
			ACTION.COMBAT_SECONDS if pose >= 2 else 0.0)
