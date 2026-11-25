extends SceneTree
const Pets = preload("res://pet/pet_presentation.gd")

func _initialize() -> void:
	var pets := Pets.new()
	var first := _snapshot(7,{
		1001:_entity(1001,4,102,5002,7,42,24.0,36.0)
	})
	if pets.apply_snapshot(first,0) != OK:
		_fail("initial authoritative pet snapshot rejected")
		return
	if pets.count()!=1 or pets.position_at(1001,0)!=Vector2(24.0,36.0):
		_fail("initial pet position missing")
		return
	if pets.unresolved_definition_ids()!=[5002] or pets.unresolved_visual_keys()!=["5002:7"]:
		_fail("unresolved visual definition/evolution was not exposed")
		return

	var moved := _snapshot(7,{
		1001:_entity(1001,4,102,5002,7,45,48.0,36.0)
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
		1001:_entity(1001,4,102,5002,7,44,12.0,36.0)
	})
	if pets.apply_snapshot(stale,210000)!=OK or pets.position_at(1001,220000).x<28.0:
		_fail("stale pet presentation regressed position")
		return

	var bad_same_tick := _snapshot(7,{
		1001:_entity(1001,4,102,5002,7,45,99.0,36.0)
	})
	if pets.apply_snapshot(bad_same_tick,220000)!=ERR_INVALID_DATA:
		_fail("same authoritative tick accepted two positions")
		return

	var fake_assets := {
		"pets/source-backed":{
			"manifest":{
				"clips":{"idle":[0],"attack":[0,1,2]},
				"logical_views":{
					"attack":{"clip":"attack","flip_h":true,"timeline_ticks":[[0,2],[1,2],[2,2]],"loop":false}
				}
			}
		}
	}
	if pets.configure_visuals({
		5002:{7:{"asset":"pets/source-backed","clip":"idle","frame":0,"attack_view":"attack"}}
	},fake_assets)!=OK:
		_fail("valid visual binding rejected")
		return
	var actors := pets.actors_at(220000)
	if actors.size()!=1 or str(actors[0].id)!="pet:102" or int(actors[0].owner_actor_id)!=1001:
		_fail("world draw actor bridge missing")
		return

	var combat := _snapshot(7,{
		1001:_entity(1001,4,102,5002,7,46,48.0,36.0)
	})
	combat["last_pet_attack_result_sequence"] = 3
	combat["last_pet_attack_result_status"] = 0
	combat["last_pet_attack_result"] = {
		"map_epoch":7,"attack_sequence":3,"status":0,"pet_uid":102,
		"target_entity_id":1001300001,"damage":25,"current_hp":75,
		"max_hp":100,"target_revision":4,"server_tick":55
	}
	if pets.apply_snapshot(combat,230000)!=OK:
		_fail("authoritative pet attack result rejected by presentation")
		return
	var attack0 := pets.actors_at(230000)
	if attack0.size()!=1 or str(attack0[0].clip)!="attack" or int(attack0[0].frame)!=0 or not bool(attack0[0].flip_h):
		_fail("authoritative pet attack did not start source-backed attack view")
		return
	var attack1 := pets.actors_at(270000)
	if attack1.size()!=1 or int(attack1[0].frame)!=1:
		_fail("pet attack did not follow source timeline ticks")
		return
	var settled := pets.actors_at(360000)
	if settled.size()!=1 or str(settled[0].clip)!="idle" or int(settled[0].frame)!=0 or bool(settled[0].flip_h):
		_fail("pet attack did not return to configured fallback visual")
		return
	if pets.apply_snapshot(combat,370000)!=OK:
		_fail("duplicate authoritative pet snapshot rejected")
		return
	var replayed := pets.actors_at(370000)
	if replayed.size()!=1 or str(replayed[0].clip)!="idle":
		_fail("duplicate pet attack result restarted one-shot animation")
		return
	var rejected := _snapshot(7,{
		1001:_entity(1001,4,102,5002,7,47,48.0,36.0)
	})
	rejected["last_pet_attack_result_sequence"] = 4
	rejected["last_pet_attack_result_status"] = 6
	rejected["last_pet_attack_result"] = {
		"map_epoch":7,"attack_sequence":4,"status":6,"pet_uid":102,
		"target_entity_id":1001300001,"damage":0,"current_hp":0,
		"max_hp":0,"target_revision":0,"server_tick":56
	}
	if pets.apply_snapshot(rejected,375000)!=OK:
		_fail("rejected pet attack result broke presentation")
		return
	var rejected_visual := pets.actors_at(375000)
	if rejected_visual.size()!=1 or str(rejected_visual[0].clip)!="idle":
		_fail("cooldown/rejected pet attack fabricated attack animation")
		return

	var remote_visual:=_snapshot(7,{
		1001:_entity(1001,4,102,5002,7,48,48.0,36.0)
	})
	remote_visual["pet_attack_visual_events"]=[{
		"receiver_map_epoch":7,"server_tick":57,"owner_actor_id":1001,
		"owner_connection_epoch":4,"owner_map_epoch":1,"attack_sequence":5,
		"pet_uid":102,"target_entity_id":1001300001,"status":0,
		"damage":25,"target_revision":5
	}]
	if pets.apply_snapshot(remote_visual,380000)!=OK:
		_fail("remote pet attack visual broke presentation")
		return
	var remote_attack:=pets.actors_at(380000)
	if remote_attack.size()!=1 or str(remote_attack[0].clip)!="attack" or int(remote_attack[0].frame)!=0:
		_fail("remote observer attack event did not start pet attack view")
		return
	if pets.actors_at(510000).size()!=1 or str(pets.actors_at(510000)[0].clip)!="idle":
		_fail("remote pet attack did not settle to fallback visual")
		return
	if pets.apply_snapshot(remote_visual,520000)!=OK or str(pets.actors_at(520000)[0].clip)!="idle":
		_fail("duplicate remote pet attack visual restarted animation")
		return

	# A committed attack result can arrive while AOI has not spawned the
	# pet. Reusing the reliable sequence after spawn must start the real
	# source-backed attack clip, not be consumed by the missing visual.
	var delayed := _snapshot(7,{})
	delayed["last_pet_attack_result_sequence"] = 6
	delayed["last_pet_attack_result_status"] = 0
	delayed["last_pet_attack_result"] = {
		"map_epoch":7,"attack_sequence":6,"status":0,"pet_uid":102,
		"target_entity_id":1001300001,"damage":19,"current_hp":56,
		"max_hp":100,"target_revision":6,"server_tick":58
	}
	if pets.apply_snapshot(delayed,530000)!=OK or pets.count()!=0:
		_fail("absent pet attack result must not create an AOI ghost")
		return
	var delayed_replay := delayed.duplicate(true)
	delayed_replay.pet_entities={1001:_entity(1001,4,102,5002,7,49,48.0,36.0)}
	if pets.apply_snapshot(delayed_replay,540000)!=OK:
		_fail("delayed pet attack result blocked the later real AOI spawn")
		return
	var recovered_attack := pets.actors_at(540000)
	if recovered_attack.size()!=1 or str(recovered_attack[0].clip)!="attack" or int(recovered_attack[0].frame)!=0:
		_fail("pet result sequence consumed before source-backed actor was visible")
		return
	if pets.apply_snapshot(_snapshot(7,{}),545000)!=OK or pets.count()!=0:
		_fail("despawn snapshot retained pet presentation")
		return

	var replacement := _snapshot(8,{
		1001:_entity(1001,5,202,7001,3,3,300.0,400.0)
	})
	if pets.apply_snapshot(replacement,550000)!=OK or pets.position_at(1001,550000)!=Vector2(300.0,400.0):
		_fail("map generation did not reset pet history")
		return
	if pets.unresolved_definition_ids()!=[7001] or pets.unresolved_visual_keys()!=["7001:3"]:
		_fail("replacement generation retained old visual identity")
		return

	# A valid delayed snapshot from an older map generation must not replace
	# the current summoned pet or rewind its visual identity.
	if (pets.apply_snapshot(first,560000)!=ERR_INVALID_DATA or
		pets.count()!=1 or pets.position_at(1001,560000)!=Vector2(300.0,400.0) or
		pets.unresolved_definition_ids()!=[7001]):
		_fail("stale pet map generation replaced current summoned pet")
		return

	# A malformed second entry must not partially despawn the first pet,
	# including when a newer map epoch is being admitted.
	var invalid_pet := _entity(1002,5,303,7001,3,4,100.0,110.0)
	invalid_pet.pet_uid = 0
	var malformed := _snapshot(9,{1002:invalid_pet})
	if (pets.apply_snapshot(malformed,570000)!=ERR_INVALID_DATA or
		pets.count()!=1 or pets.position_at(1001,570000)!=Vector2(300.0,400.0)):
		_fail("invalid new-epoch pet batch cleared the current AOI")
		return
	malformed = _snapshot(8,{1002:invalid_pet})
	if (pets.apply_snapshot(malformed,580000)!=ERR_INVALID_DATA or
		pets.count()!=1 or pets.position_at(1001,580000)!=Vector2(300.0,400.0)):
		_fail("invalid same-epoch pet batch partially despawned current pet")
		return
	var duplicate_owner := _snapshot(8,{
		1001:_entity(1001,5,202,7001,3,3,300.0,400.0),
		"1001":_entity(1001,5,202,7001,3,3,20.0,40.0)
	})
	if (pets.apply_snapshot(duplicate_owner,590000)!=ERR_INVALID_DATA or
		pets.count()!=1 or pets.position_at(1001,590000)!=Vector2(300.0,400.0)):
		_fail("duplicate numeric/string owner aliases replaced authoritative pet")
		return

	print("PET_PRESENTATION_PASS interpolation=true no_extrapolation=true generation=true visual_bridge=true attack_event=true remote_attack_visual=true source_timing=true delayed_result_after_spawn=true stale_epoch_rejected=true atomic_aoi=true")
	quit(0)

func _snapshot(map_epoch:int,entities:Dictionary)->Dictionary:
	return {
		"world_ready":true,
		"map_epoch":map_epoch,
		"map_id":13,
		"pet_entities":entities
	}

func _entity(owner:int,connection:int,uid:int,definition:int,evolution:int,tick:int,x:float,y:float)->Dictionary:
	return {
		"receiver_map_epoch":7,
		"server_tick":tick,
		"owner_actor_id":owner,
		"owner_connection_epoch":connection,
		"owner_map_epoch":1,
		"pet_uid":uid,
		"definition_id":definition,
		"evolution_id":evolution,
		"level":7,
		"x":x,
		"y":y
	}

func _fail(message:String)->void:
	push_error("PET_PRESENTATION_FAIL "+message)
	quit(1)
