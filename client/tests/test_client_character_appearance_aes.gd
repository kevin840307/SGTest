extends SceneTree
const Session = preload("res://session/client_session.gd")
const State = preload("res://world/world_state.gd")
const SessionPanel = preload("res://session/session_panel.gd")
const LOOK := {"race":1,"hair_index":3,"hair_color":7,"body_index":2,"leg_index":1,"sleeve_color":2}

func _initialize() -> void:
	_run.call_deferred()

func _fail(reason: String, session: RefCounted) -> void:
	session.stop()
	push_error("CLIENT_CHARACTER_APPEARANCE_AES_FAIL "+reason)
	quit(1)

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size()!=1:
		quit(2)
		return
	for pass_index in 2:
		var panel: PanelContainer
		var session = Session.new()
		if session.connect_secure("127.0.0.1",int(args[0]),"fixture_user","Fixture-Password-123!")!=OK:
			_fail("AES connect",session)
			return
		var deadline := Time.get_ticks_msec()+10000
		var snapshot: Dictionary = session.snapshot()
		while int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER and Time.get_ticks_msec()<deadline:
			await process_frame
			snapshot = session.snapshot()
		if int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER or snapshot.character_creation_appearances.size()!=2:
			_fail("server choices missing "+str(snapshot),session)
			return
		if pass_index==0:
			if session.create_character(0,"Chosen") or session.create_character(0,"Chosen",{"race":2,"hair_index":3,"hair_color":7}):
				_fail("client admitted unlisted/missing choice",session)
				return
			panel = SessionPanel.new()
			root.add_child(panel)
			panel.set_process(false)
			panel.session = session
			panel.active = true
			panel._latest = snapshot
			panel._set_controls()
			panel.appearance_picker._pickers.race.item_selected.emit(1)
			if panel.creation_options.selection()!=LOOK:
				_fail("real UI picker did not select allowed appearance",session)
				return
			panel.character_name_input.text = "Chosen"
			panel.character_slot_input.value = 0
			panel.character_strength_input.value = 3
			panel.character_dexterity_input.value = 2
			panel.character_constitution_input.value = 2
			panel.character_wisdom_input.value = 1
			panel.character_password_input.text = "2468"
			panel.character_password_confirm_input.text = "2468"
			panel.create_character_button.pressed.emit()
			while int(snapshot.get("last_created_character_id",0))==0 and Time.get_ticks_msec()<deadline:
				await process_frame
				snapshot = session.snapshot()
			while int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER and Time.get_ticks_msec()<deadline:
				await process_frame
				snapshot = session.snapshot()
		if snapshot.characters.size()!=1 or snapshot.characters[0].get("appearance",{})!=LOOK:
			_fail("create/relogin appearance changed "+str(snapshot),session)
			return
		if pass_index==1:
			var character_id := int(snapshot.characters[0].character_id)
			if not session.choose_character(character_id,"0000"):
				_fail("queue wrong secondary password",session)
				return
			var wrong_deadline := Time.get_ticks_msec()+5000
			while (int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER or
					int(snapshot.get("choose_status",-1))!=4) and Time.get_ticks_msec()<wrong_deadline:
				await process_frame
				snapshot = session.snapshot()
			if int(snapshot.get("state",-1))!=State.STATE_SELECTING_CHARACTER or int(snapshot.get("choose_status",-1))!=4 or snapshot.get("world_ready",false):
				_fail("wrong secondary password did not return retryable selection "+str(snapshot),session)
				return
			if not session.choose_character(character_id,"2468"):
				_fail("retry persisted character with correct secondary password",session)
				return
			while not snapshot.get("world_ready",false) and Time.get_ticks_msec()<deadline:
				await process_frame
				snapshot = session.snapshot()
			if not snapshot.get("world_ready",false) or not snapshot.character_creation_appearances.is_empty():
				_fail("world transition retained creation menu or failed ready",session)
				return
			if not bool(snapshot.get("player_profile_ready",false)) or \
					int(snapshot.get("player_verified_stats_mask",-1))!=13 or \
					not bool(snapshot.get("equipment_ready",false)) or \
					not (snapshot.get("equipment_items",{}) as Dictionary).is_empty() or \
					bool(snapshot.get("combat_available",true)) or \
					int(snapshot.get("player_level",0))!=1 or \
					int(snapshot.get("player_strength",0))!=3 or \
					int(snapshot.get("player_dexterity",0))!=2 or \
					int(snapshot.get("player_constitution",0))!=2 or \
					int(snapshot.get("player_wisdom",0))!=1 or \
					int(snapshot.get("player_current_hp",0))!=149 or \
					int(snapshot.get("player_max_hp",0))!=149 or \
					int(snapshot.get("player_current_mp",0))!=122 or \
					int(snapshot.get("player_max_mp",0))!=122 or \
					int(snapshot.get("player_current_wp",0))!=101 or \
					int(snapshot.get("player_max_wp",0))!=101 or \
					int(snapshot.get("player_attack",0))!=10 or \
					int(snapshot.get("player_defense",0))!=3 or \
					int(snapshot.get("player_hit",0))!=8 or \
					int(snapshot.get("player_dodge",0))!=9:
				_fail("source-backed profile/vitals missing or changed "+str(snapshot),session)
				return
		session.stop()
		if panel != null:
			panel.queue_free()
		await process_frame
	print("CLIENT_CHARACTER_APPEARANCE_AES_PASS server_choices=true UI_picker_and_button=true exact_choice=true persisted_relogin=true wrong_password_retry=true choose_world=true source_profile=true hp_mp_wp=true wp=101/101 combat_locked=true menu_released=true")
	quit(0)
