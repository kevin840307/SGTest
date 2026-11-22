extends SceneTree
const Pets = preload("res://pet/pet_presentation.gd")

func _initialize() -> void:
	var pets := Pets.new()
	var first := _snapshot(7,{
		1001:_entity(1001,4,102,5002,42,24.0,36.0)
	})
	if pets.apply_snapshot(first,0) != OK:
		_fail("initial authoritative pet snapshot rejected")
		return
	if pets.count()!=1 or pets.position_at(1001,0)!=Vector2(24.0,36.0):
		_fail("initial pet position missing")
		return
	if pets.unresolved_definition_ids()!=[5002]:
		_fail("unresolved visual definition was not exposed")
		return

	var moved := _snapshot(7,{
		1001:_entity(1001,4,102,5002,45,48.0,36.0)
	})
	if pets.apply_snapshot(moved,100000) != OK:
		_fail("movement snapshot rejected")
		return
	var midway := pets.position_at(1001,100000)
	if not is_equal_approx(midway.x,28.0) or not is_equal_approx(midway.y,36.0):
		_fail("pet interpolation did not use shared stable timeline")
		return
	if pets.position_at(1001,200000)!=Vector2(48.0,36.0):
		_fail("pet interpolation did not converge to authority")
		return

	var stale := _snapshot(7,{
		1001:_entity(1001,4,102,5002,44,12.0,36.0)
	})
	if pets.apply_snapshot(stale,210000)!=OK or pets.position_at(1001,220000).x<28.0:
		_fail("stale pet presentation regressed position")
		return

	var bad_same_tick := _snapshot(7,{
		1001:_entity(1001,4,102,5002,45,99.0,36.0)
	})
	if pets.apply_snapshot(bad_same_tick,220000)!=ERR_INVALID_DATA:
		_fail("same authoritative tick accepted two positions")
		return

	var fake_assets := {
		"pets/source-backed":{
			"manifest":{"clips":{"idle":[0]}}
		}
	}
	if pets.configure_visuals({
		5002:{"asset":"pets/source-backed","clip":"idle","frame":0}
	},fake_assets)!=OK:
		_fail("valid visual binding rejected")
		return
	var actors := pets.actors_at(220000)
	if actors.size()!=1 or str(actors[0].id)!="pet:102" or int(actors[0].owner_actor_id)!=1001:
		_fail("world draw actor bridge missing")
		return

	if pets.apply_snapshot(_snapshot(7,{}),230000)!=OK or pets.count()!=0:
		_fail("despawn snapshot retained pet presentation")
		return

	var replacement := _snapshot(8,{
		1001:_entity(1001,5,202,7001,3,300.0,400.0)
	})
	if pets.apply_snapshot(replacement,300000)!=OK or pets.position_at(1001,300000)!=Vector2(300.0,400.0):
		_fail("map generation did not reset pet history")
		return
	if pets.unresolved_definition_ids()!=[7001]:
		_fail("replacement generation retained old visual identity")
		return

	print("PET_PRESENTATION_PASS interpolation=true no_extrapolation=true generation=true visual_bridge=true")
	quit(0)

func _snapshot(map_epoch:int,entities:Dictionary)->Dictionary:
	return {
		"world_ready":true,
		"map_epoch":map_epoch,
		"map_id":13,
		"pet_entities":entities
	}

func _entity(owner:int,connection:int,uid:int,definition:int,tick:int,x:float,y:float)->Dictionary:
	return {
		"receiver_map_epoch":7,
		"server_tick":tick,
		"owner_actor_id":owner,
		"owner_connection_epoch":connection,
		"owner_map_epoch":1,
		"pet_uid":uid,
		"definition_id":definition,
		"level":7,
		"x":x,
		"y":y
	}

func _fail(message:String)->void:
	push_error("PET_PRESENTATION_FAIL "+message)
	quit(1)
