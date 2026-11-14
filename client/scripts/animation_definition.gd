extends RefCounted

# Atlas-independent, immutable playback metadata. One instance per definition;
# actors own elapsed/server action time, never copies of these frame tables.
var plans: Dictionary = {}

func configure(source: Dictionary) -> Error:
	plans.clear()
	if source.get("schema") != "sgtest.animation" or int(source.get("version",0)) != 2:
		return ERR_FILE_CORRUPT
	var data: Dictionary = source.duplicate(true)
	var shared_views: Dictionary = {}
	for code in data.get("behaviors",{}):
		var behavior: Dictionary = data.behaviors[code]
		var ends := PackedInt32Array()
		var indices := PackedInt32Array()
		var total := 0
		for step in behavior.get("timeline",[]):
			var ticks := int(step.get("default_ticks",0))
			var index := int(step.get("frame_index",-1))
			if ticks < 1 or index < 0 or total+ticks > 2147483647:
				plans.clear()
				return ERR_FILE_CORRUPT
			total += ticks
			ends.append(total)
			indices.append(index)
		if total == 0 or behavior.get("facings",{}).size() != 8:
			plans.clear()
			return ERR_FILE_CORRUPT
		var views: Dictionary = {}
		for facing in range(1,9):
			var view: Dictionary = behavior.facings.get(str(facing),{})
			var clip: Dictionary = data.get("clips",{}).get(view.get("clip",""),{})
			var frame_ids: Array = clip.get("frames",[])
			if frame_ids.is_empty():
				plans.clear()
				return ERR_FILE_CORRUPT
			for index in indices:
				if index >= frame_ids.size():
					plans.clear()
					return ERR_FILE_CORRUPT
			var draw_key: Variant = view.get("draw_order_key")
			var draw_layers: Variant = view.get("draw_layers")
			var draw_slots: Variant = view.get("draw_slots")
			if draw_key != null:
				if not draw_layers is Array or draw_layers.is_empty() or not draw_slots is Array or draw_slots.is_empty():
					plans.clear()
					return ERR_FILE_CORRUPT
				for slot in draw_slots:
					if not slot is int or int(slot) < 0 or int(slot) > 15:
						plans.clear()
						return ERR_FILE_CORRUPT
			elif draw_layers != null or draw_slots != null:
				plans.clear()
				return ERR_FILE_CORRUPT
			var key := "%s:%s:%s" % [view.clip,view.get("flip_h",false),str(draw_key)]
			if not shared_views.has(key):
				var descriptors: Array = []
				for frame_id in frame_ids:
					if int(frame_id) < 0 or int(frame_id) >= data.get("frames",[]).size():
						plans.clear()
						return ERR_FILE_CORRUPT
					descriptors.append({"frame":data.frames[int(frame_id)],"flip_h":bool(view.get("flip_h",false)),
						"clip":view.clip,"draw_order_key":draw_key,
						"draw_layers":draw_layers,"draw_slots":draw_slots})
				shared_views[key] = descriptors
			views[facing] = shared_views[key]
		plans[code] = {"ends":ends,"indices":indices,"total":total,"views":views,
			"loop":bool(behavior.get("loop",false)),"events":behavior.get("events",[])}
	return OK if not plans.is_empty() else ERR_FILE_CORRUPT

func sample(code: String, facing: int, seconds: float, duration: float = 0.0, loop_override: int = -1) -> Dictionary:
	if not plans.has(code) or facing < 1 or facing > 8 or not is_finite(seconds):
		return {}
	var plan: Dictionary = plans[code]
	var looping: bool = plan.loop if loop_override < 0 else loop_override != 0
	var tick := int(maxf(seconds,0.0)*(float(plan.total)/duration if duration > 0.0 else 50.0))
	tick = tick%int(plan.total) if looping else mini(tick,int(plan.total)-1)
	var step: int = plan.ends.bsearch(tick,false)
	return plan.views[facing][plan.indices[step]]
