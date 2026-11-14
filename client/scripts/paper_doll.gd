extends RefCounted

# Bare Boy composition. All player instances share these six immutable atlases.
# Layer order is read from the original client's CharDrawOrder.dat.
const ATLAS = preload("res://scripts/entity_atlas.gd")
const ACTION = preload("res://scripts/action_policy.gd")
const PARTS := {"body":"body", "leg":"leg", "head":"head", "hair":"hair00", "l-arm":"l-arm", "r-arm":"r-arm"}
var parts: Dictionary = {}
var orders: Dictionary = {}
var death: RefCounted

func open(root: String) -> Error:
	var loaded: Dictionary = {}
	for key in PARTS:
		var atlas := ATLAS.new()
		var error: Error = atlas.open(root.path_join("player/b/standard/b-%s.json" % PARTS[key]))
		if error != OK:
			return error
		loaded[key] = atlas
	var death_atlas := ATLAS.new()
	var death_error: Error = death_atlas.open(root.path_join("player/b/standard/b-death.json"))
	if death_error != OK:
		return death_error
	for line in FileAccess.get_file_as_string(root.path_join("data/player/chardraworder.dat")).split("\n"):
		var separator := line.find(":")
		if separator < 0:
			continue
		var order: Array[String] = []
		for token in line.substr(separator+1).strip_edges().split(">"):
			var part: String = token.split("+")[0].strip_edges().to_lower()
			if loaded.has(part):
				order.append(part)
		orders[line.substr(0,separator).strip_edges()] = order
	parts = loaded
	death = death_atlas
	return OK if not orders.is_empty() else ERR_FILE_CORRUPT

func draw(canvas: CanvasItem, at: Vector2, pose: int, facing: int, seconds: float) -> void:
	if parts.is_empty():
		return
	if pose == 4:
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
