extends SceneTree

var network := preload("res://scripts/network_client.gd").new()
var wire := preload("res://scripts/message_pack.gd").new()
var port := 27931
var result_path := ""
var mode := "hold"
var client_index := 77
var timeout_seconds := 30.0
var login_sent := false
var register_sent := false
var create_sent := false
var select_sent := false
var logout_sent := false
var authenticated := false
var entered := false
var replaced := false
var logged_out := false
var account_id := 0
var session_epoch := 0
var entity_id := 0
var character_id := 0
var entered_at := 0.0
var started_at := 0.0
var events: Array = []

func _initialize() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--port="):
			port = int(argument.trim_prefix("--port="))
		elif argument.begins_with("--result="):
			result_path = argument.trim_prefix("--result=")
		elif argument.begins_with("--mode="):
			mode = argument.trim_prefix("--mode=")
		elif argument.begins_with("--index="):
			client_index = maxi(0,int(argument.trim_prefix("--index=")))
		elif argument.begins_with("--timeout="):
			timeout_seconds = maxf(5.0,float(argument.trim_prefix("--timeout=")))
	started_at = Time.get_ticks_usec()/1000000.0
	network.start("ws://127.0.0.1:%d/game" % port)

func _process(_delta: float) -> bool:
	for bytes in network.receive():
		var packet: Array = wire.decode(bytes)
		if packet.is_empty():
			_finish(false,"invalid_packet")
			return false
		_dispatch(int(packet[1]),packet[2])
	if not login_sent and network.is_open():
		login_sent = true
		network.send(11,["bot_%04d" % client_index,"sgtest-bot-password"])
	if mode == "logout" and entered and not logout_sent:
		if Time.get_ticks_usec()/1000000.0-entered_at >= 1.0:
			logout_sent = true
			network.send(14,[])
	var now := Time.get_ticks_usec()/1000000.0
	if now-started_at > timeout_seconds:
		_finish(false,"timeout")
		return false
	var stats := network.statistics()
	if bool(stats.failed) and not replaced and not logged_out:
		_finish(false,"network_failed")
	return false

func _dispatch(op: int, body: Array) -> void:
	match op:
		10:
			var success := bool(body[0])
			var code := str(body[1])
			events.append(["auth",success,code])
			if success:
				authenticated = true
				account_id = int(body[3])
			elif code == "account_not_found" and not register_sent:
				register_sent = true
				network.send(10,["bot_%04d" % client_index,"sgtest-bot-password"])
			else:
				_finish(false,"auth_"+code)
		11:
			if body.size() != 1 or typeof(body[0]) != TYPE_ARRAY:
				_finish(false,"bad_character_list")
				return
			var rows: Array = body[0]
			events.append(["characters",rows.size()])
			if rows.is_empty():
				if not create_sent:
					create_sent = true
					network.send(12,["Bot%04d" % client_index])
			elif not select_sent:
				select_sent = true
				character_id = int(rows[0][0])
				network.send(13,[character_id])
		12:
			events.append(["create",bool(body[0]),str(body[1])])
			if not bool(body[0]):
				_finish(false,"create_"+str(body[1]))
		13:
			events.append(["select",bool(body[0]),str(body[1])])
			if not bool(body[0]):
				_finish(false,"select_"+str(body[1]))
			else:
				character_id = int(body[3])
				if body.size() >= 6:
					session_epoch = int(body[5])
		0:
			entity_id = int(body[0])
			if body.size() >= 2:
				character_id = int(body[1])
			events.append(["world_entity",entity_id,character_id])
		1:
			network.send(24,[int(body[0]),str(body[1]),str(body[2])])
			entered = true
			entered_at = Time.get_ticks_usec()/1000000.0
			events.append(["world_ready",int(body[0])])
			if mode == "once":
				_finish(true,"entered")
		14:
			var success := bool(body[0])
			var code := str(body[1])
			events.append(["logout",success,code])
			if success:
				logged_out = true
				_finish(true,"logged_out")
			elif code == "session_replaced":
				replaced = true
				_finish(true,"session_replaced")
			else:
				_finish(false,"logout_"+code)

func _finish(success: bool,reason: String) -> void:
	if result_path.is_empty():
		result_path = "user://identity_lifecycle_result.json"
	var payload := {
		"success":success,
		"reason":reason,
		"mode":mode,
		"authenticated":authenticated,
		"account_id":account_id,
		"session_epoch":session_epoch,
		"entity_id":entity_id,
		"character_id":character_id,
		"entered":entered,
		"replaced":replaced,
		"logged_out":logged_out,
		"events":events,
	}
	var file := FileAccess.open(result_path,FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(payload))
		file.close()
	network.close()
	quit(0 if success else 1)
