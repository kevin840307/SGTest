extends RefCounted
## Presentation-only player-name state. Combat legality remains Server-owned.
const BLUE:=Color(0.35,0.65,1.0,1.0)
const WHITE:=Color(1.0,1.0,1.0,1.0)
const PINK:=Color(1.0,0.45,0.68,1.0)
const RED:=Color(1.0,0.20,0.20,1.0)
const PINK_FLASH_USEC:=3000000
const FLASH_HALF_USEC:=250000

var _epoch:=0
var _map:=0
var _states:={}

func clear()->void:
	_epoch=0
	_map=0
	_states.clear()

func apply_snapshot(snapshot:Dictionary,now_usec:int)->void:
	if not bool(snapshot.get("world_ready",false)) or int(snapshot.get("map_epoch",0))<=0 or now_usec<0:
		return
	var epoch:=int(snapshot.map_epoch)
	var map:=int(snapshot.map_id)
	if epoch!=_epoch or map!=_map:
		_states.clear()
		_epoch=epoch
		_map=map
	var identities:Dictionary=snapshot.get("player_identities",{})
	for raw_id in _states.keys():
		if not identities.has(raw_id):
			_states.erase(raw_id)
	var ids:Array=identities.keys()
	ids.sort()
	for raw_id in ids:
		var id:=int(raw_id)
		var identity:Dictionary=identities[raw_id]
		var connection:=int(identity.get("connection_epoch",0))
		var entity_epoch:=int(identity.get("entity_map_epoch",0))
		var server_ms:=int(identity.get("server_unix_ms",0))
		var pink_ms:=int(identity.get("pink_until_unix_ms",0))
		var updated:=int(identity.get("pvp_updated_unix_ms",0))
		var mode:=int(identity.get("mode",-1))
		var red:=bool(identity.get("red_man",false))
		var name:=str(identity.get("display_name",""))
		if id<=0 or connection<=0 or entity_epoch<=0 or server_ms<=0 or pink_ms<0 or updated<0 or mode not in [0,1] or name.is_empty():
			continue
		var prior:Dictionary=_states.get(id,{})
		var changed:=prior.is_empty() or int(prior.get("connection_epoch",0))!=connection or int(prior.get("entity_map_epoch",0))!=entity_epoch or int(prior.get("source_server_unix_ms",0))!=server_ms or int(prior.get("pink_until_unix_ms",0))!=pink_ms or int(prior.get("pvp_updated_unix_ms",0))!=updated or int(prior.get("mode",-1))!=mode or bool(prior.get("red_man",false))!=red or str(prior.get("display_name",""))!=name
		if not changed:
			continue
		var remaining_ms:=maxi(0,pink_ms-server_ms)
		_states[id]={
			"actor_id":id,
			"connection_epoch":connection,
			"entity_map_epoch":entity_epoch,
			"display_name":name,
			"mode":mode,
			"red_man":red,
			"source_server_unix_ms":server_ms,
			"pink_until_unix_ms":pink_ms,
			"pvp_updated_unix_ms":updated,
			"pink_deadline_usec":0 if red or remaining_ms<=0 else now_usec+remaining_ms*1000,
		}

func label_for(actor_id:int,now_usec:int)->Dictionary:
	if actor_id<=0 or now_usec<0 or not _states.has(actor_id):
		return {}
	var state:Dictionary=_states[actor_id]
	var kind:="white"
	var color:=WHITE
	if bool(state.red_man):
		kind="red"
		color=RED
	else:
		var deadline:=int(state.pink_deadline_usec)
		var remaining:=deadline-now_usec
		if deadline>0 and remaining>0:
			if remaining<=PINK_FLASH_USEC:
				kind="pink_flashing"
				color=PINK if int(remaining/FLASH_HALF_USEC)%2==0 else WHITE
			else:
				kind="pink"
				color=PINK
		elif int(state.mode)==0:
			kind="blue"
			color=BLUE
	return {
		"actor_id":actor_id,
		"text":str(state.display_name),
		"color":color,
		"name_state":kind,
		"connection_epoch":int(state.connection_epoch),
		"entity_map_epoch":int(state.entity_map_epoch),
	}

func labels_at(now_usec:int)->Array:
	var result:Array=[]
	var ids:Array=_states.keys()
	ids.sort()
	for raw_id in ids:
		var label:=label_for(int(raw_id),now_usec)
		if not label.is_empty():
			result.append(label)
	return result

func count()->int:
	return _states.size()
