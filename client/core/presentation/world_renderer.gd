extends Node2D

# One canvas for placeholder players; no entity-per-node/Timer/Texture cost.
var state: RefCounted
var render_tick: float = 0.0
var connection_status: String = "Starting"

func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, Vector2(1400, 900)), Color(0.07, 0.11, 0.15), true)
	for i in range(0, 1400, 80):
		draw_line(Vector2(i, 0), Vector2(i, 900), Color(0.13, 0.17, 0.21), 1.0)
	for j in range(0, 900, 80):
		draw_line(Vector2(0, j), Vector2(1400, j), Color(0.13, 0.17, 0.21), 1.0)
	if state == null:
		return
	var font: Font = ThemeDB.fallback_font
	for entity_id in state.entities.keys():
		var rec: Dictionary = state.entities[entity_id]
		var pos: Vector2 = state.visual_position(entity_id, render_tick)
		var is_local: bool = int(entity_id) == state.local_entity_id
		var color: Color = Color(0.38, 0.84, 0.98) if is_local else Color(0.99, 0.74, 0.33)
		draw_circle(pos, 13.0, color)
		var angle: float = float(int(rec["facing"])) * TAU / 8.0
		draw_line(pos, pos + Vector2.RIGHT.rotated(angle) * 23.0, Color.WHITE, 3.0)
		if font != null:
			draw_string(font, pos + Vector2(-22, -25), str(rec["name"]), HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Color.WHITE)
			if not str(rec["action"]).is_empty():
				draw_string(font, pos + Vector2(-22, 36), str(rec["action"]), HORIZONTAL_ALIGNMENT_LEFT, -1, 12, color)
	if font != null:
		draw_string(font, Vector2(15, 26),
			"SGTest • %s • Click: Move • Right click: Stop • Players: %s" %
			[connection_status, str(state.entities.size())],
			HORIZONTAL_ALIGNMENT_LEFT, -1, 16, Color.WHITE)
