extends RefCounted

# One session-lifetime base-avatar bundle. Parts are logical clip prefixes inside
# one EntityAtlas; body/leg/head/hair/arms/death are not independent unload units.
const ATLAS = preload("res://scripts/entity_atlas.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const PARTS := {"body":"body", "leg":"leg", "head":"head", "hair":"hair00", "l-arm":"l-arm", "r-arm":"r-arm"}
const BUNDLE_RELATIVE := "bundles/session/player-b-base.json"

var parts: Dictionary = {}
var death: RefCounted

var store: RefCounted
var store_owner := ""
var store_epoch := 0
var bundle_path := ""
var bundle: RefCounted

func reset(force_purge: bool = false) -> void:
	if store != null and not store_owner.is_empty():
		store.release_owner(store_owner)
		if force_purge:
			store.purge_unowned(true)
	parts.clear()
	death = null
	bundle = null
	bundle_path = ""
	store_owner = ""
	store_epoch = 0

func _bind(value: RefCounted) -> void:
	bundle = value
	parts.clear()
	for key in PARTS:
		parts[key] = value
	death = value

func open(asset_root: String) -> Error:
	reset(false)
	var atlas := ATLAS.new()
	var error: Error = atlas.open(asset_root.path_join(BUNDLE_RELATIVE))
	if error != OK:
		return error
	_bind(atlas)
	return OK

func begin_async(value: RefCounted, asset_root: String, owner: String = "player-base") -> Error:
	if value == null or asset_root.is_empty() or owner.is_empty():
		return ERR_INVALID_PARAMETER
	reset(false)
	store = value
	store_owner = owner
	store_epoch = store.begin_owner(owner)
	if store_epoch <= 0:
		return ERR_INVALID_DATA
	bundle_path = asset_root.path_join(BUNDLE_RELATIVE)
	return store.acquire(bundle_path, store_owner, store_epoch, 1000)

func refresh() -> void:
	if store == null or bundle != null or bundle_path.is_empty():
		return
	if store.is_ready(bundle_path):
		var atlas: RefCounted = store.get_atlas(bundle_path)
		if atlas != null:
			_bind(atlas)

func first_error() -> Error:
	if store == null or bundle_path.is_empty():
		return OK
	return store.error(bundle_path) if bundle == null and store.is_failed(bundle_path) else OK

func is_ready() -> bool:
	return bundle != null and parts.size() == PARTS.size() and death != null

func _draw_order(body: Dictionary) -> Array[String]:
	var order: Array[String] = []
	var layers: Variant = body.get("draw_layers")
	if layers is Array:
		for raw in layers:
			var part := str(raw).split("+")[0].strip_edges().to_lower()
			if PARTS.has(part) and part not in order:
				order.append(part)
	# Normalized v2 must normally provide draw layers. Keep this tiny fallback
	# only for synthetic/legacy fixtures; production does not read a second table.
	if order.is_empty():
		return ["leg","r-arm","body","l-arm","head","hair"]
	return order

func draw(canvas: CanvasItem, at: Vector2, pose: int, facing: int, seconds: float) -> void:
	if bundle == null:
		return
	if pose == 4:
		# BaseEntity's original death policy always uses the first source view.
		bundle.draw_frame(canvas,at,"B-DEATH_ND1",seconds,8.0,ACTION.DEATH_SECONDS)
		return
	var action: String = ["NS","NR","AA","AH","NS"][clampi(pose,0,4)]
	var requested := "%s%d" % [action,facing]
	var body: Dictionary = bundle.sample("B-BODY_"+requested,seconds)
	if body.is_empty():
		return
	var order := _draw_order(body)
	canvas.draw_set_transform(at,0,Vector2(1,0.3))
	canvas.draw_circle(Vector2.ZERO,16,Color(0,0,0,0.3))
	canvas.draw_set_transform(Vector2.ZERO)
	for part in order:
		var prefix: String = ("B-"+str(PARTS[part])).to_upper()
		bundle.draw_frame(canvas,at,prefix+"_"+requested,seconds,8.0,
			ACTION.COMBAT_SECONDS if pose >= 2 else 0.0)
