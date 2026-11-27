extends SceneTree
## Source geometry and rendering contract of the three bottom HK HUD groups.
const Shell = preload("res://ui/classic_world_shell.gd")
const WireCodec = preload("res://network/wire_codec.gd")
var failed := false

class ChatSessionStub:
	extends RefCounted
	var sent:Array[Dictionary]=[]
	func _record(channel:String,message:String,bubble_type:int)->bool:
		sent.append({"channel":channel,"message":message,"bubble_type":bubble_type})
		return true
	func send_world_chat(message:String,bubble_type:int=0)->bool:return _record("world",message,bubble_type)
	func send_warband_chat(message:String,bubble_type:int=0)->bool:return _record("warband",message,bubble_type)
	func send_party_chat(message:String,bubble_type:int=0)->bool:return _record("party",message,bubble_type)
	func send_nearby_chat(message:String,bubble_type:int=0)->bool:return _record("nearby",message,bubble_type)
	func send_whisper(target_actor_id:int,message:String,bubble_type:int=0)->bool:
		sent.append({"channel":"whisper_uin","target_actor_id":target_actor_id,"message":message,"bubble_type":bubble_type})
		return true
	func send_whisper_name(target_name:String,message:String,bubble_type:int=0)->bool:
		sent.append({"channel":"whisper_name","target_name":target_name,"message":message,"bubble_type":bubble_type})
		return true

