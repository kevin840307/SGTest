extends SceneTree
## Source geometry and rendering contract of the three bottom HK HUD groups.
const Shell = preload("res://ui/classic_world_shell.gd")
var failed := false

func _initialize()->void:
	root.size=Vector2i(800,600)
	_run.call_deferred()

func _expect(condition:bool,reason:String)->void:
	if not condition:
		failed=true
		push_error("HK_BOTTOM_HUD_FAIL "+reason)

func _rect(node:Control,expected:Rect2)->void:
	_expect(node.get_rect()==expected,"%s expected %s got %s"%[node.name,expected,node.get_rect()])

func _art(panel:Control,asset:String,size:Vector2)->void:
	var bg:=panel.get_node(asset+"Background") as TextureRect
	_rect(bg,Rect2(Vector2.ZERO,size))
	# CI intentionally does not redistribute private source art.
	if FileAccess.file_exists("res://ui/private_ie_hk2/"+asset+".png"):
		_expect(bg.texture!=null and bg.texture.get_size()==size,asset+" source dimensions")

func _run()->void:
	var shell:=Shell.new()
	root.add_child(shell)
	shell.set_world_active(true)
	await process_frame
	_rect(shell.status_panel,Rect2(0,510,128,90))
	_rect(shell.chat_panel,Rect2(128,510,469,90))
	_rect(shell.friend_panel,Rect2(597,510,203,90))
	_art(shell.status_panel,"HPBAR",Vector2(128,90))
	_art(shell.chat_panel,"CHANNEL",Vector2(469,90))
	_art(shell.friend_panel,"BG_FRIENDLIST_SMALL",Vector2(203,90))
	_expect(shell.chat_panel.get_node_or_null("BG_MESSAGE_INGAMEBackground")==null,"wrong modal background")
	_expect(shell.friend_panel.get_node_or_null("BG_RIGHTBOXBackground")==null,"wrong request menu background")
	_rect(shell._chat_history,Rect2(53,19,360,48))
	_rect(shell._chat_input_clip,Rect2(179,74,253,11))
	_rect(shell._chat_calendar,Rect2(52,74,106,12))
	_rect(shell._chat_system,Rect2(53,4,360,11))
	_rect(shell._chat_up,Rect2(418,20,17,25))
	_rect(shell._chat_down,Rect2(418,45,17,25))
	_rect(shell._chat_expand,Rect2(418,3,17,17))
	_rect(shell._chat_channel,Rect2(440,7,24,24))
	_rect(shell._chat_emotion,Rect2(440,35,24,24))
	_rect(shell._chat_bubble,Rect2(440,61,24,24))
	for index in 5:
		_rect(shell._chat_tabs[index],Rect2(3,4+index*17,43,15))
	_rect(shell._friend_scroll,Rect2(9,36,168,49))
	_rect(shell.friend_panel.get_node("FriendScrollUp"),Rect2(180,36,17,25))
	_rect(shell.friend_panel.get_node("FriendScrollDown"),Rect2(180,61,17,25))
	_expect(shell._chat_history.get_theme_stylebox("read_only") is StyleBoxEmpty,"history must not paint Godot gray panel")
	_expect(shell._chat_input.placeholder_text.is_empty(),"no fabricated placeholder over source art")
	_expect(shell._chat_calendar.text.is_empty(),"do not fabricate game date")
	_expect(shell.chat_panel.gui_input.is_connected(shell._movable_gui_input.bind(shell.chat_panel)),"chat must be movable")
	_expect(shell.friend_panel.gui_input.is_connected(shell._movable_gui_input.bind(shell.friend_panel)),"friend list must be movable")
	_expect(not shell._friend_editor.visible,"friend name editor must not overlay HUD")
	_expect(shell._friend_up.disabled and shell._friend_down.disabled,"empty friend list must show disabled arrows")
	_expect(shell._chat_input_clip.clip_contents,"chat input must clip to the 11px ITF slot")
	_expect(shell._chat_input.size.x==253 and shell._chat_input.size.y<=maxf(11,shell._chat_input.get_combined_minimum_size().y),"editor must settle after theme inheritance")
	var messages:Array=[]
	for index in 20:
		messages.append({"channel":0,"sender_name":"玩家","message":"附近訊息 %d"%index})
	messages.append({"channel":1,"sender_name":"世界玩家","message":"世界測試"})
	messages.append({"channel":3,"sender_name":"隊員","message":"隊伍測試"})
	messages.append({"channel":4,"sender_name":"好友","message":"私訊測試"})
	shell.apply_snapshot({"chat_messages":messages,"friends":{1:{"name":"好友一","online":true},2:{"name":"好友二","online":false}}})
	await process_frame
	_expect(shell._chat_history.text.contains("附近訊息 19"),"real snapshot history binding")
	_expect(shell._chat_history.text.contains("世界測試") and shell._chat_history.text.contains("隊伍測試") and shell._chat_history.text.contains("私訊測試"),"supported channels visible")
	_expect(shell._friend_rows.get_child_count()==2,"contact snapshot binding")
	_expect(shell._friend_up.disabled and shell._friend_down.disabled,"two friend rows need no scroll")
	var first_friend:=shell._friend_rows.get_child(0)
	var scrolled_messages:=messages.duplicate(true)
	scrolled_messages.append({"channel":0,"sender_name":"玩家","message":"新的聊天"})
	shell.apply_snapshot({"chat_messages":scrolled_messages,"friends":{1:{"name":"好友一","online":true},2:{"name":"好友二","online":false}}})
	_expect(shell._friend_rows.get_child(0)==first_friend,"chat does not rebuild unchanged contact rows")
	shell._chat_input.text="未送出的文字"
	var before:=shell._chat_history.text
	var old_position:=shell.chat_panel.position
	shell._chat_expand.pressed.emit()
	await process_frame
	_expect(shell._chat_big,"expand control")
	_art(shell.chat_panel,"BG_CHANNEL_BIG",Vector2(289,578))
	_rect(shell._chat_history,Rect2(7,42,255,480))
	_rect(shell._chat_input_clip,Rect2(7,531,253,11))
	_rect(shell._chat_up,Rect2(268,25,17,36))
	_rect(shell._chat_down,Rect2(268,491,17,36))
	_expect(shell._chat_history.text==before and shell._chat_input.text=="未送出的文字","expand preserves text and history")
	shell._chat_expand.pressed.emit()
	await process_frame
	_expect(not shell._chat_big and shell.chat_panel.position==old_position,"collapse restores original position")
	_rect(shell._chat_input_clip,Rect2(179,74,253,11))
	shell._chat_tabs[1].pressed.emit()
	_expect(not shell._chat_history.text.contains("世界測試") and shell._chat_history.text.contains("隊伍測試"),"world display filter")
	shell._chat_tabs[3].pressed.emit()
	_expect(not shell._chat_history.text.contains("隊伍測試") and shell._chat_history.text.contains("私訊測試"),"party display filter")
	shell._chat_tabs[0].pressed.emit()
	_expect(not shell._chat_history.text.contains("私訊測試"),"private display filter")
	shell._chat_tabs[1].pressed.emit()
	shell._chat_tabs[3].pressed.emit()
	shell._chat_tabs[0].pressed.emit()
	_expect(shell._chat_history.text==before,"filters do not discard history")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==1,"channel control routes to world channel")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==3,"channel control routes to party channel")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==0,"return to nearby channel")
	var args:=OS.get_cmdline_user_args()
	if not args.is_empty():
		# Native renderer only; capture the same 800x600 UI surface without
		# pretending these supplied fixture values are a live server session.
		shell._chat_input.clear()
		shell.apply_snapshot({"chat_messages":[],"friends":{},"player_profile_ready":true,"player_level":3,"player_current_hp":154,"player_max_hp":154,"player_current_mp":125,"player_max_mp":125,"player_current_wp":101,"player_max_wp":101,"player_experience_percent":11})
		await process_frame
		await RenderingServer.frame_post_draw
		_expect(root.get_texture().get_image().save_png(args[0])==OK,"native capture")
	shell.free()
	if not failed:print("HK_BOTTOM_HUD_PASS status=128x90 chat=469x90 friend=203x90 original_controls=true expanded=289x578 preserve_draft=true filters=true")
	quit(1 if failed else 0)
