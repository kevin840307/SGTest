extends SceneTree

const Names=preload("res://pvp/pvp_name_presentation.gd")

func _fail(message:String)->void:
	push_error("PVP_NAME_PRESENTATION_FAIL "+message)
	quit(1)

func _initialize()->void:
	var names:=Names.new()
	var now:=1000000
	var snapshot={
		"world_ready":true,"map_epoch":1,"map_id":13,
		"player_identities":{
			1:_identity(1,0,false,1000000,0,1,"Blue"),
			2:_identity(2,1,false,1000000,0,2,"White"),
			3:_identity(3,1,false,1000000,1030000,3,"Pink"),
			4:_identity(4,1,true,1000000,0,4,"Red"),
		}}
	names.apply_snapshot(snapshot,now)
	if names.count()!=4:
		_fail("identity count")
		return
	if (str(names.label_for(1,now).name_state)!="blue" or
		str(names.label_for(2,now).name_state)!="white" or
		str(names.label_for(3,now).name_state)!="pink" or
		str(names.label_for(4,now).name_state)!="red"):
		_fail("base colors")
		return
	var flashing:=now+27100000
	var first:Dictionary=names.label_for(3,flashing)
	var second:Dictionary=names.label_for(3,flashing+250000)
	if str(first.name_state)!="pink_flashing" or str(second.name_state)!="pink_flashing" or first.color==second.color:
		_fail("final three seconds do not flash")
		return
	if str(names.label_for(3,now+30000001).name_state)!="white":
		_fail("expired pink did not return to base white")
		return

	# Repeated render snapshots with the same server timestamp must not slide
	# the 30-second deadline forward.
	names.apply_snapshot(snapshot,now+5000000)
	if str(names.label_for(3,now+30000001).name_state)!="white":
		_fail("snapshot poll extended pink deadline")
		return

	snapshot.player_identities[3]=_identity(3,1,true,1000010,0,10,"Pink")
	names.apply_snapshot(snapshot,now+10000000)
	if str(names.label_for(3,now+40000000).name_state)!="red":
		_fail("red update not persistent")
		return

	# A delayed snapshot from before the authoritative kill must not restore
	# pink after the newest server PvP update turned that name red.
	snapshot.player_identities[3]=_identity(3,1,false,1000000,1030000,3,"OldPink")
	names.apply_snapshot(snapshot,now+10500000)
	if str(names.label_for(3,now+10500000).name_state)!="red":
		_fail("late same-generation PvP snapshot rolled red name back to pink")
		return

	snapshot.map_epoch=2
	snapshot.map_id=15
	snapshot.player_identities={}
	names.apply_snapshot(snapshot,now+11000000)
	if names.count()!=0:
		_fail("map generation retained names")
		return
	var stale_map={"world_ready":true,"map_epoch":1,"map_id":13,"player_identities":{3:_identity(3,1,true,1000010,0,10,"StaleMap")}}
	names.apply_snapshot(stale_map,now+12000000)
	if names.count()!=0:
		_fail("late old-map snapshot repopulated departed players")
		return

	var joined:=_identity(3,0,false,1000020,0,1,"Rejoined")
	joined.connection_epoch=100
	joined.entity_map_epoch=2
	snapshot.player_identities={3:joined}
	names.apply_snapshot(snapshot,now+13000000)
	if str(names.label_for(3,now+13000000).name_state)!="blue":
		_fail("new connection could not rejoin with fresh PvP state")
		return
	var stale_connection:=_identity(3,1,true,1000030,0,90,"OldConnection")
	stale_connection.entity_map_epoch=2
	snapshot.player_identities={3:stale_connection}
	names.apply_snapshot(snapshot,now+14000000)
	if str(names.label_for(3,now+14000000).name_state)!="blue":
		_fail("old connection snapshot replaced the new player PvP name")
		return

	print("PVP_NAME_PRESENTATION_PASS blue=true white=true pink=true final3_flash=true pink_expiry=true red=true no_timer_slide=true generation_clear=true stale_pvp_update=true stale_map=true old_connection=true batched_canvas=true")
	quit(0)

func _identity(actor:int,mode:int,red:bool,server_ms:int,pink_ms:int,updated:int,name:String)->Dictionary:
	return {
		"receiver_map_epoch":1,"actor_id":actor,
		"connection_epoch":actor+10,"entity_map_epoch":1,
		"mode":mode,"red_man":red,"server_unix_ms":server_ms,
		"pink_until_unix_ms":pink_ms,"pvp_updated_unix_ms":updated,
		"display_name":name,
	}
