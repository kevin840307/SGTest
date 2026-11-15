extends RefCounted

# Player paper-doll parts remain independent runtime assets, matching the
# original game's identity. They share one AssetStore owner/lifetime but are
# never physically merged into a player/session atlas.
const ATLAS = preload("res://scripts/assets/entity_atlas.gd")
const ACTION = preload("res://scripts/gameplay/action_policy.gd")
const PARTS := {
	"body":{"name":"body","path":"players/b/base/body/atlas.json"},
	"leg":{"name":"leg","path":"players/b/base/leg/atlas.json"},
	"head":{"name":"head","path":"players/b/base/head/atlas.json"},
	"hair":{"name":"hair00","path":"players/b/hair/hair00/atlas.json"},
	"l-arm":{"name":"l-arm","path":"players/b/base/left_arm/atlas.json"},
	"r-arm":{"name":"r-arm","path":"players/b/base/right_arm/atlas.json"},
}
const DEATH_RELATIVE := "players/b/base/death/atlas.json"

var parts: Dictionary = {}
var death: RefCounted

var store: RefCounted
var store_owner := ""
var store_epoch := 0
var root := ""
var paths: Dictionary = {}

func reset(force_purge: bool = false) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	parts.clear()
	paths.clear()
	death = null
	store = null
	store_owner = ""
	store_epoch = 0
	root = ""

func _part_path(key: String) -> String:
	return root.path_join(str(PARTS[key].path))

func open(asset_root: String) -> Error:
	reset(false)
	root = asset_root
	for key in PARTS:
		var atlas := ATLAS.new()
		var error: Error = atlas.open(_part_path(key))
		if error != OK:
			return error
		parts[key] = atlas
	var death_atlas := ATLAS.new()
	var death_error: Error = death_atlas.open(root.path_join(DEATH_RELATIVE))
	if death_error != OK:
		return death_error
	death = death_atlas
	return OK

func begin_async(value: RefCounted, asset_root: String, owner: String = "player-base") -> Error:
	if value == null or asset_root.is_empty() or owner.is_empty():
		return ERR_INVALID_PARAMETER
	reset(false)
	store = value
	root = asset_root
	store_owner = owner
	store_epoch = store.begin_owner(owner)
	if store_epoch <= 0:
		return ERR_INVALID_DATA
	for key in PARTS:
		var path := _part_path(key)
		paths[key] = path
		var error: Error = store.acquire(path,store_owner,store_epoch,1000)
		if error != OK:
			return error
	var death_path := root.path_join(DEATH_RELATIVE)
	paths["death"] = death_path
	return store.acquire(death_path,store_owner,store_epoch,1000)

func refresh() -> void:
	if store == null:
		return
	for key in PARTS:
		if parts.has(key):
			continue
		var path := str(paths.get(key,""))
		if not path.is_empty() and store.is_ready(path):
			var atlas: RefCounted = store.get_atlas(path)
			if atlas != null:
				parts[key] = atlas
	if death == null:
		var death_path := str(paths.get("death",""))
		if not death_path.is_empty() and store.is_ready(death_path):
			death = store.get_atlas(death_path)

func first_error() -> Error:
	if store == null:
		return OK
	for path in paths.values():
		if store.is_failed(str(path)):
			return store.error(str(path))
	return OK

func is_ready() -> bool:
	refresh()
	return parts.size() == PARTS.size() and death != null

func _draw_order(body: Dictionary) -> Array[String]:
	var order: Array[String] = []
	var layers: Variant = body.get("draw_layers")
	if layers is Array:
		for raw in layers:
			var part := str(raw).split("+")[0].strip_edges().to_lower()
			if PARTS.has(part) and part not in order:
				order.append(part)
	if order.is_empty():
		return ["leg","r-arm","body","l-arm","head","hair"]
	return order

func draw(canvas: CanvasItem, at: Vector2, pose: int, facing: int, seconds: float) -> void:
	if not is_ready():
		return
	if pose == 4:
		death.draw_frame(canvas,at,"B-DEATH_ND1",seconds,8.0,ACTION.DEATH_SECONDS)
		return
	var action: String = ["NS","NR","AA","AH","NS"][clampi(pose,0,4)]
	var requested := "%s%d" % [action,facing]
	var body_atlas: RefCounted = parts["body"]
	var body: Dictionary = body_atlas.sample("B-BODY_"+requested,seconds)
	if body.is_empty():
		return
	var order := _draw_order(body)
	canvas.draw_set_transform(at,0,Vector2(1,0.3))
	canvas.draw_circle(Vector2.ZERO,16,Color(0,0,0,0.3))
	canvas.draw_set_transform(Vector2.ZERO)
	for part in order:
		var atlas: RefCounted = parts.get(part)
		if atlas == null:
			continue
		var prefix: String = ("B-"+str(PARTS[part].name)).to_upper()
		atlas.draw_frame(canvas,at,prefix+"_"+requested,seconds,8.0,
			ACTION.COMBAT_SECONDS if pose >= 2 else 0.0)