class ChatPanelStub:
	extends RefCounted
	var session
	func _init(value)->void:session=value

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
	_expect(shell._chat_balloon_menu.size==Vector2(216,30),"019_TALKBUBBLE_BIG source size")
	_expect(not shell._chat_balloon_menu.visible,"balloon menu starts closed")
	_rect(shell._chat_balloon_menu.get_node("BalloonBack"),Rect2(3,3,14,24))
	_rect(shell._chat_balloon_menu.get_node("BalloonNext"),Rect2(18,3,14,24))
	_rect(shell._chat_balloon_menu.get_node("BalloonClose"),Rect2(195,6,16,16))
	_expect((shell._chat_balloon_menu.get_node("BalloonBack") as TextureButton).disabled and (shell._chat_balloon_menu.get_node("BalloonNext") as TextureButton).disabled,"six-entry HK BalloonStyle.dat disables paging")
	for index in 6:
		_rect(shell._chat_balloon_choices[index],Rect2(34+index*27,3,24,24))
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
	var oversized_zh:="繁".repeat(100)
	shell._chat_input.text=oversized_zh
	shell._chat_input.caret_column=oversized_zh.length()
	shell._chat_input.text_changed.emit(oversized_zh)
	_expect(shell._chat_input.text.to_utf8_buffer().size()<=WireCodec.CHAT_MAX_MESSAGE_BYTES,"Traditional Chinese input must respect authoritative UTF-8 byte cap")
	_expect(shell._chat_input.text.length()==85 and shell._chat_input.text.to_utf8_buffer().size()==255,"UTF-8 clamp must keep complete Chinese codepoints")
	var messages:Array=[]
	for index in 20:
		messages.append({"channel":0,"sender_name":"玩家","message":"附近訊息 %d"%index})
	messages.append({"channel":1,"sender_name":"世界玩家","message":"世界測試"})
	messages.append({"channel":2,"sender_name":"戰團成員","message":"戰團測試"})
	messages.append({"channel":3,"sender_name":"隊員","message":"隊伍測試"})
	messages.append({"channel":4,"sender_name":"好友","message":"私訊測試"})
	shell.apply_snapshot({"chat_messages":messages,"friends":{1:{"name":"好友一","online":true},2:{"name":"好友二","online":false}}})
	await process_frame
	_expect(shell._chat_history.text.contains("附近訊息 19"),"real snapshot history binding")
	_expect(shell._chat_history.text.contains("世界測試") and shell._chat_history.text.contains("戰團測試") and shell._chat_history.text.contains("隊伍測試") and shell._chat_history.text.contains("私訊測試"),"supported channels visible")
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
	_expect(not shell._chat_history.text.contains("世界測試") and shell._chat_history.text.contains("戰團測試"),"world display filter")
	shell._chat_tabs[2].pressed.emit()
	_expect(not shell._chat_history.text.contains("戰團測試") and shell._chat_history.text.contains("隊伍測試"),"warband display filter")
	shell._chat_tabs[3].pressed.emit()
	_expect(not shell._chat_history.text.contains("隊伍測試") and shell._chat_history.text.contains("私訊測試"),"party display filter")
	shell._chat_tabs[0].pressed.emit()
	_expect(not shell._chat_history.text.contains("私訊測試"),"private display filter")
	shell._chat_tabs[4].pressed.emit()
	_expect(not shell._chat_history.text.contains("附近訊息 19"),"nearby display filter")
	shell._chat_tabs[1].pressed.emit()
	shell._chat_tabs[2].pressed.emit()
	shell._chat_tabs[3].pressed.emit()
	shell._chat_tabs[0].pressed.emit()
	shell._chat_tabs[4].pressed.emit()
	_expect(shell._chat_history.text==before,"filters do not discard history")
	_expect(not shell._chat_tabs[4].disabled,"original E channel filter must be interactive")
	_expect(shell._chat_send_channel==0,"original channel type 0 starts on world")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==2,"original channel cycle world to Warband")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==3,"original channel cycle Warband to party")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==4,"original channel cycle party to nearby")
	shell._chat_channel.pressed.emit()
	_expect(shell._chat_send_channel==0,"original channel cycle nearby to world")
	var chat_session:=ChatSessionStub.new()
	shell._panel=ChatPanelStub.new(chat_session)
	for route in [[0,"world"],[2,"warband"],[3,"party"],[4,"nearby"]]:
		shell._chat_send_channel=int(route[0])
		shell._chat_bubble_type=5
		shell._chat_input.text="路由測試 "+str(route[1])
		shell._send_chat()
		var sent:Dictionary=chat_session.sent.back()
		_expect(str(sent.get("channel",""))==str(route[1]),"chat input routes original channel type %d"%int(route[0]))
		_expect(int(sent.get("bubble_type",-1))==5,"chat input preserves bubble type")
		_expect(shell._chat_input.text.is_empty(),"accepted chat clears editor")
	# Old ProcessPrivateMsg accepts /name <character> <message> and
	# /uin <numeric-id> <message> in this same source input control.
	shell._chat_bubble_type=4
	shell._chat_input.text="/name 好友一 私訊名稱測試"
	shell._send_chat()
	var name_whisper:Dictionary=chat_session.sent.back()
	_expect(str(name_whisper.get("channel",""))=="whisper_name" and str(name_whisper.get("target_name",""))=="好友一","/name routes to name-authoritative whisper")
	_expect(str(name_whisper.get("message",""))=="私訊名稱測試" and int(name_whisper.get("bubble_type",-1))==4,"/name preserves message and bubble")
	_expect(shell._chat_input.text.is_empty(),"accepted /name clears editor")
	shell._chat_input.text="/uin 42 私訊編號測試"
	shell._send_chat()
	var uin_whisper:Dictionary=chat_session.sent.back()
	_expect(str(uin_whisper.get("channel",""))=="whisper_uin" and int(uin_whisper.get("target_actor_id",0))==42,"/uin routes to numeric-authoritative whisper")
	_expect(str(uin_whisper.get("message",""))=="私訊編號測試" and int(uin_whisper.get("bubble_type",-1))==4,"/uin preserves message and bubble")
	_expect(shell._chat_input.text.is_empty(),"accepted /uin clears editor")
	var sent_before_invalid:=chat_session.sent.size()
	shell._chat_input.text="/name 缺訊息"
	shell._send_chat()
	_expect(chat_session.sent.size()==sent_before_invalid and shell._chat_input.text=="/name 缺訊息","invalid private command is not silently sent")
	shell._chat_send_channel=0
	var old_bubble_type:=shell._chat_bubble_type
	shell._chat_bubble.pressed.emit()
	_expect(shell._chat_balloon_menu.visible,"bubble icon opens original selector")
	(shell._chat_balloon_menu.get_node("BalloonClose") as TextureButton).pressed.emit()
	_expect(not shell._chat_balloon_menu.visible and shell._chat_bubble_type==old_bubble_type,"close cancels without changing bubble type")
	shell._chat_bubble.pressed.emit()
	shell._chat_balloon_choices[4].pressed.emit()
	_expect(shell._chat_bubble_type==4 and not shell._chat_balloon_menu.visible,"selecting style updates preview state and closes selector")
	# EXP percent is Server-authored from the source-backed PLAYERLVLUP table.
	# The original HUD must show that value directly and leave unknown (-1)
	# blank instead of inventing a Client-side threshold.
	shell.apply_snapshot({"chat_messages":[],"friends":{},"player_profile_ready":true,"player_level":3,"player_current_hp":154,"player_max_hp":154,"player_current_mp":125,"player_max_mp":125,"player_current_wp":101,"player_max_wp":101,"player_experience_percent":11,"player_money":12345})
	await process_frame
	_expect(shell._level_label.text=="3","authoritative level in original StatusLite")
	_expect(shell._hp_label.text=="154/154","authoritative HP text")
	_expect(shell._mp_label.text=="125/125","authoritative MP text")
	_expect(shell._wp_label.text=="101/101","authoritative WP/Will text")
	_expect(is_equal_approx(shell._hud_hp_clip.size.x,92.0),"authoritative HP bar width")
	_expect(is_equal_approx(shell._hud_mp_clip.size.x,92.0),"authoritative MP bar width")
	_expect(is_equal_approx(shell._hud_wp_clip.size.x,92.0),"authoritative WP bar width")
	_expect(shell._status_value_label.text=="12345","authoritative wallet in source s04 row")
	_rect(shell._status_value_label,Rect2(27,64,87,10))
	_expect(shell._exp_percent_label.text=="11%","authority EXP percent text")
	_expect(is_equal_approx(shell._hud_exp_clip.size.x,92.0*0.11),"authority EXP bar width")
	shell.apply_snapshot({"player_profile_ready":true,"player_experience_percent":-1})
	await process_frame
	_expect(shell._exp_percent_label.text.is_empty() and is_zero_approx(shell._hud_exp_clip.size.x),"unknown EXP percent stays blank")
	_expect(shell._status_value_label.text.is_empty(),"unknown money stays blank")
	shell.apply_snapshot({"player_money":0})
	_expect(shell._status_value_label.text=="0","known empty wallet displays zero")
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
	if not failed:print("HK_BOTTOM_HUD_PASS status=128x90 chat=469x90 friend=203x90 original_controls=true expanded=289x578 preserve_draft=true filters=private/world/warband/party/nearby channel_cycle=0-2-3-4-0 send_routes=true utf8_byte_cap=true")
	quit(1 if failed else 0)
