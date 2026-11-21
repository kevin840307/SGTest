extends SceneTree
const Npcs = preload("res://npc/npc_presentation.gd")

func _fail(reason: String) -> void:
	push_error("NPC_PRESENTATION_FAIL " + reason)
	quit(1)

func _initialize() -> void:
	var presenter = Npcs.new()
	var frame := {"page":0,"rect":[0,0,52,80],"trim":[0,0],"origin":[26,70]}
	var asset := {"manifest":{"clips":{"NPC003":[0]},"frames":[frame]},"pages":[]}
	var population := {"schema":"sgtest.npcs/v1","npcs":[
		{"spawn_id":46,"p":[7906,11358],"visual":{"resolved":true,"atlas":"npcs/npc003/atlas.json"},
			"interaction":{"mode":"confirm_transfer","destination":{"map_id":13}}},
		{"spawn_id":2,"p":[200,200],"visual":{"resolved":true,"atlas":"npcs/npc003/atlas.json"},
			"interaction":{"mode":"script_required","dependencies_complete":false}}
	]}
	if presenter.configure(population,{"npcs/npc003":asset}) != OK or presenter.count() != 2:
		_fail("source NPC model not configured")
		return
	var view := Rect2(7860,11250,140,180)
	var actors: Array = presenter.actors_at(view)
	if actors.size() != 1 or actors[0].id != "npc:46" or actors[0].parts[0].clip != "NPC003":
		_fail("scenery actor pipeline dropped visible NPC or used wrong clip")
		return
	if presenter.pick_at(Vector2(7906,11295)) != 46 or presenter.pick_at(Vector2(7906,11358)) != 46:
		_fail("visible original SPR body cannot be clicked")
		return
	if presenter.pick_at(Vector2(12000,12000)) != 0:
		_fail("unrelated terrain hit an NPC")
		return
	if int(presenter.interaction_for(46).get("destination",{}).get("map_id",0)) != 13:
		_fail("portal metadata missing")
		return
	var mutated := presenter.interaction_for(46)
	mutated.destination.map_id = 99
	if presenter.interaction_for(46).destination.map_id != 13:
		_fail("presenter leaked authoritative metadata")
		return
	# In formal-Host fixture maps, trusted NPCs may exist without any
	# visual sprite. Their metadata must not crash or block world loading.
	var partial = population.duplicate(true)
	partial.npcs[1].erase("visual")
	if presenter.configure(partial,{"npcs/npc003":asset}) != OK or presenter.count() != 1:
		_fail("NPC without visual should remain gameplay-only, not break map")
		return
	if presenter.configure({}, {}) != OK or presenter.count() != 0:
		_fail("absent NPC catalog should be a valid empty visual layer")
		return
	var invalid = population.duplicate(true)
	invalid.npcs[1].spawn_id = 46
	if presenter.configure(invalid,{"npcs/npc003":asset}) != ERR_INVALID_DATA or presenter.count() != 0:
		_fail("duplicate spawn ID accepted or leaked old visuals")
		return
	print("NPC_PRESENTATION_PASS original_sprite=true world_order_record=true hitbox=true portal_metadata=true fail_closed=true")
	quit(0)
