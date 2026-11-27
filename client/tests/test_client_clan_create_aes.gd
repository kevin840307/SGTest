extends SceneTree

const SessionPanel = preload("res://session/session_panel.gd")
const ClassicWorldShell = preload("res://ui/classic_world_shell.gd")
const WorldState = preload("res://world/world_state.gd")
const FixtureLogin = preload("res://tests/aes_character_fixture.gd")

const USERNAME := "fixture_user"
const PASSWORD := "Fixture-Password-123!"
const CLAN_NAME := "RoyalGuild"

func _fail(message:String,panel=null)->void:
	if panel!=null:
		panel.disconnect_session()
	push_error("CLIENT_CLAN_CREATE_AES_FAIL "+message)
	quit(1)

func _initialize()->void:
	var args:=OS.get_cmdline_user_args()
	if args.size()!=1 or not str(args[0]).is_valid_int():
		_fail("expected port")
		return
	root.size=Vector2i(800,600)
	var panel=SessionPanel.new()
	var shell=ClassicWorldShell.new()
	root.add_child(panel)
	root.add_child(shell)
	shell.bind_session(panel)
	panel.world_snapshot.connect(shell.apply_snapshot)
	panel.server_host="127.0.0.1"
	panel.server_port=int(args[0])
	panel.username_input.text=USERNAME
	panel.password_input.text=PASSWORD
	panel.login()

	var selecting:Dictionary=await _wait_panel(panel,func(v):
		return int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting.is_empty() or selecting.characters.size()!=1:
		_fail("first character list "+str(panel._latest),panel)
		return
	var character_id:=int(selecting.characters[0].character_id)
	if not FixtureLogin.provision_and_choose(panel.session,character_id):
		_fail("first choose",panel)
		return
	var ready:Dictionary=await _wait_panel(panel,func(v):
		return _world_ready(v) and int(v.get("clan_id",0))==0,9000)
	if ready.is_empty():
		_fail("first world baseline "+str(panel._latest),panel)
		return

	shell.set_world_active(true)
	shell.apply_snapshot(ready)
	await process_frame
	var guild_command:=shell.secondary_menu.get_node_or_null("GuildCommand") as Control
	if guild_command==null:
		_fail("GuildCommand missing",panel)
		return
	# Headless cannot prove viewport mouse hit-testing (native visible UI remains
	# separately NOT VERIFIED), so drive the real TextureButton pressed path while
	# still sending actual key events through the focused 061 LineEdit.
	(guild_command as BaseButton).pressed.emit()
	if not shell._clan_name_dialog.visible or shell.clan_window.visible:
		_fail("GuildCommand did not open source 061_CLUB_Name for no-clan player",panel)
		return
	shell._clan_name_input.focus_editor()
	await process_frame
	if not shell._clan_name_input._editor.has_focus():
		_fail("061_CLUB_Name input did not receive focus",panel)
		return
	await _type_text(CLAN_NAME)
	if shell._clan_name_input.text!=CLAN_NAME or shell._clan_name_confirm.disabled:
		_fail("typed clan name was not accepted "+shell._clan_name_input.text,panel)
		return
	shell._clan_name_confirm.pressed.emit()

	var created:Dictionary=await _wait_panel(panel,func(v):
		var members:Dictionary=v.get("clan_members",{})
		return (
			int(v.get("pending_clan_sequence",0))==0 and
			int(v.get("last_clan_result_sequence",0))>0 and
			int(v.get("last_clan_result_status",-1))==0 and
			int(v.get("clan_id",0))>0 and
			str(v.get("clan_name",""))==CLAN_NAME and
			int(v.get("clan_leader_actor_id",0))==int(v.get("actor_id",0)) and
			members.has(int(v.get("actor_id",0)))
		),9000)
	if created.is_empty():
		_fail("authoritative create result "+str(panel._latest),panel)
		return
	await process_frame
	if shell._clan_name_dialog.visible or not shell.clan_window.visible:
		_fail("authoritative snapshot did not replace create UI with 057_CLUB_Menu",panel)
		return
	if shell._clan_name_value.text!=CLAN_NAME or shell._clan_member_count_value.text!="1":
		_fail("057_CLUB_Menu did not render authoritative clan name/member count",panel)
		return
	shell._open_clan_member_window()
	await process_frame
	if not shell._clan_member_window.visible or shell._clan_member_actor_ids[0]!=int(created.get("actor_id",0)):
		_fail("060_CLUB_MemList did not render authoritative leader",panel)
		return
	var clan_id:=int(created.get("clan_id",0))

	panel.disconnect_session()
	shell.set_world_active(false)
	await process_frame
	panel.username_input.text=USERNAME
	panel.password_input.text=PASSWORD
	panel.login()
	var selecting2:Dictionary=await _wait_panel(panel,func(v):
		return int(v.get("state",-1))==WorldState.STATE_SELECTING_CHARACTER,7000)
	if selecting2.is_empty() or selecting2.characters.size()!=1:
		_fail("relog character list "+str(panel._latest),panel)
		return
	if not FixtureLogin.provision_and_choose(panel.session,character_id):
		_fail("relog choose",panel)
		return
	var relog:Dictionary=await _wait_panel(panel,func(v):
		var members:Dictionary=v.get("clan_members",{})
		return (
			_world_ready(v) and int(v.get("clan_id",0))==clan_id and
			str(v.get("clan_name",""))==CLAN_NAME and
			int(v.get("clan_leader_actor_id",0))==int(v.get("actor_id",0)) and
			members.has(int(v.get("actor_id",0)))
		),9000)
	if relog.is_empty():
		_fail("relog clan snapshot "+str(panel._latest),panel)
		return
	shell.set_world_active(true)
	shell.apply_snapshot(relog)
	await process_frame
	(guild_command as BaseButton).pressed.emit()
	await process_frame
	if not shell.clan_window.visible or shell._clan_name_value.text!=CLAN_NAME or shell._clan_member_count_value.text!="1":
		_fail("relog 057_CLUB_Menu did not restore authoritative state",panel)
		return

	panel.disconnect_session()
	shell.set_world_active(false)
	print("CLIENT_CLAN_CREATE_AES_PASS button_signal=true keyboard_input_event=true server_result=true snapshot=true classic_ui=true relog=true clan_id="+str(clan_id))
	quit(0)

func _type_text(value:String)->void:
	for character in value:
		var event:=InputEventKey.new()
		event.pressed=true
		event.unicode=character.unicode_at(0)
		Input.parse_input_event(event)
	await process_frame

func _world_ready(v:Dictionary)->bool:
	return (
		int(v.get("state",-1))==WorldState.STATE_IN_WORLD and
		bool(v.get("world_ready",false)) and
		int(v.get("pending_world_ready_ack_epoch",0))==0 and
		int(v.get("last_world_ready_ack_epoch",0))==int(v.get("map_epoch",0))
	)

func _wait_panel(panel,predicate:Callable,timeout_ms:int)->Dictionary:
	var deadline:=Time.get_ticks_msec()+timeout_ms
	while Time.get_ticks_msec()<deadline:
		var value:Dictionary=panel._latest.duplicate(true)
		if not value.is_empty() and predicate.call(value):
			return value
		if int(value.get("state",-1))==WorldState.STATE_FAILED and not bool(value.get("reconnecting",false)):
			return {}
		await process_frame
	return {}
