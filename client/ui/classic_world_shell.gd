extends Control
signal skin_requested(id:String)
## Original IE HK2 in-world presentation.
## This shell owns only presentation/hitboxes. All state and actions delegate
## to the same SGTest SessionPanel/ClientSession as every other UI profile.
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const UiSkins = preload("res://ui/ui_skin_catalog.gd")
const Photos = preload("res://ui/local_item_photo_cache.gd")
const WireCodec = preload("res://network/wire_codec.gd")
const HotkeyDragButton = preload("res://ui/hotkey_drag_button.gd")
const HotkeySlot = preload("res://ui/hotkey_slot.gd")
const CharacterPreview = preload("res://player/character_preview.gd")
const AppearanceProfile = preload("res://player/character_appearance_profile.gd")

const PRIVATE := "res://ui/private_ie_hk2"
const BASE_SIZE := Vector2(800,600)
const ITEM_ROW_COUNT := 16
const SKILL_ROW_COUNT := 16
const HK_SHARED_FONT_FACE := "新細明體"
const HK_SHARED_FONT_HEIGHT := 12
const HK_SHARED_FONT_WEIGHT := 500
const HK_SHARED_FONT_BASELINE := 10.0
const HK_SHARED_FONT_BITMAP := PRIVATE + "/HK_SHARED_UI_FONT.fnt"
const LOCAL_SETTINGS_PATH := "user://sgtest_world_settings.cfg"

class SourceLineEdit extends Control:
	signal text_changed(value:String)
	signal text_submitted(value:String)
	var _editor:=LineEdit.new()
	var _max_length:=0
	var display_font:Font
	var display_font_size:=12
	var baseline:=10.0
	var max_length:int:
		get:return _max_length
		set(value):
			_max_length=value
			_editor.max_length=value
	var text:String:
		get:return _editor.text
		set(value):
			_editor.text=value
			queue_redraw()
	var caret_column:int:
		get:return _editor.caret_column
		set(value):_editor.caret_column=value

	func _ready()->void:
		clip_contents=true
		mouse_filter=Control.MOUSE_FILTER_STOP
		_editor.name="_Editor"
		_editor.modulate=Color(1,1,1,0)
		_editor.mouse_filter=Control.MOUSE_FILTER_IGNORE
		_editor.focus_mode=Control.FOCUS_ALL
		_editor.position=Vector2.ZERO
		add_child(_editor)
		_editor.text_changed.connect(_on_text_changed)
		_editor.text_submitted.connect(func(value:String):text_submitted.emit(value))
		_editor.focus_entered.connect(queue_redraw)
		_editor.focus_exited.connect(queue_redraw)

	func _on_text_changed(value:String)->void:
		text_changed.emit(value)
		queue_redraw()

	func focus_editor()->void:
		_editor.grab_focus()

	func release_editor_focus()->void:
		_editor.release_focus()

	func select_all()->void:
		_editor.select_all()

	func _gui_input(event:InputEvent)->void:
		if event is InputEventMouseButton and event.button_index==MOUSE_BUTTON_LEFT and event.pressed:
			_editor.grab_focus()
			accept_event()

	func _draw()->void:
		var font:=display_font if display_font!=null else get_theme_default_font()
		var color:=get_theme_color("font_color","LineEdit")
		draw_string(font,Vector2(0,baseline),text,HORIZONTAL_ALIGNMENT_LEFT,-1,display_font_size,color)
		if _editor.has_focus():
			var prefix:=text.substr(0,caret_column)
			var x:=font.get_string_size(prefix,HORIZONTAL_ALIGNMENT_LEFT,-1,display_font_size).x
			draw_line(Vector2(x,0),Vector2(x,size.y),color,1.0)

class CharacterText extends Control:
	var centered := false
	var font_size := 12
	var baseline := 10.0
	var text_font: Font
	var raster_font: Font
	var text := "":
		set(value):
			if text != value:
				text = value
				queue_redraw()

	func _draw() -> void:
		var alignment := HORIZONTAL_ALIGNMENT_CENTER if centered else HORIZONTAL_ALIGNMENT_LEFT
		var draw_font := raster_font if raster_font != null else (text_font if text_font != null else get_theme_default_font())
		draw_string(draw_font,Vector2(0,baseline),text,alignment,int(size.x),font_size,Color.WHITE)

var _panel
var _stats:Dictionary={}
var _active:=false
var _last_view:=Vector2.ZERO
var _photos:=Photos.new()
var _character_font:=SystemFont.new()
var _character_raster_font:Font

var stage:=Control.new()
var hud:=Control.new()
# HK persistent HUD groups are independent movable interfaces. The right-side
# Command strip is intentionally excluded: its buttons must remain click-only.
var status_panel:=Control.new()
var minimap_panel:=Control.new()
var _minimap_compact:=false
var hotkey_panel:=Control.new()
var _hotkey_page:=0
var _hotkey_slots:Array=[]
var _hotkey_refresh_requested:=false
var _pending_hotkey_rebind:Dictionary={}
var command_panel:=Control.new()
var menu:=Control.new() # retained as a non-visible compatibility handle
var secondary_menu:=Control.new()
var chat_panel:=Control.new()
var friend_panel:=Control.new()
var _drag_target:Control
var _drag_offset:=Vector2.ZERO
var inventory: Control
var character: Control
var skill_window: Control
var party_window: Control
var clan_window: Control
var shop_window: Control
var quest_window: Control
var player_shop_window: Control
var storage_window: Control
var bank_window: Control
var repair_window: Control
var trade_window: Control
var dialog_window: Control
var settings_window: Control
var _settings_skin_label: Label
var _settings_hint: Label
var _settings_bgm_value: RichTextLabel
var _settings_sfx_value: RichTextLabel
var _settings_quality_value: RichTextLabel
var _settings_battle_value: RichTextLabel
var _settings_bgm_level:=10
var _settings_sfx_level:=10
var _settings_high_fps:=true
var _settings_battle_mode:=0
var _system_action_blocker:=Control.new()
var _system_action_question:=Control.new()
var _system_action_question_text:=RichTextLabel.new()
var _system_action:=0 # 0=none, 1=back to server/login, 2=exit game
var _system_action_previous_focus:Control
var _skin_switch_visible:=false
var _windows:Array[Control]=[]

var _hp_label: RichTextLabel
var _mp_label: RichTextLabel
var _wp_label: RichTextLabel
var _level_label: RichTextLabel
var _exp_percent_label: RichTextLabel
var _status_value_label: RichTextLabel
var _hud_hp_clip:Control
var _hud_mp_clip:Control
var _hud_wp_clip:Control
var _hud_exp_clip:Control
var _hud_hp_fill:TextureRect
var _hud_mp_fill:TextureRect
var _hud_wp_fill:TextureRect
var _hud_exp_fill:TextureRect
var _hud_animated:Array[Dictionary]=[]
var _hud_anim_frame:=-1
var _map_title:Label
var _map_coords:Label
var _inventory_rows:=VBoxContainer.new()
var _equipment_rows:=Control.new()
var _inventory_item_bg:TextureRect
var _inventory_equip_bg:TextureRect
var _inventory_tab:=0
var _equipment_slot_buttons:Dictionary={}
var _equipment_slot_icons:Dictionary={}
var _equipment_preview:=CharacterPreview.new()
var _asset_store:RefCounted
# Preserve interactive inventory rows across unchanged AOI/status snapshots.
# Rebuilding every presentation tick invalidates active item click targets.
var _inventory_render_signature:Dictionary={}
var _inventory_render_initialized:=false
# Party and social lists are player-facing UI: unrelated 30Hz World updates
# should not destroy their buttons or force the chat viewport to scroll.
var _social_render_signatures:Dictionary={}
var _classic_mecha_uid:=0
var _mecha_window:Control
var _mecha_info_labels:Dictionary={}
var _mecha_slot_buttons:Dictionary={}
var _mecha_battery_rows:=VBoxContainer.new()
var _mecha_drive_button:TextureButton
var _mecha_exit_button:TextureButton
var _mecha_energy_hud:=Control.new()
var _mecha_energy_clip:Control
var _mecha_energy_fill:TextureRect
var _inventory_tab_buttons:Array[TextureButton]=[]
var _character_labels:Dictionary={}
var _character_point_buttons:Array[TextureButton]=[]
var _character_hp_fill:Control
var _character_mp_fill:Control
# Skill rows are owned by classic_skill_window.gd; 026_TEAM also uses only
# fixed ITF controls, so keep no unparented legacy VBoxContainer here.
var _party_member_labels:Array[RichTextLabel]=[]
var _party_hp_clips:Array[Control]=[]
var _party_mp_clips:Array[Control]=[]
var _party_member_actor_ids:Array[int]=[0,0,0,0,0,0]
var _party_question:=Control.new()
var _party_question_text:=RichTextLabel.new()
var _party_question_actor:=0
var _party_leave_blocker:=Control.new()
var _party_leave_question:=Control.new()
var _party_leave_question_text:=RichTextLabel.new()
var _party_leave_previous_focus:Control
var _request_menu:=Control.new()
var _request_menu_actor:=0
var _clan_rows:=VBoxContainer.new() # retained for compatibility; 060 uses fixed source rows below
var _clan_create_button:Button
var _clan_name_value:RichTextLabel
var _clan_member_count_value:RichTextLabel
var _clan_member_window:Control
var _clan_member_name_labels:Array[RichTextLabel]=[]
var _clan_member_role_labels:Array[RichTextLabel]=[]
var _clan_member_state_labels:Array[RichTextLabel]=[]
var _clan_member_actor_ids:Array[int]=[]
var _clan_member_offset:=0
var _clan_selected_actor:=0
var _clan_announcement_window:Control
var _clan_current_announcement:TextEdit
var _clan_announcement_input:TextEdit
var _clan_announcement_remaining:RichTextLabel
var _clan_announcement_confirm:TextureButton
var _clan_question:=Control.new()
var _clan_question_text:=RichTextLabel.new()
var _clan_question_actor:=0
var _clan_name_dialog:=Control.new()
var _clan_name_input:=SourceLineEdit.new()
var _clan_name_confirm:TextureButton
var _shop_buy_rows:=VBoxContainer.new()
var _shop_sell_rows:=VBoxContainer.new()
var _quest_title: Label
var _quest_text: Label
var _quest_choices:=VBoxContainer.new()
var _chat_history:=TextEdit.new()
var _chat_input:=LineEdit.new()
var _chat_input_normalizing:=false
var _chat_input_clip:=Control.new()
var _chat_bg:TextureRect
var _chat_up:TextureButton
var _chat_down:TextureButton
var _chat_expand:TextureButton
var _chat_channel:TextureButton
var _chat_bubble:TextureButton
var _chat_balloon_menu:=Control.new()
var _chat_balloon_choices:Array[TextureButton]=[]
var _chat_emotion_menu:=Control.new()
var _chat_emotion_choices:Array[TextureButton]=[]
var _chat_emotion_back:TextureButton
var _chat_emotion_next:TextureButton
var _chat_emotion_page:=0
var _chat_tabs:Array[TextureButton]=[]
var _chat_calendar:RichTextLabel
var _chat_system:RichTextLabel
var _chat_emotion:TextureButton
var _chat_big:=false
var _chat_small_position:=Vector2(128,510)
var _chat_send_channel:=0
var _chat_bubble_type:=0
var _chat_show_world:=true
var _chat_show_warband:=true
var _chat_show_party:=true
var _chat_show_private:=true
var _chat_show_nearby:=true
var _friend_scroll:ScrollContainer
var _friend_up:TextureButton
var _friend_down:TextureButton
var _friend_background:TextureRect
var _friend_expand:TextureButton
var _friend_large:=false
var _friend_small_position:=Vector2(597,510)
var _friend_editor:=PopupPanel.new()
var _friend_delete:TextureButton
var _friend_ignore:TextureButton
var _friend_edit:TextureButton
var _friend_rename:=Control.new()
var _friend_rename_input:=SourceLineEdit.new()
var _friend_rename_confirm:TextureButton
var _friend_rename_target_actor:=0
var _friend_list_button:TextureButton
var _friend_ignore_list_button:TextureButton
var _friend_show_ignored:=false
var _friend_selected_actor:=0
var _friend_rows:=VBoxContainer.new()
var _friend_name_input:=LineEdit.new()
var _friend_action:Button
var _friend_question:=Control.new()
var _friend_question_text:=Label.new()
var _friend_question_actor:=0
var _friend_mail_buttons:Array[TextureButton]=[]
var _friend_ordered_actors:Array[int]=[]
var _friend_visible_message_actors:Array[int]=[0,0,0]
var _friend_message_panel:=Control.new()
var _friend_message_title:RichTextLabel
var _friend_message_timestamp:RichTextLabel
var _friend_message_counter:RichTextLabel
var _friend_message_direction:RichTextLabel
var _friend_message_history:RichTextLabel
var _friend_message_input_aux:RichTextLabel
var _friend_message_input:=SourceLineEdit.new()
var _friend_message_up:TextureButton
var _friend_message_down:TextureButton
var _friend_message_actor:=0
var _friend_message_index:=-1
var _friend_message_auto_latest:=true
var _friend_message_last_count:=0
var _player_shop_rows:=VBoxContainer.new()
var _player_shop_inventory_rows:=VBoxContainer.new()
var _player_shop_buy:TextureButton
var _player_shop_action_text:RichTextLabel
var _player_shop_name_label:RichTextLabel
var _player_shop_name_dialog:=Control.new()
var _player_shop_name_input:=SourceLineEdit.new()
var _player_shop_name_confirm:TextureButton
var _player_shop_sell_price_dialog:=Control.new()
var _player_shop_sell_price_input:=SourceLineEdit.new()
var _player_shop_sell_quantity_dialog:=Control.new()
var _player_shop_sell_quantity_input:=SourceLineEdit.new()
var _player_shop_sell_uid:=0
var _player_shop_sell_price_value:=0
var _player_shop_buy_dialog:=Control.new()
var _player_shop_buy_seller:=0
var _player_shop_buy_listing:=0
var _player_shop_buy_quantity:RichTextLabel
var _player_shop_buy_total:RichTextLabel
var _player_shop_close_question:=Control.new()
var _player_shop_close_question_text:RichTextLabel
var _player_shop_sign_layer:=Control.new()
var _player_shop_signs:Dictionary={}
var _world_view
var _storage_rows:=VBoxContainer.new()
var _storage_inventory_rows:=VBoxContainer.new()
var _bank_status: Label
var _bank_amount:=SpinBox.new()
var _repair_rows:=VBoxContainer.new()
var _trade_rows:=VBoxContainer.new()
var _trade_status: Label
var _dialog_title: Label
var _dialog_text: Label
var _dialog_choices:=VBoxContainer.new()
var _dialog_key:=""
var _settings_picker:=OptionButton.new()

func _make_character_raster_font()->Font:
	# Target-HK shared text renderer: sub_4E92A9 passes the CP950 face bytes
	# for 新細明體 with height 12 / weight 500 into sub_561330, which creates
	# the GDI font and DIB used by the original text path. The ignored private
	# BMFont is generated locally from that renderer and is never committed.
	if FileAccess.file_exists(HK_SHARED_FONT_BITMAP):
		var bitmap:=FontFile.new()
		if bitmap.load_bitmap_font(HK_SHARED_FONT_BITMAP)==OK:
			return bitmap
	return _character_font

func _init()->void:
	name="ClassicWorldShell"
	mouse_filter=Control.MOUSE_FILTER_IGNORE
	set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	size=BASE_SIZE
	add_child(stage)
	stage.name="ClassicStage"
	stage.size=BASE_SIZE
	stage.mouse_filter=Control.MOUSE_FILTER_PASS
	stage.add_child(hud)
	_player_shop_sign_layer.name="PlayerShopSignLayer"
	_player_shop_sign_layer.size=BASE_SIZE
	_player_shop_sign_layer.mouse_filter=Control.MOUSE_FILTER_IGNORE
	stage.add_child(_player_shop_sign_layer)
	stage.move_child(_player_shop_sign_layer,0)
	hud.name="ClassicHud"
	hud.size=BASE_SIZE
	hud.mouse_filter=Control.MOUSE_FILTER_PASS
	# HK SGOClient.exe creates the shared GDI UI font as 新細明體,
	# lfHeight=12 and lfWeight=500. Keep the SystemFont metadata as a safe
	# fallback, but render through the local GDI-generated BMFont when present.
	_character_font.font_names=PackedStringArray([HK_SHARED_FONT_FACE])
	_character_font.font_weight=HK_SHARED_FONT_WEIGHT
	_character_raster_font=_make_character_raster_font()
	_build_hud()
	_build_inventory()
	_build_mecha()
	_build_character()
	_build_skill()
	_build_party()
	_build_clan()
	_build_shop()
	_build_quest()
	_build_player_shop()
	_build_storage()
	_build_bank()
	_build_repair()
	_build_trade()
	_build_dialog()
	_build_settings()
	_windows=[inventory,_mecha_window,character,skill_window,party_window,clan_window,_clan_member_window,_clan_announcement_window,shop_window,quest_window,player_shop_window,storage_window,bank_window,repair_window,trade_window,dialog_window,settings_window]
	visible=false
	set_process(false)

func _tex(name:String)->Texture2D:
	return UiTextures.get_texture(PRIVATE.path_join(name+".png"))

func _background(parent:Control,name:String,size:Vector2)->TextureRect:
	var texture:=TextureRect.new()
	texture.name=name+"Background"
	texture.texture=_tex(name)
	texture.position=Vector2.ZERO
	texture.size=size
	texture.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	texture.stretch_mode=TextureRect.STRETCH_SCALE
	texture.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	texture.mouse_filter=Control.MOUSE_FILTER_IGNORE
	parent.add_child(texture)
	parent.move_child(texture,0)
	return texture

func _classic_label(parent:Control,node_name:String,rect:Rect2,font_size:=11,center:=false)->Label:
	var label:=Label.new()
	label.name=node_name
	label.position=rect.position
	label.size=rect.size
	label.mouse_filter=Control.MOUSE_FILTER_IGNORE
	label.vertical_alignment=VERTICAL_ALIGNMENT_CENTER
	label.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER if center else HORIZONTAL_ALIGNMENT_LEFT
	if _character_raster_font!=null:
		label.add_theme_font_override("font",_character_raster_font)
	label.add_theme_font_size_override("font_size",font_size)
	label.add_theme_color_override("font_color",Color("#f9f2d6"))
	label.add_theme_color_override("font_shadow_color",Color(0,0,0,0.9))
	label.add_theme_constant_override("shadow_offset_x",1)
	label.add_theme_constant_override("shadow_offset_y",1)
	parent.add_child(label)
	return label

func _itf_text(parent:Control,node_name:String,rect:Rect2,font_size:=9,center:=false)->RichTextLabel:
	# RichTextLabel with fit_content=false has no intrinsic line-height minimum,
	# so the decoded ITF rectangle remains authoritative even for 10/12px rows.
	var label:=RichTextLabel.new()
	label.name=node_name
	label.position=rect.position
	label.size=rect.size
	label.fit_content=false
	label.scroll_active=false
	label.bbcode_enabled=false
	label.mouse_filter=Control.MOUSE_FILTER_IGNORE
	label.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER if center else HORIZONTAL_ALIGNMENT_LEFT
	label.vertical_alignment=VERTICAL_ALIGNMENT_CENTER
	label.add_theme_stylebox_override("normal",StyleBoxEmpty.new())
	if _character_raster_font!=null:
		label.add_theme_font_override("normal_font",_character_raster_font)
	label.add_theme_font_size_override("normal_font_size",font_size)
	label.add_theme_color_override("default_color",Color("#f9f2d6"))
	parent.add_child(label)
	return label

func _hit(parent:Control,node_name:String,rect:Rect2,callback:Callable,tooltip:String="")->Button:
	var button:=Button.new()
	button.name=node_name
	button.position=rect.position
	button.size=rect.size
	button.flat=true
	button.focus_mode=Control.FOCUS_NONE
	button.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
	button.tooltip_text=tooltip
	button.pressed.connect(callback)
	parent.add_child(button)
	return button

func _texture_button(parent:Control,node_name:String,art:String,position:Vector2,size:Vector2,callback:Callable,tooltip:String="")->TextureButton:
	var button:=TextureButton.new()
	button.name=node_name
	button.position=position
	button.size=size
	button.ignore_texture_size=true
	button.stretch_mode=TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	button.focus_mode=Control.FOCUS_NONE
	button.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
	button.tooltip_text=tooltip
	button.texture_normal=_tex(art+"_0")
	button.texture_hover=_tex(art+"_1")
	button.texture_pressed=_tex(art+"_2")
	button.texture_disabled=_tex(art+"_3")
	if callback.is_valid():button.pressed.connect(callback)
	parent.add_child(button)
	return button

func _animated_progress(parent:Control,node_name:String,rect:Rect2,art:String,frame_count:int)->Dictionary:
	var clip:=Control.new()
	clip.name=node_name+"Clip"
	clip.position=rect.position
	clip.size=rect.size
	clip.clip_contents=true
	clip.mouse_filter=Control.MOUSE_FILTER_IGNORE
	parent.add_child(clip)
	var fill:=TextureRect.new()
	fill.name=node_name+"Fill"
	fill.texture=_tex(art+"_0")
	fill.size=rect.size
	fill.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	fill.stretch_mode=TextureRect.STRETCH_SCALE
	fill.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	fill.mouse_filter=Control.MOUSE_FILTER_IGNORE
	clip.add_child(fill)
	var row:={"clip":clip,"fill":fill,"art":art,"frames":frame_count,"width":rect.size.x,"height":rect.size.y}
	_hud_animated.append(row)
	return row

func _set_progress(clip:Control,current:int,maximum:int)->void:
	if clip==null:return
	var full_width:=float(clip.get_meta("full_width",clip.size.x))
	if not clip.has_meta("full_width"):
		clip.set_meta("full_width",full_width)
	var ratio:=0.0 if maximum<=0 else clampf(float(current)/float(maximum),0.0,1.0)
	clip.size.x=full_width*ratio

func _animate_status_bars()->void:
	if not _active:return
	var tick:=int(Time.get_ticks_msec()/70)
	if tick==_hud_anim_frame:return
	_hud_anim_frame=tick
	for row in _hud_animated:
		var frames:=int(row.frames)
		if frames<=0:continue
		var fill:TextureRect=row.fill
		fill.texture=_tex("%s_%d"%[str(row.art),tick%frames])

func _process(_delta:float)->void:
	_animate_status_bars()
	_refresh_player_shop_signs()


func _unhandled_key_input(event:InputEvent)->void:
	if _system_action_blocker.visible:
		if event.is_action_pressed("ui_accept"):
			_confirm_system_action()
		elif event.is_action_pressed("ui_cancel"):
			_cancel_system_action()
		get_viewport().set_input_as_handled()
		return
	if _party_leave_blocker.visible:
		if event.is_action_pressed("ui_accept"):
			_confirm_party_leave()
		elif event.is_action_pressed("ui_cancel"):
			_cancel_party_leave()
		get_viewport().set_input_as_handled()
		return
	if not event.is_action_pressed("ui_cancel"):
		_handle_hotkey_key(event)
		return
	if _party_question.visible:
		_reject_party_invite()
		get_viewport().set_input_as_handled()
		return
	if _clan_question.visible:
		_reject_clan_invite()
		get_viewport().set_input_as_handled()
		return
	if _request_menu.visible:
		close_player_request_menu()
		get_viewport().set_input_as_handled()
		return
	if _friend_question.visible:
		_reject_friend_invite()
		get_viewport().set_input_as_handled()
		return
	if _player_shop_buy_dialog.visible:
		_cancel_player_shop_buy()
		get_viewport().set_input_as_handled()
		return
	if _player_shop_sell_price_dialog.visible or _player_shop_sell_quantity_dialog.visible:
		_cancel_player_shop_sell()
		get_viewport().set_input_as_handled()
		return
	if _player_shop_close_question.visible:
		_cancel_player_shop_close_all()
		get_viewport().set_input_as_handled()
		return
	if _player_shop_name_dialog.visible:
		_close_player_shop_name_dialog()
		get_viewport().set_input_as_handled()
		return
	if _panel!=null and _panel.cancel_pre_use_skill():
		get_viewport().set_input_as_handled()


func _transparent_line_edit(edit:LineEdit)->void:
	var empty:=StyleBoxEmpty.new()
	edit.add_theme_stylebox_override("normal",empty)
	edit.add_theme_stylebox_override("focus",StyleBoxEmpty.new())
	edit.add_theme_stylebox_override("read_only",StyleBoxEmpty.new())
	edit.add_theme_color_override("font_color",Color("#f9f2d6"))
	edit.add_theme_color_override("font_placeholder_color",Color("#bda982"))
	edit.add_theme_color_override("caret_color",Color.WHITE)
	if _character_raster_font!=null:
		edit.add_theme_font_override("font",_character_raster_font)
	edit.add_theme_font_size_override("font_size",11)

func _deferred_background(parent:Control,name:String,size:Vector2)->TextureRect:
	var texture:=TextureRect.new()
	texture.name=name+"Background"
	texture.position=Vector2.ZERO
	texture.size=size
	texture.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	texture.stretch_mode=TextureRect.STRETCH_SCALE
	texture.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	texture.mouse_filter=Control.MOUSE_FILTER_IGNORE
	texture.set_meta("classic_art_name",name)
	parent.add_child(texture)
	parent.move_child(texture,0)
	return texture

func _ensure_window_art(window:Control)->void:
	for child in window.get_children():
		if child is TextureRect and child.has_meta("classic_art_name") and child.texture==null:
			child.texture=_tex(str(child.get_meta("classic_art_name")))

func _make_movable(control:Control)->void:
	# Original HK interfaces are movable by pressing any non-interactive area.
	# Child Buttons/LineEdits keep MOUSE_FILTER_STOP, so clicking them performs
	# their action instead of beginning a drag.
	control.mouse_filter=Control.MOUSE_FILTER_STOP
	control.gui_input.connect(_movable_gui_input.bind(control))

func _clamp_drag_position(control:Control,position:Vector2)->Vector2:
	# Keep a small grab strip on-screen even for the source windows wider/taller
	# than the currently visible viewport.
	const GRAB:=24.0
	return Vector2(
		clampf(position.x,-control.size.x+GRAB,BASE_SIZE.x-GRAB),
		clampf(position.y,-control.size.y+GRAB,BASE_SIZE.y-GRAB))

func _movable_gui_input(event:InputEvent,control:Control)->void:
	if event is InputEventMouseButton and event.button_index==MOUSE_BUTTON_LEFT:
		if event.pressed:
			_drag_target=control
			_drag_offset=stage.get_local_mouse_position()-control.position
			var parent:=control.get_parent()
			if parent!=null:
				parent.move_child(control,parent.get_child_count()-1)
			control.accept_event()
		elif _drag_target==control:
			_drag_target=null
			control.accept_event()
	elif event is InputEventMouseMotion and _drag_target==control:
		control.position=_clamp_drag_position(control,stage.get_local_mouse_position()-_drag_offset)
		control.accept_event()

func _window(name:String,background:String,size:Vector2,position:Vector2)->Control:
	var window:=Control.new()
	window.name=name
	window.position=position
	window.size=size
	stage.add_child(window)
	_make_movable(window)
	_deferred_background(window,background,size)
	_texture_button(window,"CloseWindow","CLOSESCREEN",Vector2(size.x-20,3),Vector2(17,17),window.hide,"關閉")
	window.hide()
	return window

func _build_hud()->void:
	# 006_StatusLite.itf: keep every component in ITF-local coordinates.
	status_panel.name="ClassicStatusLite"
	status_panel.position=Vector2(0,510)
	status_panel.size=Vector2(128,90)
	hud.add_child(status_panel)
	_make_movable(status_panel)
	_background(status_panel,"HPBAR",status_panel.size)
	var hp_row:=_animated_progress(status_panel,"HP",Rect2(24,20,92,10),"HP",16)
	_hud_hp_clip=hp_row.clip;_hud_hp_fill=hp_row.fill
	var mp_row:=_animated_progress(status_panel,"MP",Rect2(24,34,92,10),"MP",16)
	_hud_mp_clip=mp_row.clip;_hud_mp_fill=mp_row.fill
	var wp_row:=_animated_progress(status_panel,"WP",Rect2(24,48,92,10),"WP",18)
	_hud_wp_clip=wp_row.clip;_hud_wp_fill=wp_row.fill
	var exp_row:=_animated_progress(status_panel,"EXP",Rect2(24,80,92,4),"EXP",16)
	_hud_exp_clip=exp_row.clip;_hud_exp_fill=exp_row.fill
	_level_label=_itf_text(status_panel,"Level",Rect2(42,5,25,10),9,true)
	_exp_percent_label=_itf_text(status_panel,"ExpPercent",Rect2(76,5,25,10),9,true)
	_status_value_label=_itf_text(status_panel,"StatusValue",Rect2(27,64,87,10),9,true)
	# CELProgressBar draws current/max over the HP/MP/WP bar itself. Mirror that
	# instead of placing separate labels elsewhere on the HUD.
	_hp_label=_itf_text(status_panel,"HPText",Rect2(24,20,92,10),8,true)
	_mp_label=_itf_text(status_panel,"MPText",Rect2(24,34,92,10),8,true)
	_wp_label=_itf_text(status_panel,"WPText",Rect2(24,48,92,10),8,true)

	minimap_panel.name="ClassicMinimap"
	minimap_panel.position=Vector2(647,7)
	minimap_panel.size=Vector2(133,122)
	hud.add_child(minimap_panel)
	_make_movable(minimap_panel)
	var minimap:=TextureRect.new()
	minimap.name="MiniMap"
	minimap.texture=_tex("MINIMAP_L")
	minimap.size=minimap_panel.size
	minimap.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	minimap.stretch_mode=TextureRect.STRETCH_SCALE
	minimap.mouse_filter=Control.MOUSE_FILTER_IGNORE
	minimap_panel.add_child(minimap)
	# 008_MINIMAP_L.itf: the 115x81 source-backed overlay is hidden when
	# switched to the separate 133x20 compact 008_MINIMAP_S.itf layout.
	var map_points:=TextureRect.new()
	map_points.name="MinimapPointsArt"
	map_points.position=Vector2(8,37)
	map_points.size=Vector2(115,81)
	map_points.texture=_tex("BG_MINIMAP_POINT")
	map_points.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	map_points.stretch_mode=TextureRect.STRETCH_KEEP
	map_points.mouse_filter=Control.MOUSE_FILTER_IGNORE
	minimap_panel.add_child(map_points)
	_texture_button(minimap_panel,"MinimapSizeToggle","MINISCREEN",Vector2(115,1),Vector2(17,17),_toggle_minimap_size,"縮小地圖")

	# 007_HOTKEY_Yl.itf: keep the source 30x358 strip and every label/control
	# at its decoded local rectangle. The previous 27.2px approximation was
	# visibly wrong, especially F2/F3 and F10-F12.
	hotkey_panel.name="ClassicHotkey"
	hotkey_panel.position=Vector2(2,17)
	hotkey_panel.size=Vector2(30,358)
	hud.add_child(hotkey_panel)
	_make_movable(hotkey_panel)
	var hotkey:=TextureRect.new()
	hotkey.name="HotkeyArt"
	hotkey.texture=_tex("HOTKEY_LY")
	hotkey.size=hotkey_panel.size
	hotkey.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	hotkey.stretch_mode=TextureRect.STRETCH_SCALE
	hotkey.mouse_filter=Control.MOUSE_FILTER_IGNORE
	hotkey_panel.add_child(hotkey)
	_texture_button(hotkey_panel,"HotkeyPage1","OFFON",Vector2(3,4),Vector2(11,11),func():_select_hotkey_page(0),"快捷列頁 1")
	_texture_button(hotkey_panel,"HotkeyPage2","OFFON",Vector2(16,4),Vector2(11,11),func():_select_hotkey_page(1),"快捷列頁 2")
	_select_hotkey_page(0)
	var hotkey_rects:=[
		Rect2(4,18,12,12),Rect2(4,47,12,12),Rect2(4,72,12,12),Rect2(4,99,12,12),
		Rect2(4,126,12,12),Rect2(4,153,12,12),Rect2(4,180,12,12),Rect2(4,207,12,12),
		Rect2(4,234,12,12),Rect2(4,261,18,12),Rect2(4,288,18,12),Rect2(4,315,18,12)]
	for i in 12:
		var hotkey_label:=_itf_text(hotkey_panel,"Hotkey%d"%i,hotkey_rects[i],9,false)
		# Fixed ITF key captions are single-line even at F10-F12; wrapping
		# would move text outside the original twelve-pixel-high row.
		hotkey_label.autowrap_mode=TextServer.AUTOWRAP_OFF
		hotkey_label.text="F%d"%(i+1)
		var slot:=HotkeySlot.new()
		slot.name="HotkeySlot%d"%i
		slot.slot_index=i
		slot.position=Vector2(4,18+27*i)
		slot.size=Vector2(24,26)
		slot.mouse_filter=Control.MOUSE_FILTER_STOP
		slot.binding_dropped.connect(_on_hotkey_binding_dropped)
		slot.activated.connect(_on_hotkey_slot_activated)
		var icon:=TextureRect.new()
		icon.name="Icon"
		icon.position=Vector2(1,1)
		icon.size=Vector2(22,22)
		icon.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		icon.mouse_filter=Control.MOUSE_FILTER_IGNORE
		slot.add_child(icon)
		hotkey_panel.add_child(slot)
		_hotkey_slots.append(slot)
	var size_button:=_texture_button(hotkey_panel,"HotkeySizeToggle","HOTKEY_YS_B",Vector2(3,342),Vector2(24,13),_toggle_hotkey_size,"切換快捷列大小")
	# The HK expand/collapse SPR has two frames: normal and highlighted.
	# Never request non-existent pressed/disabled frames 2 and 3.
	size_button.texture_pressed=_tex("HOTKEY_YS_B_1")
	size_button.texture_disabled=_tex("HOTKEY_YS_B_0")

	# 021_COMMAND.itf: the right-side command strip is fixed/clickable, not draggable.
	# Local control rects come from the decoded HK ITF; in particular the bag is
	# B_BASIC_ITEM at (3,32,24,23), not the temporary bottom command row.
	command_panel.name="ClassicCommand"
	command_panel.position=Vector2(770,0)
	command_panel.size=Vector2(30,249)
	command_panel.mouse_filter=Control.MOUSE_FILTER_PASS
	hud.add_child(command_panel)
	var command:=TextureRect.new()
	command.name="CommandArt"
	command.texture=_tex("COMMAND")
	command.size=command_panel.size
	command.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	command.stretch_mode=TextureRect.STRETCH_SCALE
	command.mouse_filter=Control.MOUSE_FILTER_IGNORE
	command_panel.add_child(command)
	secondary_menu.name="SecondaryCommandHitboxes"
	secondary_menu.size=command_panel.size
	secondary_menu.mouse_filter=Control.MOUSE_FILTER_PASS
	command_panel.add_child(secondary_menu)
	_texture_button(secondary_menu,"PlayerCommand","B_BASIC_PLAYER",Vector2(3,5),Vector2(24,23),func():_toggle(character),"人物狀態")
	_texture_button(secondary_menu,"ItemCommand","B_BASIC_ITEM",Vector2(3,32),Vector2(24,23),func():_toggle(inventory),"包包")
	_texture_button(secondary_menu,"SkillCommand","B_BASIC_SKILL",Vector2(3,59),Vector2(24,23),func():_toggle(skill_window),"技能")
	_texture_button(secondary_menu,"PartyCommand","B_COMMAND_TEAM",Vector2(2,86),Vector2(24,23),func():_toggle(party_window),"隊伍")
	_texture_button(secondary_menu,"GuildCommand","B_BASIC_GUILD",Vector2(3,113),Vector2(24,23),_open_clan_ui,"戰團")
	_texture_button(secondary_menu,"PlayerShopCommand","B_BASIC_PSHOP",Vector2(4,142),Vector2(21,19),_open_player_shop_ui,"小商店")
	_texture_button(secondary_menu,"QuestCommand","B_BASIC_QUEST",Vector2(2,169),Vector2(25,22),func():_toggle(quest_window),"任務")
	_texture_button(secondary_menu,"GmCommand","B_BASIC_GM",Vector2(2,196),Vector2(25,22),func():pass,"GM")
	_texture_button(secondary_menu,"SystemCommand","B_BASIC_SYSTEM",Vector2(2,222),Vector2(24,23),func():_toggle(settings_window),"系統")

	_build_chat_panel()
	_build_friend_panel()

	# Compatibility handle for older tests/callers. The temporary bottom command
	# bar is no longer player-visible; HK uses the fixed right-side 021_COMMAND.
	menu.name="MainCommandBar"
	menu.position=Vector2(615,489)
	menu.size=Vector2(145,28)
	menu.visible=false
	hud.add_child(menu)
	_texture_button(menu,"Menu0","B_BASIC_GUILD",Vector2(0,2),Vector2(24,23),_menu_action.bind(0),"戰團")
	_texture_button(menu,"Menu1","B_BASIC_PLAYER",Vector2(29,2),Vector2(24,23),_menu_action.bind(1),"人物狀態")
	_texture_button(menu,"Menu2","B_BASIC_ITEM",Vector2(58,2),Vector2(24,23),_menu_action.bind(2),"包包")
	_texture_button(menu,"Menu3","B_BASIC_PSHOP",Vector2(88,4),Vector2(24,23),_menu_action.bind(3),"小商店")
	_texture_button(menu,"Menu4","B_BASIC_SYSTEM",Vector2(117,2),Vector2(24,23),_menu_action.bind(4),"系統")

# 016_CHANNEL / 017_CHANNEL_BIG: use the actual HK backgrounds, not the
# unrelated 033_MESSAGE_INGAME dialog. Both layouts share one history/input.
func _build_chat_panel()->void:
	chat_panel.name="ClassicChatPanel"
	chat_panel.position=_chat_small_position
	chat_panel.size=Vector2(469,90)
	hud.add_child(chat_panel)
	_make_movable(chat_panel)
	_chat_bg=_background(chat_panel,"CHANNEL",chat_panel.size)
	_chat_history.name="ChatHistory"
	_chat_history.editable=false
	_chat_history.wrap_mode=TextEdit.LINE_WRAPPING_BOUNDARY
	_chat_history.focus_mode=Control.FOCUS_NONE
	_chat_history.mouse_filter=Control.MOUSE_FILTER_IGNORE
	for state in ["normal","read_only","focus"]:
		_chat_history.add_theme_stylebox_override(state,StyleBoxEmpty.new())
	for color_name in ["font_color","font_readonly_color"]:
		_chat_history.add_theme_color_override(color_name,Color.WHITE)
	_chat_history.add_theme_color_override("background_color",Color.TRANSPARENT)
	_chat_history.add_theme_font_override("font",_character_raster_font)
	_chat_history.add_theme_font_size_override("font_size",12)
	_chat_history.add_theme_constant_override("line_spacing",0)
	chat_panel.add_child(_chat_history)
	_chat_history.get_v_scroll_bar().modulate=Color.TRANSPARENT
	_chat_history.get_v_scroll_bar().mouse_filter=Control.MOUSE_FILTER_IGNORE
	_chat_history.get_v_scroll_bar().value_changed.connect(func(_value):_update_chat_scroll_buttons())
	_chat_input.name="ChatInput"
	# The authoritative SGTest chat wire is capped in UTF-8 bytes, not Unicode
	# codepoints. Keep the original single-line editor geometry while preventing
	# multibyte Traditional Chinese input from looking valid and then being
	# rejected only when Enter reaches ClientSession.
	_chat_input.max_length=WireCodec.CHAT_MAX_MESSAGE_BYTES
	_transparent_line_edit(_chat_input)
	_chat_input.text_changed.connect(_limit_chat_input_bytes)
	_chat_input.text_submitted.connect(func(_value):_send_chat())
	# Godot LineEdit's intrinsic font minimum can exceed the 11px ITF
	# slot (31px before theme inheritance). Constrain rendering and hitbox
	# with the source rect, and center the actual editor after theme setup.
	_chat_input_clip.name="ChatInputSlot"
	_chat_input_clip.clip_contents=true
	_chat_input_clip.mouse_filter=Control.MOUSE_FILTER_IGNORE
	chat_panel.add_child(_chat_input_clip)
	_chat_input_clip.add_child(_chat_input)
	_chat_input.minimum_size_changed.connect(_fit_chat_editor)
	_fit_chat_editor.call_deferred()
	# ID 2213 is the in-game calendar (Old CChatItf::UpdateCalendar).
	# Do not substitute wall-clock time when the server has not sent a calendar.
	_chat_calendar=_itf_text(chat_panel,"GameCalendar",Rect2(52,74,106,12),12)
	_chat_system=_itf_text(chat_panel,"SystemMessage",Rect2(53,4,360,11),11)
	var arts:=["B_CHANNEL_CB","B_CHANNEL_CA","B_CHANNEL_CC","B_CHANNEL_CD","B_CHANNEL_CE"]
	for index in arts.size():
		var button:=_texture_button(chat_panel,"ChatFilter"+str(index),arts[index],Vector2.ZERO,Vector2(43,15),_toggle_chat_filter.bind(index))
		# Source/reference default checked artwork is frame 2.
		button.texture_normal=_tex(arts[index]+"_2")
		button.texture_pressed=_tex(arts[index]+"_0")
		button.tooltip_text=["私訊顯示","世界頻道顯示","戰團頻道顯示","隊伍顯示","附近頻道顯示"][index]
		_chat_tabs.append(button)
	_chat_up=_texture_button(chat_panel,"ChatScrollUp","SCROLLUP_S",Vector2.ZERO,Vector2(17,25),_scroll_chat.bind(-1),"往上捲動")
	_chat_down=_texture_button(chat_panel,"ChatScrollDown","SCROLLDOWN_S",Vector2.ZERO,Vector2(17,25),_scroll_chat.bind(1),"往下捲動")
	_chat_expand=_texture_button(chat_panel,"ChatExpand","ZONESCREEN",Vector2.ZERO,Vector2(17,17),_toggle_chat_size,"展開聊天")
	_chat_channel=_texture_button(chat_panel,"ChatChannel","ICON_CHANNEL",Vector2.ZERO,Vector2(24,24),_toggle_chat_send_channel,"世界聊天（Lv10）；點擊切換戰團")
	_chat_channel.texture_normal=_tex("ICON_CHANNEL_3")
	_chat_channel.texture_hover=_chat_channel.texture_normal
	_chat_channel.texture_pressed=_chat_channel.texture_normal
	_chat_emotion=_texture_button(chat_panel,"ChatEmotion","B_EMOTETION",Vector2.ZERO,Vector2(24,24),_open_chat_emotion_menu,"選擇表情")
	_chat_bubble=_texture_button(chat_panel,"ChatBubble","ICON_BUBBLE",Vector2.ZERO,Vector2(24,24),_open_chat_balloon_menu,"選擇聊天氣泡")
	_set_chat_bubble_preview()
	_build_chat_balloon_menu()
	_build_chat_emotion_menu()
	_layout_chat_panel()
	_update_chat_scroll_buttons()

func _set_chat_rect(control:Control,rect:Rect2)->void:
	control.position=rect.position
	control.size=rect.size

func _fit_chat_editor()->void:
	var height:=maxf(_chat_input_clip.size.y,_chat_input.get_combined_minimum_size().y)
	_chat_input.size=Vector2(_chat_input_clip.size.x,height)
	_chat_input.position=Vector2(0,floorf((_chat_input_clip.size.y-height)/2.0))

func _limit_chat_input_bytes(value:String)->void:
	if _chat_input_normalizing or value.to_utf8_buffer().size()<=WireCodec.CHAT_MAX_MESSAGE_BYTES:
		return
	var trimmed:=value
	while not trimmed.is_empty() and trimmed.to_utf8_buffer().size()>WireCodec.CHAT_MAX_MESSAGE_BYTES:
		trimmed=trimmed.left(trimmed.length()-1)
	var caret:=mini(_chat_input.caret_column,trimmed.length())
	_chat_input_normalizing=true
	_chat_input.text=trimmed
	_chat_input.caret_column=caret
	_chat_input_normalizing=false

func _set_chat_button_art(button:TextureButton,art:String)->void:
	button.texture_normal=_tex(art+"_0")
	button.texture_hover=_tex(art+"_1")
	button.texture_pressed=_tex(art+"_2")
	button.texture_disabled=_tex(art+"_3")

func _layout_chat_panel()->void:
	chat_panel.size=Vector2(289,578) if _chat_big else Vector2(469,90)
	_chat_bg.texture=_tex("BG_CHANNEL_BIG" if _chat_big else "CHANNEL")
	_chat_bg.size=chat_panel.size
	_chat_bg.name="BG_CHANNEL_BIGBackground" if _chat_big else "CHANNELBackground"
	var tab_positions:Array[Vector2]
	if _chat_big:
		tab_positions=[Vector2(195,5),Vector2(7,5),Vector2(54,5),Vector2(101,5),Vector2(148,5)]
		_set_chat_rect(_chat_history,Rect2(7,42,255,480))
		_set_chat_rect(_chat_input_clip,Rect2(7,531,253,11))
		_set_chat_rect(_chat_calendar,Rect2(8,548,106,11))
		_set_chat_rect(_chat_system,Rect2(8,26,255,11))
		_set_chat_rect(_chat_up,Rect2(268,25,17,36))
		_set_chat_rect(_chat_down,Rect2(268,491,17,36))
		_set_chat_rect(_chat_expand,Rect2(268,2,16,16))
		_set_chat_rect(_chat_channel,Rect2(186,549,24,24))
		_set_chat_rect(_chat_emotion,Rect2(213,549,24,24))
		_set_chat_rect(_chat_bubble,Rect2(239,549,24,24))
	else:
		tab_positions=[Vector2(3,4),Vector2(3,21),Vector2(3,38),Vector2(3,55),Vector2(3,72)]
		_set_chat_rect(_chat_history,Rect2(53,19,360,48))
		_set_chat_rect(_chat_input_clip,Rect2(179,74,253,11))
		_set_chat_rect(_chat_calendar,Rect2(52,74,106,12))
		_set_chat_rect(_chat_system,Rect2(53,4,360,11))
		_set_chat_rect(_chat_up,Rect2(418,20,17,25))
		_set_chat_rect(_chat_down,Rect2(418,45,17,25))
		_set_chat_rect(_chat_expand,Rect2(418,3,17,17))
		_set_chat_rect(_chat_channel,Rect2(440,7,24,24))
		_set_chat_rect(_chat_emotion,Rect2(440,35,24,24))
		_set_chat_rect(_chat_bubble,Rect2(440,61,24,24))
	_fit_chat_editor.call_deferred()
	for index in tab_positions.size():
		_chat_tabs[index].position=tab_positions[index]
	_set_chat_button_art(_chat_up,"SCROLLUP_BUTTON" if _chat_big else "SCROLLUP_S")
	_set_chat_button_art(_chat_down,"SCROLLDOWN_BUTTON" if _chat_big else "SCROLLDOWN_S")
	_set_chat_button_art(_chat_expand,"CLOSESCREEN" if _chat_big else "ZONESCREEN")
	_chat_expand.tooltip_text="收合聊天" if _chat_big else "展開聊天"

func _toggle_chat_size()->void:
	if not _chat_big:
		_chat_small_position=chat_panel.position
		_chat_big=true
		_layout_chat_panel()
		chat_panel.position=Vector2(clampf(_chat_small_position.x,0,BASE_SIZE.x-289),22)
	else:
		_chat_big=false
		_layout_chat_panel()
		chat_panel.position=_chat_small_position
	_update_chat_scroll_buttons.call_deferred()

func _scroll_chat(direction:int)->void:
	_chat_history.set_v_scroll(_chat_history.get_v_scroll()+direction)
	_update_chat_scroll_buttons()

func _update_chat_scroll_buttons()->void:
	if _chat_up==null or _chat_down==null:return
	var scroll:=_chat_history.get_v_scroll_bar()
	_chat_up.disabled=scroll.value<=scroll.min_value
	_chat_down.disabled=scroll.value>=maxf(scroll.min_value,scroll.max_value-scroll.page)-0.01

func _toggle_chat_filter(index:int)->void:
	if index==0:
		_chat_show_private=not _chat_show_private
		_chat_tabs[0].texture_normal=_tex("B_CHANNEL_CB_2" if _chat_show_private else "B_CHANNEL_CB_0")
	elif index==1:
		_chat_show_world=not _chat_show_world
		_chat_tabs[1].texture_normal=_tex("B_CHANNEL_CA_2" if _chat_show_world else "B_CHANNEL_CA_0")
	elif index==2:
		_chat_show_warband=not _chat_show_warband
		_chat_tabs[2].texture_normal=_tex("B_CHANNEL_CC_2" if _chat_show_warband else "B_CHANNEL_CC_0")
	elif index==3:
		_chat_show_party=not _chat_show_party
		_chat_tabs[3].texture_normal=_tex("B_CHANNEL_CD_2" if _chat_show_party else "B_CHANNEL_CD_0")
	elif index==4:
		_chat_show_nearby=not _chat_show_nearby
		_chat_tabs[4].texture_normal=_tex("B_CHANNEL_CE_2" if _chat_show_nearby else "B_CHANNEL_CE_0")
	_refresh_chat_history()

func _toggle_chat_send_channel()->void:
	# Old CChatItf::OnSwitchChannel stores the original channel type at +0x13c:
	# 0 -> 2 -> 3 -> 4 -> 0. Type 1 is private chat and is deliberately skipped;
	# type 6 is a locked/special state and is not entered by the normal button.
	# Old ProcessSendToChannel proves type 0 is WORLDCHANNEL. The remaining tabs
	# line up with C/D/E: clan, team, and nearby respectively.
	match _chat_send_channel:
		0: _chat_send_channel=2
		2: _chat_send_channel=3
		3: _chat_send_channel=4
		_: _chat_send_channel=0
	# Exact HK ICON_CHANNEL frame semantics beyond the known party variant still
	# need native A/B; retain the source-backed frames instead of inventing art.
	_chat_channel.texture_normal=_tex("ICON_CHANNEL_2" if _chat_send_channel==3 else "ICON_CHANNEL_3")
	_chat_channel.texture_hover=_chat_channel.texture_normal
	_chat_channel.texture_pressed=_chat_channel.texture_normal
	match _chat_send_channel:
		0: _chat_channel.tooltip_text="世界聊天（Lv10）；點擊切換戰團"
		2: _chat_channel.tooltip_text="戰團聊天；點擊切換隊伍"
		3: _chat_channel.tooltip_text="隊伍聊天；點擊切換附近"
		4: _chat_channel.tooltip_text="附近聊天；點擊切換世界"
		_: _chat_channel.tooltip_text="聊天頻道狀態不可用"

func _set_chat_bubble_preview()->void:
	if _chat_bubble==null:return
	var normal:=_tex("ICON_BUBBLE_%d"%(_chat_bubble_type*2))
	var active:=_tex("ICON_BUBBLE_%d"%(_chat_bubble_type*2+1))
	_chat_bubble.texture_normal=normal
	_chat_bubble.texture_hover=active if active!=null else normal
	_chat_bubble.texture_pressed=active if active!=null else normal

func _build_chat_balloon_menu()->void:
	# Old CBalloonMenuItf::Create loads 019_TALKBUBBLE_BIG.itf. The HK
	# BalloonStyle.dat contains exactly six player-selectable entries (s01-s06),
	# so the original back/next controls are present but disabled for this build.
	_chat_balloon_menu.name="ClassicBalloonMenu"
	_chat_balloon_menu.size=Vector2(216,30)
	_chat_balloon_menu.visible=false
	hud.add_child(_chat_balloon_menu)
	_background(_chat_balloon_menu,"BG_TALKBUBBLE",_chat_balloon_menu.size)
	var back:=_texture_button(_chat_balloon_menu,"BalloonBack","B_BACK",Vector2(3,3),Vector2(14,24),Callable())
	back.disabled=true
	back.texture_disabled=_tex("B_BACK_0")
	var next:=_texture_button(_chat_balloon_menu,"BalloonNext","B_NEXT",Vector2(18,3),Vector2(14,24),Callable())
	next.disabled=true
	next.texture_disabled=_tex("B_NEXT_0")
	_chat_balloon_choices.clear()
	for index in 6:
		var choice:=TextureButton.new()
		choice.name="BalloonChoice%d"%index
		choice.position=Vector2(34+index*27,3)
		choice.size=Vector2(24,24)
		choice.ignore_texture_size=true
		choice.stretch_mode=TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		choice.focus_mode=Control.FOCUS_NONE
		choice.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
		choice.texture_normal=_tex("ICON_BUBBLE_%d"%(index*2))
		choice.texture_hover=_tex("ICON_BUBBLE_%d"%(index*2+1))
		choice.texture_pressed=choice.texture_hover
		choice.pressed.connect(_select_chat_bubble.bind(index))
		_chat_balloon_menu.add_child(choice)
		_chat_balloon_choices.append(choice)
	_texture_button(_chat_balloon_menu,"BalloonClose","CLOSESCREEN",Vector2(195,6),Vector2(16,16),_close_chat_balloon_menu)

func _open_chat_balloon_menu()->void:
	_close_chat_emotion_menu()
	# The ITF provides exact local geometry but not a screen-space spawn point.
	# Keep the popup attached to the invoking chat control and clamp it on-screen;
	# native HK global placement remains a visual-verification item.
	if _chat_balloon_menu.visible:return
	var anchor:=chat_panel.position+_chat_bubble.position+Vector2(12,-15)
	_chat_balloon_menu.position=Vector2(
		clampf(anchor.x-_chat_balloon_menu.size.x*0.5,0,BASE_SIZE.x-_chat_balloon_menu.size.x),
		clampf(anchor.y-_chat_balloon_menu.size.y,0,BASE_SIZE.y-_chat_balloon_menu.size.y))
	_chat_balloon_menu.show()
	_chat_balloon_menu.move_to_front()

func _close_chat_balloon_menu()->void:
	_chat_balloon_menu.hide()

func _select_chat_bubble(index:int)->void:
	if index<0 or index>=6:return
	_chat_bubble_type=index
	_set_chat_bubble_preview()
	_close_chat_balloon_menu()

# 018_EMOTION_BIG: exact 242x30 HK geometry. Paradise.exe pages seven
# slots at a time and converts the selected slot to a 1-based emotion id.
func _build_chat_emotion_menu()->void:
	_chat_emotion_menu.name="ClassicEmotionMenu"
	_chat_emotion_menu.size=Vector2(242,30)
	_chat_emotion_menu.visible=false
	hud.add_child(_chat_emotion_menu)
	_background(_chat_emotion_menu,"BG_EMOTION",_chat_emotion_menu.size)
	_chat_emotion_back=_texture_button(_chat_emotion_menu,"EmotionBack","B_BACK",Vector2(3,3),Vector2(14,24),_change_chat_emotion_page.bind(-1))
	_chat_emotion_next=_texture_button(_chat_emotion_menu,"EmotionNext","B_NEXT",Vector2(18,3),Vector2(14,24),_change_chat_emotion_page.bind(1))
	_chat_emotion_choices.clear()
	for slot in 7:
		var choice:=TextureButton.new()
		choice.name="EmotionChoice%d"%slot
		choice.position=Vector2(33+slot*27,3)
		choice.size=Vector2(24,24)
		choice.ignore_texture_size=true
		choice.stretch_mode=TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		choice.focus_mode=Control.FOCUS_NONE
		choice.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
		choice.pressed.connect(_select_chat_emotion.bind(slot))
		_chat_emotion_menu.add_child(choice)
		_chat_emotion_choices.append(choice)
	_texture_button(_chat_emotion_menu,"EmotionClose","CLOSESCREEN",Vector2(222,6),Vector2(16,16),_close_chat_emotion_menu)
	_refresh_chat_emotion_page()

func _refresh_chat_emotion_page()->void:
	_chat_emotion_page=clampi(_chat_emotion_page,0,1)
	for slot in _chat_emotion_choices.size():
		var emotion_id:=_chat_emotion_page*7+slot+1
		var choice:=_chat_emotion_choices[slot]
		var valid:=emotion_id<=12
		choice.visible=valid
		choice.disabled=not valid
		if valid:
			var icon:=_tex("EMOTIONAL_ICON_%d"%(emotion_id-1))
			choice.texture_normal=icon
			choice.texture_hover=icon
			choice.texture_pressed=icon
	if _chat_emotion_back!=null:
		_chat_emotion_back.disabled=_chat_emotion_page==0
		_chat_emotion_back.texture_disabled=_tex("B_BACK_0")
	if _chat_emotion_next!=null:
		_chat_emotion_next.disabled=_chat_emotion_page==1
		_chat_emotion_next.texture_disabled=_tex("B_NEXT_0")

func _change_chat_emotion_page(delta:int)->void:
	var next:=clampi(_chat_emotion_page+delta,0,1)
	if next==_chat_emotion_page:return
	_chat_emotion_page=next
	_refresh_chat_emotion_page()

func _open_chat_emotion_menu()->void:
	_close_chat_balloon_menu()
	if _chat_emotion_menu.visible:return
	var anchor:=chat_panel.position+_chat_emotion.position+Vector2(12,-15)
	_chat_emotion_menu.position=Vector2(
		clampf(anchor.x-_chat_emotion_menu.size.x*0.5,0,BASE_SIZE.x-_chat_emotion_menu.size.x),
		clampf(anchor.y-_chat_emotion_menu.size.y,0,BASE_SIZE.y-_chat_emotion_menu.size.y))
	_chat_emotion_menu.show()
	_chat_emotion_menu.move_to_front()

func _close_chat_emotion_menu()->void:
	_chat_emotion_menu.hide()

func _select_chat_emotion(slot:int)->void:
	if slot<0 or slot>=7 or _panel==null or _panel.session==null:return
	var emotion_id:=_chat_emotion_page*7+slot+1
	if emotion_id<1 or emotion_id>12:return
	_panel.session.send_emotion(emotion_id)

# 020_FRIENDLIST: independent movable 203x90 interface, not Rightbox
# (which is the context menu for player requests).
func _build_friend_panel()->void:
	friend_panel.name="ClassicFriendPanel"
	friend_panel.position=Vector2(597,510)
	friend_panel.size=Vector2(203,90)
	hud.add_child(friend_panel)
	_make_movable(friend_panel)
	_friend_background=_background(friend_panel,"BG_FRIENDLIST_SMALL",friend_panel.size)
	_texture_button(friend_panel,"FriendAdd","B_LIST_ADD",Vector2(5,7),Vector2(24,24),_restore_selected_friend,"還原好友")
	_friend_ignore=_texture_button(friend_panel,"FriendIgnore","B_LIST_TO_INGORE",Vector2(31,7),Vector2(24,24),_toggle_selected_friend_ignore,"移至黑名單")
	_friend_delete=_texture_button(friend_panel,"FriendDelete","B_LIST_DEL",Vector2(71,7),Vector2(24,24),_delete_selected_friend,"移除選取的好友")
	_friend_edit=_texture_button(friend_panel,"FriendEdit","B_LIST_EDIT",Vector2(97,7),Vector2(24,24),_open_friend_rename,"好友備註")
	_friend_list_button=_texture_button(friend_panel,"FriendList","B_LIST_FRIENDLIST",Vector2(128,7),Vector2(24,24),_show_friend_list,"好友清單")
	_friend_ignore_list_button=_texture_button(friend_panel,"IgnoreList","B_LIST_IGNORELIST",Vector2(154,7),Vector2(24,24),_show_ignore_list,"黑名單")
	_friend_expand=_texture_button(friend_panel,"FriendExpand","ZONESCREEN",Vector2(183,6),Vector2(17,17),_toggle_friend_size,"展開好友清單")
	_friend_scroll=ScrollContainer.new()
	_friend_scroll.name="FriendScroll"
	_friend_scroll.position=Vector2(9,36)
	_friend_scroll.size=Vector2(168,49)
	_friend_scroll.horizontal_scroll_mode=ScrollContainer.SCROLL_MODE_DISABLED
	_friend_scroll.vertical_scroll_mode=ScrollContainer.SCROLL_MODE_SHOW_NEVER
	_friend_scroll.add_theme_stylebox_override("panel",StyleBoxEmpty.new())
	friend_panel.add_child(_friend_scroll)
	_friend_rows.name="FriendRows"
	_friend_rows.custom_minimum_size.x=150
	_friend_rows.add_theme_constant_override("separation",2)
	_friend_scroll.add_child(_friend_rows)
	_friend_up=_texture_button(friend_panel,"FriendScrollUp","SCROLLUP_S",Vector2(180,36),Vector2(17,25),func():_friend_scroll.scroll_vertical-=17,"往上捲動")
	_friend_down=_texture_button(friend_panel,"FriendScrollDown","SCROLLDOWN_S",Vector2(180,61),Vector2(17,25),func():_friend_scroll.scroll_vertical+=17,"往下捲動")
	# 020_FRIENDlIST controls 2900/2901/2902 are one-frame B_GET_MAIL
	# sprites at x=163 and y=36/53/70. Keep their hitboxes source-exact.
	for slot in range(3):
		var mail:=TextureButton.new()
		mail.name="FriendMail%d"%slot
		mail.position=Vector2(163,36+17*slot)
		mail.size=Vector2(14,15)
		mail.ignore_texture_size=true
		mail.stretch_mode=TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		mail.focus_mode=Control.FOCUS_NONE
		mail.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND
		mail.texture_normal=_tex("B_GET_MAIL_0")
		mail.texture_hover=mail.texture_normal
		mail.texture_pressed=mail.texture_normal
		mail.tooltip_text="好友訊息"
		mail.pressed.connect(_open_friend_message_slot.bind(slot))
		friend_panel.add_child(mail)
		_friend_mail_buttons.append(mail)
	var scrollbar:=_friend_scroll.get_v_scroll_bar()
	scrollbar.value_changed.connect(func(_value):
		_update_friend_scroll_buttons()
		_update_friend_mail_slots())
	scrollbar.changed.connect(func():
		_update_friend_scroll_buttons()
		_update_friend_mail_slots())
	_update_friend_scroll_buttons.call_deferred()
	_update_friend_mail_slots.call_deferred()
	# Keep real add/remove requests available without covering the original
	# three contact rows with a fabricated inline edit box.
	_friend_editor.name="FriendNameEditor"
	_friend_editor.size=Vector2i(230,40)
	add_child(_friend_editor)
	_friend_name_input.name="FriendNameInput"
	_friend_name_input.position=Vector2(5,8)
	_friend_name_input.size=Vector2(165,24)
	_friend_name_input.placeholder_text="好友名稱"
	_friend_name_input.max_length=32
	_friend_name_input.text_changed.connect(func(_value):_update_friend_action())
	_friend_name_input.text_submitted.connect(func(_value):_friend_action_pressed())
	_friend_editor.add_child(_friend_name_input)
	var action:=Button.new()
	action.name="FriendAction"
	action.position=Vector2(174,8)
	action.size=Vector2(50,24)
	action.pressed.connect(_friend_action_pressed)
	_friend_editor.add_child(action)
	_friend_action=action
	_update_friend_action()

	# Old CFriendItf::OnRenamePlayer loads 046_Rename.itf, seeds the selected
	# contact name, caps the editor at 10 and sends only after confirm.
	_friend_rename.name="FriendRename"
	_friend_rename.position=Vector2(272,250)
	_friend_rename.size=Vector2(256,100)
	_friend_rename.visible=false
	_friend_rename.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_friend_rename)
	_background(_friend_rename,"BG_RANK_01",_friend_rename.size)
	_texture_button(
		_friend_rename,"FriendRenameClose","CLOSESCREEN",Vector2(235,4),Vector2(16,16),
		_close_friend_rename,"取消")
	_texture_button(
		_friend_rename,"FriendRenameHelp","B_ONLINEHELP",Vector2(217,4),Vector2(16,16),
		Callable(),"說明")
	var rename_prompt:=Label.new()
	rename_prompt.name="FriendRenamePrompt"
	rename_prompt.position=Vector2(10,29)
	rename_prompt.size=Vector2(96,12)
	rename_prompt.text="請輸入最新的名字"
	rename_prompt.mouse_filter=Control.MOUSE_FILTER_IGNORE
	rename_prompt.add_theme_font_override("font",_character_raster_font)
	rename_prompt.add_theme_font_size_override("font_size",12)
	_friend_rename.add_child(rename_prompt)
	_friend_rename_input.name="FriendRenameInput"
	_friend_rename_input.position=Vector2(14,53)
	_friend_rename_input.size=Vector2(236,12)
	_friend_rename_input.max_length=WireCodec.FRIEND_ALIAS_MAX_CHARS
	_friend_rename_input.display_font=_character_raster_font
	_friend_rename_input.display_font_size=12
	_friend_rename_input.baseline=10.0
	_friend_rename_input.text_changed.connect(func(_value):_update_friend_rename_action())
	_friend_rename_input.text_submitted.connect(func(_value):_submit_friend_rename())
	_friend_rename.add_child(_friend_rename_input)
	_friend_rename_confirm=_texture_button(
		_friend_rename,"FriendRenameConfirm","YELLOW_BUTTON",Vector2(163,73),Vector2(85,21),
		_submit_friend_rename,"確定")
	var rename_confirm_text:=Label.new()
	rename_confirm_text.name="FriendRenameConfirmText"
	rename_confirm_text.position=Vector2(194,77)
	rename_confirm_text.size=Vector2(24,12)
	rename_confirm_text.text="確定"
	rename_confirm_text.mouse_filter=Control.MOUSE_FILTER_IGNORE
	rename_confirm_text.add_theme_font_override("font",_character_raster_font)
	rename_confirm_text.add_theme_font_size_override("font_size",12)
	_friend_rename.add_child(rename_confirm_text)
	_update_friend_rename_action()

	# Original 044_QUESTION_BOX geometry used by CFriendItf::OnInviteFriendRequest.
	# The original client routes YES/NO through ONUIQuestion; keep the exact
	# source-backed background/button rects instead of a Godot-native dialog.
	_friend_question.name="FriendInviteQuestion"
	_friend_question.position=Vector2(166,290)
	_friend_question.size=Vector2(469,19)
	_friend_question.visible=false
	_friend_question.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_friend_question)
	_background(_friend_question,"BG_QUESTION_BOX",_friend_question.size)
	_friend_question_text.name="QuestionText"
	_friend_question_text.position=Vector2(9,3)
	_friend_question_text.size=Vector2(382,12)
	_friend_question_text.add_theme_font_override("font",_character_raster_font)
	_friend_question_text.add_theme_font_size_override("font_size",12)
	_friend_question_text.vertical_alignment=VERTICAL_ALIGNMENT_CENTER
	_friend_question_text.clip_text=true
	_friend_question.add_child(_friend_question_text)
	_texture_button(_friend_question,"QuestionYes","B_OK_BUTTON",Vector2(424,1),Vector2(16,16),_accept_friend_invite,"接受")
	_texture_button(_friend_question,"QuestionNo","CLOSESCREEN",Vector2(444,1),Vector2(16,16),_reject_friend_invite,"拒絕")
	_build_friend_message_panel()


func _build_friend_message_panel()->void:
	# 043_FRIEND_MSG local geometry comes from the HK ITF. Old Create accepts
	# runtime x/y, so the file itself has no authoritative global screen origin;
	# center it without claiming that center is original, and keep it movable.
	_friend_message_panel.name="FriendMessagePanel"
	_friend_message_panel.size=Vector2(256,175)
	_friend_message_panel.position=(BASE_SIZE-_friend_message_panel.size)*0.5
	_friend_message_panel.visible=false
	_friend_message_panel.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_friend_message_panel)
	_make_movable(_friend_message_panel)
	_background(_friend_message_panel,"BG_FRIENDMSG_BOX",_friend_message_panel.size)
	_texture_button(
		_friend_message_panel,"FriendMessageClose","CLOSESCREEN",
		Vector2(233,1),Vector2(16,16),_close_friend_message,"關閉")
	_texture_button(
		_friend_message_panel,"FriendMessageHelp","B_ONLINEHELP",
		Vector2(216,1),Vector2(16,16),Callable(),"線上說明")
	_friend_message_up=_texture_button(
		_friend_message_panel,"FriendMessageUp","SCROLLUP_BUTTON",
		Vector2(233,24),Vector2(17,36),func():_scroll_friend_message(-1),"上一則")
	_friend_message_down=_texture_button(
		_friend_message_panel,"FriendMessageDown","SCROLLDOWN_BUTTON",
		Vector2(233,111),Vector2(17,36),func():_scroll_friend_message(1),"下一則")
	_friend_message_title=_itf_text(
		_friend_message_panel,"FriendMessageTitle",Rect2(12,4,200,13),9)
	_friend_message_timestamp=_itf_text(
		_friend_message_panel,"FriendMessageTimestamp",Rect2(12,28,218,13),9)
	_friend_message_counter=_itf_text(
		_friend_message_panel,"FriendMessageCounter",Rect2(129,28,100,13),9)
	_friend_message_direction=_itf_text(
		_friend_message_panel,"FriendMessageDirection",Rect2(12,50,218,13),9)
	_friend_message_history=_itf_text(
		_friend_message_panel,"FriendMessageHistory",Rect2(12,65,218,70),9)
	# Control 2943 is a source message-area control, but its text semantics are
	# still unproven. Preserve the rect and leave it blank instead of inventing.
	_friend_message_input_aux=_itf_text(
		_friend_message_panel,"FriendMessageInputAux",Rect2(227,155,18,15),9)
	_friend_message_input.name="FriendMessageInput"
	_friend_message_input.position=Vector2(9,155)
	_friend_message_input.size=Vector2(215,15)
	_friend_message_input.max_length=WireCodec.FRIEND_MESSAGE_MAX_MESSAGE_BYTES
	_friend_message_input.display_font=_character_raster_font
	_friend_message_input.display_font_size=12
	_friend_message_input.baseline=11.0
	_friend_message_input.text_changed.connect(_limit_friend_message_input)
	_friend_message_input.text_submitted.connect(func(_value):_send_friend_message())
	_friend_message_panel.add_child(_friend_message_input)


func _update_friend_scroll_buttons()->void:
	if _friend_up==null or _friend_down==null:return
	var scrollbar:=_friend_scroll.get_v_scroll_bar()
	_friend_up.disabled=scrollbar.value<=scrollbar.min_value
	_friend_down.disabled=scrollbar.value>=maxf(scrollbar.min_value,scrollbar.max_value-scrollbar.page)-0.01


func _update_friend_mail_slots()->void:
	if _friend_mail_buttons.is_empty():
		return
	var small_friend_list:=not _friend_large and not _friend_show_ignored
	var start:=0
	if _friend_scroll!=null:
		start=maxi(0,int(round(_friend_scroll.scroll_vertical/17.0)))
	for slot in range(_friend_mail_buttons.size()):
		var button:=_friend_mail_buttons[slot]
		var index:=start+slot
		var actor:=0
		if small_friend_list and index<_friend_ordered_actors.size():
			actor=int(_friend_ordered_actors[index])
		_friend_visible_message_actors[slot]=actor
		button.visible=actor>0
		button.disabled=actor<=0
		if actor>0:
			var entry:Dictionary=_stats.get("friends",{}).get(actor,{})
			button.tooltip_text="好友訊息｜%s"%str(entry.get("name",""))


func _open_friend_message_slot(slot:int)->void:
	if slot<0 or slot>=_friend_visible_message_actors.size():
		return
	var actor:=int(_friend_visible_message_actors[slot])
	if actor>0:
		_open_friend_message(actor)


func _open_friend_message(actor_id:int)->void:
	var friends:Dictionary=_stats.get("friends",{})
	var ignored:Dictionary=_stats.get("ignored_friends",{})
	if actor_id<=0 or not friends.has(actor_id) or ignored.has(actor_id):
		return
	_friend_message_actor=actor_id
	_friend_message_auto_latest=true
	_friend_message_index=-1
	_friend_message_last_count=0
	_refresh_friend_message()
	_friend_message_panel.show()
	_friend_message_panel.move_to_front()
	_friend_message_input.focus_editor()


func _close_friend_message()->void:
	_friend_message_panel.hide()
	_friend_message_actor=0
	_friend_message_index=-1
	_friend_message_auto_latest=true
	_friend_message_last_count=0
	_friend_message_input.text=""


func _friend_messages_for_actor(actor_id:int)->Array[Dictionary]:
	var rows:Array[Dictionary]=[]
	var all:Array=_stats.get("friend_messages",[])
	for raw in all:
		var row:Dictionary=raw
		if int(row.get("peer_actor_id",0))==actor_id:
			rows.append(row)
	return rows


func _scroll_friend_message(delta:int)->void:
	var rows:=_friend_messages_for_actor(_friend_message_actor)
	if rows.is_empty():
		return
	_friend_message_index=clampi(_friend_message_index+delta,0,rows.size()-1)
	_friend_message_auto_latest=_friend_message_index==rows.size()-1
	_friend_message_last_count=rows.size()
	_refresh_friend_message()


func _refresh_friend_message()->void:
	if _friend_message_title==null:
		return
	var friends:Dictionary=_stats.get("friends",{})
	var ignored:Dictionary=_stats.get("ignored_friends",{})
	if _friend_message_actor<=0:
		return
	if not friends.has(_friend_message_actor) or ignored.has(_friend_message_actor):
		_close_friend_message()
		return
	var entry:Dictionary=friends[_friend_message_actor]
	var peer_name:=str(entry.get("name",""))
	var online_text:="在線" if bool(entry.get("online",false)) else "離線"
	# Old UpdateInfo formats: "傳送訊息到．．．" + contact + " [在線/離線]".
	_friend_message_title.text="傳送訊息到．．．%s [%s]"%[peer_name,online_text]

	var rows:=_friend_messages_for_actor(_friend_message_actor)
	if rows.size()!=_friend_message_last_count and _friend_message_auto_latest:
		_friend_message_index=rows.size()-1
	_friend_message_last_count=rows.size()
	if rows.is_empty():
		_friend_message_index=-1
		_friend_message_timestamp.text=""
		_friend_message_counter.text=""
		_friend_message_direction.text=""
		_friend_message_history.text=""
		_friend_message_up.disabled=true
		_friend_message_down.disabled=true
		return
	_friend_message_index=clampi(_friend_message_index,0,rows.size()-1)
	var row:Dictionary=rows[_friend_message_index]
	_friend_message_timestamp.text=str(row.get("local_timestamp",""))
	_friend_message_counter.text="%d/%d"%[_friend_message_index+1,rows.size()]
	if str(row.get("direction",""))=="outgoing":
		var own_name:=str(_stats.get("character_name",""))
		_friend_message_direction.text=own_name if not own_name.is_empty() else "->"
	else:
		_friend_message_direction.text=peer_name
	_friend_message_history.text=str(row.get("message",""))
	_friend_message_up.disabled=_friend_message_index<=0
	_friend_message_down.disabled=_friend_message_index>=rows.size()-1


func _limit_friend_message_input(value:String)->void:
	if value.to_utf8_buffer().size()<=WireCodec.FRIEND_MESSAGE_MAX_MESSAGE_BYTES:
		return
	var clipped:=value
	while (
		not clipped.is_empty() and
		clipped.to_utf8_buffer().size()>WireCodec.FRIEND_MESSAGE_MAX_MESSAGE_BYTES
	):
		clipped=clipped.left(clipped.length()-1)
	var caret:=mini(_friend_message_input.caret_column,clipped.length())
	_friend_message_input.text=clipped
	_friend_message_input.caret_column=caret


func _send_friend_message()->void:
	if _panel==null or _panel.session==null or _friend_message_actor<=0:
		return
	var message:=_friend_message_input.text
	if message.is_empty():
		return
	if _panel.session.send_friend_message(_friend_message_actor,message):
		_friend_message_input.text=""
		_friend_message_auto_latest=true


func _set_friend_button_art(button:TextureButton,art:String)->void:
	button.texture_normal=_tex(art+"_0")
	button.texture_hover=_tex(art+"_1")
	button.texture_pressed=_tex(art+"_2")
	button.texture_disabled=_tex(art+"_3")

func _toggle_friend_size()->void:
	_friend_large=not _friend_large
	if _friend_large:
		_friend_small_position=friend_panel.position
		# Old CFriendSItf::OnSwitchPanel creates CFriendLItf with x=0,y=0.
		friend_panel.position=Vector2.ZERO
		friend_panel.size=Vector2(173,600)
		_friend_background.texture=_tex("BG_FRIENDLIST_BIG")
		_friend_background.size=friend_panel.size
		(friend_panel.get_node("FriendAdd") as TextureButton).position=Vector2(2,6)
		_friend_ignore.position=Vector2(27,6)
		_friend_delete.position=Vector2(52,6)
		_friend_edit.position=Vector2(77,6)
		_friend_list_button.position=Vector2(104,6)
		_friend_ignore_list_button.position=Vector2(129,6)
		_friend_expand.position=Vector2(155,5)
		_set_friend_button_art(_friend_expand,"MINISCREEN")
		_friend_expand.tooltip_text="縮小好友清單"
		_friend_scroll.position=Vector2(9,36)
		_friend_scroll.size=Vector2(137,560)
		_friend_rows.custom_minimum_size.x=119
		_friend_up.position=Vector2(150,36)
		_friend_up.size=Vector2(17,36)
		_set_friend_button_art(_friend_up,"SCROLLUP_BUTTON")
		_friend_down.position=Vector2(150,560)
		_friend_down.size=Vector2(17,36)
		_set_friend_button_art(_friend_down,"SCROLLDOWN_BUTTON")
	else:
		friend_panel.position=_friend_small_position
		friend_panel.size=Vector2(203,90)
		_friend_background.texture=_tex("BG_FRIENDLIST_SMALL")
		_friend_background.size=friend_panel.size
		(friend_panel.get_node("FriendAdd") as TextureButton).position=Vector2(5,7)
		_friend_ignore.position=Vector2(31,7)
		_friend_delete.position=Vector2(71,7)
		_friend_edit.position=Vector2(97,7)
		_friend_list_button.position=Vector2(128,7)
		_friend_ignore_list_button.position=Vector2(154,7)
		_friend_expand.position=Vector2(183,6)
		_set_friend_button_art(_friend_expand,"ZONESCREEN")
		_friend_expand.tooltip_text="展開好友清單"
		_friend_scroll.position=Vector2(9,36)
		_friend_scroll.size=Vector2(168,49)
		_friend_rows.custom_minimum_size.x=150
		_friend_up.position=Vector2(180,36)
		_friend_up.size=Vector2(17,25)
		_set_friend_button_art(_friend_up,"SCROLLUP_S")
		_friend_down.position=Vector2(180,61)
		_friend_down.size=Vector2(17,25)
		_set_friend_button_art(_friend_down,"SCROLLDOWN_S")
	_refresh_friends()
	# ScrollContainer clamps assignments against the old child minimum. Re-apply
	# the source viewport after rebuilding rows at the new 119/150px width.
	_friend_scroll.size=Vector2(137,560) if _friend_large else Vector2(168,49)
	_update_friend_scroll_buttons.call_deferred()
	_update_friend_mail_slots.call_deferred()

func _open_friend_editor()->void:
	_friend_name_input.clear()
	_update_friend_action()
	_friend_editor.popup_centered(Vector2i(230,40))
	_friend_name_input.grab_focus()

func _refresh_friend_server()->void:
	if _panel!=null and _panel.session!=null:
		_panel.session.friend_refresh()

func _open_friend_rename()->void:
	if _friend_selected_actor<=0:
		return
	var entries:Dictionary=_stats.get("ignored_friends",{}) if _friend_show_ignored else _stats.get("friends",{})
	if not entries.has(_friend_selected_actor):
		return
	var entry:Dictionary=entries[_friend_selected_actor]
	_friend_rename_target_actor=_friend_selected_actor
	_friend_rename_input.text=str(entry.get("name",""))
	_friend_rename_input.caret_column=_friend_rename_input.text.length()
	_update_friend_rename_action()
	_friend_rename.show()
	_friend_rename.move_to_front()
	_friend_rename_input.focus_editor()

func _close_friend_rename()->void:
	_friend_rename.hide()
	_friend_rename_target_actor=0

func _update_friend_rename_action()->void:
	if _friend_rename_confirm==null:
		return
	var clean:=_friend_rename_input.text.strip_edges()
	var pending:=int(_stats.get("pending_friend_sequence",0))!=0
	_friend_rename_confirm.disabled=(
		_friend_rename_target_actor<=0 or clean.is_empty() or
		clean!=_friend_rename_input.text or clean.length()>WireCodec.FRIEND_ALIAS_MAX_CHARS or
		clean.to_utf8_buffer().size()>WireCodec.PLAYER_IDENTITY_MAX_NAME_BYTES or pending)

func _submit_friend_rename()->void:
	if _panel==null or _panel.session==null or _friend_rename_target_actor<=0:
		return
	var clean:=_friend_rename_input.text.strip_edges()
	if clean!=_friend_rename_input.text or clean.is_empty():
		return
	if _panel.session.friend_rename(_friend_rename_target_actor,clean):
		_close_friend_rename()
		_update_friend_action()

func _show_friend_list()->void:
	_friend_show_ignored=false
	_friend_selected_actor=0
	_refresh_friend_server()
	_refresh_friends()

func _show_ignore_list()->void:
	_friend_show_ignored=true
	_friend_selected_actor=0
	_refresh_friend_server()
	_refresh_friends()

func _restore_selected_friend()->void:
	# Old CFriendItf::OnAddPlayer (control 0x8DB / B_LIST_ADD) only acts on
	# the selected ignored contact. In friend-list mode the original disables
	# this control; friend invitations come from 040_Rightbox instead.
	if not _friend_show_ignored or _panel==null or _panel.session==null or _friend_selected_actor<=0:
		return
	if _panel.session.friend_restore(_friend_selected_actor):
		_friend_selected_actor=0
		_update_friend_action()

func _toggle_selected_friend_ignore()->void:
	# Old control 0xAFC / B_LIST_TO_INGORE is the opposite one-way action:
	# friend list -> ignore list. It is disabled while viewing ignored contacts.
	if _friend_show_ignored or _panel==null or _panel.session==null or _friend_selected_actor<=0:
		return
	if _panel.session.friend_ignore(_friend_selected_actor):
		_friend_selected_actor=0
		_update_friend_action()

func _delete_selected_friend()->void:
	if _friend_show_ignored or _friend_selected_actor<=0 or _panel==null or _panel.session==null:
		return
	if _panel.session.friend_remove(_friend_selected_actor):
		_friend_selected_actor=0
		_update_friend_action()

func _style_friend_row(button:Button)->void:
	button.custom_minimum_size=Vector2(119 if _friend_large else 150,15)
	button.clip_text=true
	button.add_theme_font_override("font",_character_raster_font)
	button.add_theme_font_size_override("font_size",12)
	button.add_theme_stylebox_override("normal",StyleBoxEmpty.new())
	button.add_theme_stylebox_override("hover",StyleBoxEmpty.new())
	button.add_theme_stylebox_override("pressed",StyleBoxEmpty.new())
	button.add_theme_stylebox_override("focus",StyleBoxEmpty.new())

# Original 008_MINIMAP_L/S ITFs switch between complete 133x122 and
# 133x20 backgrounds; never shrink the map artwork into a 20px row.
# Map pixel plotting/scale remains source-gated, not invented here.
func _toggle_minimap_size()->void:
	_minimap_compact=not _minimap_compact
	minimap_panel.size=Vector2(133,20 if _minimap_compact else 122)
	var art:=minimap_panel.get_node("MiniMap") as TextureRect
	art.texture=_tex("MINIMAP_S" if _minimap_compact else "MINIMAP_L")
	art.size=minimap_panel.size
	var map_points:=minimap_panel.get_node("MinimapPointsArt") as TextureRect
	map_points.visible=not _minimap_compact
	var toggle:=minimap_panel.get_node("MinimapSizeToggle") as TextureButton
	var toggle_art:="ZONESCREEN" if _minimap_compact else "MINISCREEN"
	toggle.texture_normal=_tex(toggle_art+"_0")
	toggle.texture_hover=_tex(toggle_art+"_1")
	toggle.texture_pressed=_tex(toggle_art+"_2")
	toggle.texture_disabled=_tex(toggle_art+"_3")
	toggle.tooltip_text="放大地圖" if _minimap_compact else "縮小地圖"

# OFFON.spr contains only two original 11x11 frames: off (0) and on (1).
# Keep the page selector responsive without inventing skill/item assignments
# or submitting Server actions before GP-028 has an authoritative binding.
func _select_hotkey_page(index:int)->void:
	if index<0 or index>1:return
	_hotkey_page=index
	for page in 2:
		var button:=hotkey_panel.get_node("HotkeyPage%d"%(page+1)) as TextureButton
		button.texture_normal=_tex("OFFON_1" if page==index else "OFFON_0")
		button.texture_hover=_tex("OFFON_1")
		button.texture_pressed=_tex("OFFON_1")
		button.texture_disabled=_tex("OFFON_0")
	_refresh_hotkeys()

# The two HK ITFs are distinct complete layouts, not a scaled copy of the
# long rail. Switch the original background, control rects and collapse icon
# together; the six hidden keys cannot receive input in compact mode.
func _toggle_hotkey_size()->void:
	var compact:=hotkey_panel.size.y>196.0
	hotkey_panel.size=Vector2(30,196 if compact else 358)
	var art:=hotkey_panel.get_node("HotkeyArt") as TextureRect
	art.texture=_tex("HOTKEY_SY" if compact else "HOTKEY_LY")
	art.size=hotkey_panel.size
	var toggle:=hotkey_panel.get_node("HotkeySizeToggle") as TextureButton
	toggle.position=Vector2(3,179 if compact else 342)
	var toggle_art:="HOTKEY_YL_B" if compact else "HOTKEY_YS_B"
	toggle.texture_normal=_tex(toggle_art+"_0")
	toggle.texture_hover=_tex(toggle_art+"_1")
	toggle.texture_pressed=_tex(toggle_art+"_1")
	toggle.texture_disabled=_tex(toggle_art+"_0")
	var long_ys:=[18,47,72,99,126,153,180,207,234,261,288,315]
	for i in 12:
		var label:=hotkey_panel.get_node("Hotkey%d"%i) as Control
		label.visible=not compact or i<6
		if compact and i<6:
			label.position=Vector2(10,17+27*i)
			label.size=Vector2(12,12)
		elif not compact:
			label.position=Vector2(4,long_ys[i])
			label.size=Vector2(18 if i>=9 else 12,12)
	_refresh_hotkeys()

func _on_hotkey_slot_activated(slot_index:int)->void:
	if _active:
		_activate_hotkey(slot_index)

func _on_hotkey_binding_dropped(slot_index:int,payload:Dictionary)->void:
	if _panel==null or slot_index<0 or slot_index>=12:
		return
	var kind:=str(payload.get("hotkey_kind",""))
	var data_id:=int(payload.get("data_id",0))
	if kind not in ["item","skill"] or data_id<=0:
		return
	var data_type:=0 if kind=="item" else 1
	var source_slot:=int(payload.get("source_slot",-1))
	if source_slot>=0:
		if source_slot==slot_index or source_slot>=12 or int(_stats.get("pending_hotkey_sequence",0))!=0:
			return
		var bindings:Dictionary=_stats.get("hotkeys",{})
		if bindings.has(slot_index):
			# Scheduler v20 requires occupied-slot exchange in addition to the Old
			# replacement flow. Keep it Server-authoritative and atomic so neither
			# binding can disappear between multiple Client commands.
			if _panel.submit_hotkey_swap(source_slot,slot_index):
				_hotkey_refresh_requested=true
			return
		# Original CHotKeyItf::ReceiveDropButton sends source removal only after a
		# valid hotkey destination accepts the drop. Keep a cancelled drag durable,
		# then sequence destination Save behind the authoritative Remove snapshot.
		_pending_hotkey_rebind={
			"slot_index":slot_index,"data_type":data_type,"data_id":data_id,
			"after_sequence":int(_stats.get("hotkey_sequence",0))
		}
		if _panel.submit_hotkey_remove(source_slot):
			_hotkey_refresh_requested=true
		else:
			_pending_hotkey_rebind.clear()
		return
	if int(_stats.get("pending_hotkey_sequence",0))!=0:
		return
	if _panel.submit_hotkey_bind(slot_index,data_type,data_id):
		_hotkey_refresh_requested=true

func _try_submit_pending_hotkey_rebind()->void:
	if (
		_pending_hotkey_rebind.is_empty() or _panel==null or
		int(_stats.get("pending_hotkey_sequence",0))!=0
	):
		return
	# Ignore a stale pre-remove presentation snapshot (including an old failure
	# status). Only the next authoritative hotkey sequence can resolve this move.
	if int(_stats.get("hotkey_sequence",0))<=int(_pending_hotkey_rebind.get("after_sequence",-1)):
		return
	# This queue exists only after a source-slot remove. Never create a duplicate
	# binding when that authoritative remove was rejected.
	if int(_stats.get("last_hotkey_status",-1))>0:
		_pending_hotkey_rebind.clear()
		return
	var queued:=_pending_hotkey_rebind.duplicate(true)
	if _panel.submit_hotkey_bind(
		int(queued.get("slot_index",-1)),int(queued.get("data_type",-1)),
		int(queued.get("data_id",0))
	):
		_pending_hotkey_rebind.clear()
		_hotkey_refresh_requested=true

func _refresh_hotkeys()->void:
	if _hotkey_slots.is_empty():
		return
	var compact:=hotkey_panel.size.y<300.0
	var bindings:Dictionary=_stats.get("hotkeys",{})
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var long_ys:=[18,45,72,99,126,153,180,207,234,261,288,315]
	for i in 12:
		var slot=_hotkey_slots[i]
		var visible_slot:=not compact or i<6
		slot.visible=visible_slot
		if not visible_slot:
			continue
		var actual_slot:=i+_hotkey_page*6 if compact else i
		slot.slot_index=actual_slot
		slot.position=Vector2(4,17+27*i) if compact else Vector2(4,long_ys[i])
		slot.size=Vector2(24,26)
		var icon:=slot.get_node("Icon") as TextureRect
		icon.texture=null
		slot.tooltip_text="F%d"%(i+1)
		slot.hotkey_drag_payload={}
		slot.drag_enabled=int(_stats.get("pending_hotkey_sequence",0))==0
		if not bindings.has(actual_slot):
			continue
		var binding:Dictionary=bindings[actual_slot]
		var data_type:=int(binding.get("data_type",-1))
		var data_id:=int(binding.get("data_id",0))
		if data_id>0 and data_type in [0,1]:
			slot.hotkey_drag_payload={
				"hotkey_kind":"item" if data_type==0 else "skill",
				"data_id":data_id
			}
		if data_type==0 and inventory_items.has(data_id) and _panel!=null:
			var item:Dictionary=inventory_items[data_id]
			var definition:=int(item.get("item_definition_id",0))
			icon.texture=_photos.photo_for_item(definition,_panel.item_catalog)
			slot.tooltip_text="F%d｜%s"%[i+1,str(_panel.item_catalog.name_for(definition))]
		elif data_type==1:
			if skill_window!=null and skill_window.has_method("icon_for_skill"):
				var skill_icon:Variant=skill_window.call("icon_for_skill",data_id)
				if skill_icon is Texture2D:
					icon.texture=skill_icon
			var skill_name:="技能 %d"%data_id
			if skill_window!=null and skill_window.has_method("display_name_for_skill"):
				skill_name=str(skill_window.call("display_name_for_skill",data_id))
			slot.tooltip_text="F%d｜%s"%[i+1,skill_name]

func _handle_hotkey_key(event:InputEvent)->void:
	if not _active or not (event is InputEventKey) or not event.pressed or event.echo:
		return
	var keys:=[KEY_F1,KEY_F2,KEY_F3,KEY_F4,KEY_F5,KEY_F6,KEY_F7,KEY_F8,KEY_F9,KEY_F10,KEY_F11,KEY_F12]
	var index:=keys.find(event.keycode)
	if index<0:
		index=keys.find(event.physical_keycode)
	if index<0:
		return
	var compact:=hotkey_panel.size.y<300.0
	if compact and index>=6:
		return
	var actual_slot:=index+_hotkey_page*6 if compact else index
	if _activate_hotkey(actual_slot):
		get_viewport().set_input_as_handled()

func _activate_hotkey(slot_index:int)->bool:
	if _panel==null:
		return false
	var bindings:Dictionary=_stats.get("hotkeys",{})
	if not bindings.has(slot_index):
		return false
	var binding:Dictionary=bindings[slot_index]
	var data_type:=int(binding.get("data_type",-1))
	var data_id:=int(binding.get("data_id",0))
	if data_type==0 and data_id>0:
		var used:bool=_panel.submit_recovery_use(data_id)
		if used:
			_panel.cancel_pre_use_skill()
		return used
	if data_type==1 and data_id>0:
		var learned:Dictionary=_stats.get("skill_book",{})
		if int(learned.get(data_id,learned.get(str(data_id),0)))<=0:
			return false
		# Old CHotKeyItf::OnUseSkill direct-call evidence includes
		# SetPreUseSkill/SetControlMode. Keep Classic F-key staging separate
		# from the diagnostic persistent skill-mode toggle. Exact original
		# cancellation timing remains native-reference NOT VERIFIED.
		# Target kind is not guessed: the next monster click uses the existing
		# authenticated SkillUse path and Server-owned rule checks.
		return _panel.arm_pre_use_skill(data_id)
	return false

func _make_item_hotkey_draggable(row:Button,item_uid:int,item_definition_id:int)->void:
	if _panel==null or item_uid<=0:
		return
	if str(_panel.item_catalog.category_for(item_definition_id))!="recovery":
		return
	row.set_script(HotkeyDragButton)
	row.set("hotkey_drag_payload",{"hotkey_kind":"item","data_id":item_uid})
	row.tooltip_text+="\n拖曳到快捷列可綁定 F 鍵"

func _build_inventory()->void:
	inventory=_window("InventoryWindow","BG_ITEM",Vector2(259,506),Vector2(531,71))
	_inventory_item_bg=inventory.get_node_or_null("BG_ITEMBackground") as TextureRect
	_inventory_equip_bg=_deferred_background(inventory,"BG_Equip",inventory.size)
	_inventory_equip_bg.hide()
	# 023_ITEM.itf: the five category controls are fixed 35×37 hitboxes at
	# y=21/58/95/132/169. Keep the original visual order while binding to the
	# existing SGTest logical categories (mecha/armor is logical tab 2).
	var tab_specs=[
		["B_ITEM_ITEM","道具",0,21],
		["B_ITEM_EQUIP","裝備",1,58],
		["B_ITEM_PET","寵物",3,95],
		["B_ITEM_RIDE","騎乘",4,132],
		["B_ITEM_ARMOR","機甲／裝甲",2,169],
	]
	for i in tab_specs.size():
		var spec:Array=tab_specs[i]
		var button:=_texture_button(
			inventory,"InventoryTab%d"%i,str(spec[0]),Vector2(0,float(spec[3])),
			Vector2(35,37),_change_inventory_tab.bind(int(spec[2])),str(spec[1]))
		_inventory_tab_buttons.append(button)
	_texture_button(
		inventory,"InventoryScrollUp","SCROLLUP_BUTTON",Vector2(204,60),
		Vector2(17,36),_scroll_inventory.bind(-1),"向上捲動")
	_texture_button(
		inventory,"InventoryScrollDown","SCROLLDOWN_BUTTON",Vector2(204,459),
		Vector2(17,36),_scroll_inventory.bind(1),"向下捲動")
	inventory.set_meta("item_scroll",0)
	# 023_ITEM.itf exposes 16 row hit areas at y=62..467, exactly 27 px apart.
	_inventory_rows.name="InventoryRows"
	_inventory_rows.position=Vector2(42,62)
	_inventory_rows.size=Vector2(152,432)
	_inventory_rows.add_theme_constant_override("separation",0)
	inventory.add_child(_inventory_rows)
	# 022_equip.itf: nine fixed equipment hit rows occupy the upper-left
	# paper-doll area. Keep them absolute; a VBox would destroy source rects.
	_equipment_rows.name="EquippedRows"
	_equipment_rows.position=Vector2.ZERO
	_equipment_rows.size=inventory.size
	_equipment_rows.hide()
	inventory.add_child(_equipment_rows)
	inventory.set_meta("equip_scroll",0)
	var slot_specs=[
		[1,"左手",Rect2(44,90,90,24)],
		[2,"右手",Rect2(44,117,90,24)],
		[3,"身體",Rect2(44,144,90,24)],
		[4,"腳部",Rect2(44,171,90,24)],
		[5,"頭部",Rect2(44,63,90,24)],
		[6,"武器",Rect2(44,198,90,24)],
		[7,"盾牌",Rect2(43,224,90,24)],
		[8,"背部",Rect2(43,252,90,24)],
		[9,"護符",Rect2(43,278,90,26)],
	]
	for raw_spec in slot_specs:
		var spec:Array=raw_spec
		var slot_id:=int(spec[0])
		var slot_name:=str(spec[1])
		var rect:Rect2=spec[2]
		var button:=TextureButton.new()
		button.name="EquipmentSlot%d"%slot_id
		button.position=rect.position
		button.size=rect.size
		button.focus_mode=Control.FOCUS_NONE
		button.tooltip_text="%s：未裝備"%slot_name
		button.set_meta("slot_id",slot_id)
		button.set_meta("slot_name",slot_name)
		button.set_meta("item_uid",0)
		button.pressed.connect(_on_equipment_slot_pressed.bind(slot_id))
		var name_label:=Label.new()
		name_label.name="SlotName"
		name_label.position=Vector2.ZERO
		name_label.size=rect.size
		name_label.text=slot_name
		name_label.horizontal_alignment=HORIZONTAL_ALIGNMENT_RIGHT
		name_label.vertical_alignment=VERTICAL_ALIGNMENT_CENTER
		name_label.add_theme_font_size_override("font_size",HK_SHARED_FONT_HEIGHT)
		name_label.add_theme_color_override("font_color",Color("#f9f2d6"))
		name_label.mouse_filter=Control.MOUSE_FILTER_IGNORE
		button.add_child(name_label)
		var icon:=TextureRect.new()
		icon.name="Icon"
		icon.position=Vector2(-1,0)
		icon.size=Vector2(24,24)
		icon.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		icon.mouse_filter=Control.MOUSE_FILTER_IGNORE
		button.add_child(icon)
		# 022_equip uses B_RADIO_B.spr (19x19) beside every equipment row.
		# Keep the source art as the committed-equipment indicator instead of
		# drawing a font glyph, so the 90x24 hit rect is never changed by theme metrics.
		var marker:=TextureRect.new()
		marker.name="EquippedMarker"
		marker.position=Vector2(27,2)
		marker.size=Vector2(19,19)
		marker.texture=_tex("B_RADIO_B_0")
		marker.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
		marker.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		marker.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
		marker.mouse_filter=Control.MOUSE_FILTER_IGNORE
		marker.hide()
		button.add_child(marker)
		_equipment_slot_buttons[slot_id]=button
		_equipment_slot_icons[slot_id]=icon
		_equipment_rows.add_child(button)
	# CEquipItf::Draw/UpdateFigure1 renders a standing character beside the
	# source slot rows. This preview shares WorldView's AssetStore; it creates
	# no second loader/session/reducer.
	_equipment_preview.name="EquipmentPaperDoll"
	_equipment_preview.position=Vector2(136,55)
	_equipment_preview.size=Vector2(100,245)
	_equipment_preview.set_occupied(false)
	_equipment_rows.add_child(_equipment_preview)

func _build_mecha()->void:
	# 056_Driver_INFO.itf is the authoritative mounted-Mecha detail surface.
	# Keep source-local geometry; gameplay state below always comes from the
	# authenticated MechaSnapshot/MechaEquipmentSnapshot reducer.
	_mecha_window=_window("MechaDriverWindow","BG_DRIVER",Vector2(523,393),Vector2(138,101))
	var close:=_mecha_window.get_node("CloseWindow") as TextureButton
	close.position=Vector2(500,1)
	close.size=Vector2(16,16)
	for pair in [
		["名　稱",Rect2(9,37,36,12)],["類　形",Rect2(9,53,36,12)],
		["能　源",Rect2(9,69,36,12)],["等　級",Rect2(10,94,36,12)],
		["機甲裝備",Rect2(333,30,72,12)],["機甲能源",Rect2(333,310,72,12)]
	]:
		var caption:=_itf_text(_mecha_window,"Caption"+str(pair[0]),pair[1],9,false)
		caption.text=str(pair[0])
	for spec in [
		["name",Rect2(50,35,136,15)],["type",Rect2(50,51,136,15)],
		["energy",Rect2(50,67,136,15)],["level",Rect2(50,92,70,15)],
		["experience",Rect2(50,108,136,15)]
	]:
		var key:=str(spec[0])
		_mecha_info_labels[key]=_itf_text(_mecha_window,"Mecha"+key.capitalize(),spec[1],9,false)
	_mecha_drive_button=_texture_button(
		_mecha_window,"DriveMecha","B_DRIVER01",Vector2(1,330),Vector2(113,23),
		_submit_selected_mecha_mount,"駕駛")
	_mecha_exit_button=_texture_button(
		_mecha_window,"ExitMecha","B_DRIVER02",Vector2(1,360),Vector2(113,23),
		_submit_selected_mecha_dismount,"機甲脫出")
	# The original controls expose these fixed captions even when button art is
	# unavailable during a source-sync failure.
	var drive_text:=_itf_text(_mecha_window,"DriveText",Rect2(9,336,48,12),9,false)
	drive_text.text="駕駛"
	var exit_text:=_itf_text(_mecha_window,"ExitText",Rect2(8,366,72,12),9,false)
	exit_text.text="機甲脫出"
	var slot_specs=[
		[0,"主體",Rect2(200,312,56,22)],
		[1,"武裝",Rect2(199,339,56,22)],
		[2,"引擎",Rect2(199,366,56,22)],
		[3,"強化卡 1",Rect2(262,312,64,22)],
		[4,"強化卡 2",Rect2(262,340,64,22)],
		[5,"強化卡 3",Rect2(262,366,64,22)],
		[6,"守護石",Rect2(280,72,64,22)],
	]
	for raw_spec in slot_specs:
		var spec:Array=raw_spec
		var slot_id:=int(spec[0]);var label:=str(spec[1]);var rect:Rect2=spec[2]
		var button:=Button.new()
		button.name="MechaSlot%d"%slot_id
		button.position=rect.position;button.size=rect.size
		button.flat=true;button.focus_mode=Control.FOCUS_NONE
		button.mouse_default_cursor_shape=Control.CURSOR_POINTING_HAND if slot_id>0 else Control.CURSOR_ARROW
		button.set_meta("slot_id",slot_id);button.set_meta("item_uid",0)
		button.text=label
		button.add_theme_font_size_override("font_size",8)
		if _character_raster_font!=null:button.add_theme_font_override("font",_character_raster_font)
		if slot_id>0:button.pressed.connect(_on_mecha_slot_pressed.bind(slot_id))
		_mecha_window.add_child(button)
		_mecha_slot_buttons[slot_id]=button
	_mecha_battery_rows.name="MechaBatteryRows"
	_mecha_battery_rows.position=Vector2(333,327)
	_mecha_battery_rows.size=Vector2(176,58)
	_mecha_battery_rows.add_theme_constant_override("separation",1)
	_mecha_window.add_child(_mecha_battery_rows)

func _build_character()->void:
	character=_window("CharacterWindow","BG_CHARACTER_INFO",Vector2(263,397),Vector2(269,95))
	# 013_Character_INFO.itf coordinates are local to the 263×397 sprite.
	# The type 1 captions and type 0/3 value cells are different controls.
	var close:=character.get_node("CloseWindow") as TextureButton
	close.position=Vector2(244,1)
	close.size=Vector2(16,16)
	_character_hp_fill=_character_vital_fill("HP",Vector2(30,268))
	_character_mp_fill=_character_vital_fill("MP",Vector2(30,285))
	for index in 4:
		var plus:=TextureButton.new()
		plus.name="AddPoint%d"%(index+1)
		plus.texture_normal=_tex("ADD_POINT_0")
		plus.texture_hover=_tex("ADD_POINT_1")
		plus.texture_pressed=_tex("ADD_POINT_2")
		plus.texture_disabled=_tex("ADD_POINT_3")
		plus.position=Vector2(93,[99,119,139,159][index])
		plus.size=Vector2(17,17)
		plus.ignore_texture_size=true
		plus.disabled=true
		plus.pressed.connect(_submit_character_attribute.bind(index+1))
		_character_point_buttons.append(plus)
		character.add_child(plus)
	for pair in [
		["名　稱",Rect2(11,37,36,12)],["稱　號",Rect2(11,53,36,12)],
		["職　業",Rect2(11,69,36,12)],["等級",Rect2(196,32,24,12)],
		["力量",Rect2(14,102,24,12)],["速度",Rect2(13,122,24,12)],
		["體質",Rect2(13,142,24,12)],["智慧",Rect2(13,162,24,12)],
		["神級",Rect2(13,191,24,12)],["GExp",Rect2(12,208,27,12)],
		["向性",Rect2(5,232,24,12)],["金錢",Rect2(5,249,24,12)],
		["生命",Rect2(5,268,24,12)],["魔力",Rect2(5,285,24,12)],["意志",Rect2(5,302,24,12)],
		["攻　擊",Rect2(148,102,36,12)],["防　御",Rect2(148,119,36,12)],
		["魔　攻",Rect2(148,136,36,12)],["魔　防",Rect2(148,153,36,12)],
		["命　中",Rect2(148,170,36,12)],["回　避",Rect2(148,187,36,12)],
		["幸　運",Rect2(148,204,36,12)],["總攻擊",Rect2(148,226,36,12)],["總防禦",Rect2(148,243,36,12)],
		["配點",Rect2(140,268,24,12)],["升級",Rect2(140,285,24,12)],["死亡",Rect2(140,302,24,12)],
	]:
		_character_text("Caption"+pair[0],pair[1],pair[0],false)
	for pair in [
		["名稱",Rect2(50,37,118,12)],["稱號",Rect2(50,53,118,12)],
		["職業",Rect2(50,69,118,12)],["等級",Rect2(223,31,33,12)],
		["力量",Rect2(40,102,47,10)],["速度",Rect2(40,122,47,10)],
		["體質",Rect2(40,142,47,10)],["智慧",Rect2(40,162,47,10)],
		["神級",Rect2(42,190,74,11)],["GExp",Rect2(42,208,75,10)],
		["向性",Rect2(30,232,92,12)],["金錢",Rect2(30,249,92,12)],
		["HP",Rect2(30,268,92,10)],["MP",Rect2(30,285,92,10)],["意志",Rect2(30,302,92,10)],
		["攻擊",Rect2(189,103,62,10)],["防禦",Rect2(189,120,62,10)],
		["魔攻",Rect2(189,137,62,10)],["魔防",Rect2(189,154,62,10)],
		["命中",Rect2(189,171,62,10)],["迴避",Rect2(189,188,62,10)],["幸運",Rect2(189,205,62,10)],
		["總攻擊",Rect2(189,227,62,10)],["總防禦",Rect2(189,244,62,10)],
		["配點",Rect2(166,268,92,12)],["EXP",Rect2(166,285,92,10)],["死亡",Rect2(165,302,94,12)],
	]:
		_character_labels[pair[0]]=_character_text("Field"+pair[0],pair[1],"—",true)

func _character_text(node_name:String,rect:Rect2,value:String,centered:bool)->CharacterText:
	var label:=CharacterText.new()
	label.name=node_name
	label.position=rect.position
	label.size=rect.size
	label.clip_contents=true
	label.mouse_filter=Control.MOUSE_FILTER_IGNORE
	label.centered=centered
	label.text_font=_character_font
	label.raster_font=_character_raster_font
	label.font_size=HK_SHARED_FONT_HEIGHT
	label.baseline=HK_SHARED_FONT_BASELINE
	label.text=value
	character.add_child(label)
	return label

func _character_vital_fill(art:String,origin:Vector2)->Control:
	var viewport:=Control.new()
	viewport.position=origin
	viewport.size=Vector2(0,10)
	viewport.clip_contents=true
	viewport.mouse_filter=Control.MOUSE_FILTER_IGNORE
	var strip:=TextureRect.new()
	strip.texture=_tex(art+"_0")
	strip.mouse_filter=Control.MOUSE_FILTER_IGNORE
	viewport.add_child(strip)
	character.add_child(viewport)
	return viewport

func _build_skill()->void:
	var skill_script:=load("res://ui/classic_skill_window.gd")
	skill_window=skill_script.new()
	skill_window.position=Vector2(542,63)
	skill_window.connect("skill_selected",func(skill_id:int):
		if _panel==null:return
		_panel.skill_id_input.value=skill_id
		_panel.skill_target_mode.button_pressed=true)
	stage.add_child(skill_window)
	skill_window.visible=false

func _build_party()->void:
	# HK 026_TEAM.itf: keep source rects exact. The original handler map binds
	# 2493=BuildTeam, 2685=QuitTeam, 2475..2480=KickTeamMem(index).
	party_window=Control.new()
	party_window.name="PartyWindow"
	party_window.position=Vector2(626,424)
	party_window.size=Vector2(139,144)
	party_window.visible=false
	stage.add_child(party_window)
	_make_movable(party_window)
	_background(party_window,"BG_TEAM",party_window.size)
	_texture_button(party_window,"PartyBuild","B_TEAM_BUILDUP",Vector2(4,1),Vector2(16,16),_party_create,"建立隊伍")
	_texture_button(party_window,"PartyLeave","B_TEAM_GOOUT",Vector2(23,1),Vector2(16,16),_party_leave,"離開隊伍")
	_texture_button(party_window,"PartyComplex","B_COMPLEX_TEAM",Vector2(42,1),Vector2(16,16),Callable(),"隊伍功能")
	_texture_button(party_window,"PartyHelp","B_ONLINEHELP",Vector2(102,1),Vector2(16,16),Callable(),"線上說明")
	_texture_button(party_window,"PartyClose","CLOSESCREEN",Vector2(119,1),Vector2(16,16),func():party_window.hide(),"關閉")
	for i in range(6):
		var y:=24.0+20.0*i
		var label:=_itf_text(party_window,"PartyMember%d"%i,Rect2(9,y,120,12),9)
		_party_member_labels.append(label)
		var hit:=_hit(party_window,"PartyMemberHit%d"%i,Rect2(9,y,120,12),_party_member_pressed.bind(i),"隊伍成員")
		hit.modulate=Color(1,1,1,0.01)
		var hp:=_animated_progress(party_window,"PartyHp%d"%i,Rect2(10,38+20*i,88,3),"HP",16)
		var mp:=_animated_progress(party_window,"PartyMp%d"%i,Rect2(100,38+20*i,29,3),"MP",16)
		_party_hp_clips.append(hp.clip)
		_party_mp_clips.append(mp.clip)

	# Old CTeamItf::OnQuitTeam constructs CYesNoBox with an empty ITF path;
	# CYesNoBox::Create therefore loads 010_MESSAGE01.itf and CenterScreen
	# integer-centers its 256x119 source rect at (272,240) on the 800x600 UI.
	# Keep a full-canvas blocker so the confirmation is actually modal while
	# preserving the inner source geometry and original BG/YELLOW_BUTTON art.
	_party_leave_blocker.name="PartyLeaveModalBlocker"
	_party_leave_blocker.position=Vector2.ZERO
	_party_leave_blocker.size=BASE_SIZE
	_party_leave_blocker.visible=false
	_party_leave_blocker.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_party_leave_blocker)
	_party_leave_question.name="PartyLeaveQuestion"
	_party_leave_question.position=Vector2(272,240)
	_party_leave_question.size=Vector2(256,119)
	_party_leave_question.mouse_filter=Control.MOUSE_FILTER_STOP
	_party_leave_blocker.add_child(_party_leave_question)
	_background(_party_leave_question,"BG_MESSAGE",_party_leave_question.size)
	_party_leave_question_text=_itf_text(
		_party_leave_question,"PartyLeaveQuestionText",Rect2(15,11,231,71),12)
	_texture_button(
		_party_leave_question,"PartyLeaveConfirm","YELLOW_BUTTON",Vector2(38,93),Vector2(85,21),
		_confirm_party_leave,"確定")
	_texture_button(
		_party_leave_question,"PartyLeaveCancel","YELLOW_BUTTON",Vector2(138,93),Vector2(85,21),
		_cancel_party_leave,"取消")
	var leave_confirm_text:=_itf_text(
		_party_leave_question,"PartyLeaveConfirmText",Rect2(69,97,24,12),12,true)
	leave_confirm_text.text="確定"
	var leave_cancel_text:=_itf_text(
		_party_leave_question,"PartyLeaveCancelText",Rect2(169,97,24,12),12,true)
	leave_cancel_text.text="取消"

	# Paradise CSGOUim::OnTeamInviteReq routes through ONUIQuestion and
	# CReplierItf, which loads 044_QUESTION_BOX.itf.
	_party_question.name="PartyInviteQuestion"
	_party_question.position=Vector2(166,290)
	_party_question.size=Vector2(469,19)
	_party_question.visible=false
	_party_question.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_party_question)
	_background(_party_question,"BG_QUESTION_BOX",_party_question.size)
	_party_question_text=_itf_text(
		_party_question,"QuestionText",Rect2(9,3,382,12),12)
	_texture_button(_party_question,"QuestionYes","B_OK_BUTTON",Vector2(424,1),Vector2(16,16),_accept_party_invite,"接受")
	_texture_button(_party_question,"QuestionNo","CLOSESCREEN",Vector2(444,1),Vector2(16,16),_reject_party_invite,"拒絕")

	# 040_Rightbox.itf is created at the clicked player's runtime x/y. Paradise
	# CReqMenuItf::MessageMap proves 3016 (0xBC8)=OnTeam and 3015
	# (0xBC7)=OnFriend. Keep the remaining source rows unbound until their own
	# authoritative slices are implemented.
	_request_menu.name="PlayerRequestMenu"
	_request_menu.size=Vector2(86,99)
	_request_menu.visible=false
	_request_menu.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_request_menu)
	_background(_request_menu,"BG_RIGHTBOX",_request_menu.size)
	var rows:=[
		["Trade",0.0,"交易"],["Transfer",15.0,"傳送"],["Buy",28.0,"購買"],
		["Team",42.0,"組隊"],["Friend",57.0,"好友"],["Clan",70.0,"社團"],["Extra",84.0,"更多"]]
	for row in rows:
		var action:=str(row[0])
		var callback:=_request_team if action=="Team" else (_request_friend if action=="Friend" else (_request_clan if action=="Clan" else Callable()))
		_texture_button(_request_menu,"Request"+action,"B_RIGHTBOX_BUTTON",Vector2(-1,float(row[1])),Vector2(86,14),callback,str(row[2]))

func _build_clan()->void:
	# 057_CLUB_Menu.itf: 205x363 source-local geometry.
	clan_window=_window("ClanWindow","BG_CLUB_MENU",Vector2(205,363),Vector2(269,113))
	var clan_close:=clan_window.get_node("CloseWindow") as TextureButton
	clan_close.position=Vector2(186,1)
	clan_close.size=Vector2(16,16)
	_texture_button(
		clan_window,"ClanHelp","B_ONLINEHELP",Vector2(170,1),Vector2(16,16),
		Callable(),"線上說明")
	for raw in [
		["ClanNameCaption",Rect2(11,38,48,12),"學會名稱"],
		["ClanBankCaption",Rect2(11,53,48,12),"學會金庫"],
		["ClanMaintenanceCaption",Rect2(10,69,48,12),"維持日計"],
		["ClanCommandCaption",Rect2(79,115,48,12),"學會指令"],
		["ClanMembersCaption",Rect2(10,85,48,12),"學會人數"]
	]:
		var spec:Array=raw
		var caption:=_itf_text(clan_window,str(spec[0]),spec[1],9)
		caption.text=str(spec[2])
	_clan_name_value=_itf_text(clan_window,"ClanNameValue",Rect2(68,37,118,12),9)
	_clan_member_count_value=_itf_text(clan_window,"ClanMemberCount",Rect2(67,86,30,12),9)
	# Bank and maintenance are intentionally blank until their authoritative
	# gameplay slices exist; do not synthesize values into original fields.
	_itf_text(clan_window,"ClanBankValue",Rect2(80,53,106,12),9)
	_itf_text(clan_window,"ClanMaintenanceValue",Rect2(68,69,118,12),9)

	var command_specs=[
		["ClanTransfer","B_CLUB_B01",Vector2(10,131),Vector2(91,15),"會長讓位",Rect2(31,133,48,12),_clan_transfer_selected],
		["ClanDissolve","B_CLUB_B01",Vector2(104,131),Vector2(91,15),"解散學會",Rect2(125,133,48,12),Callable()],
		["ClanDelegate","B_CLUB_B01",Vector2(10,148),Vector2(91,15),"委任",Rect2(43,150,24,12),Callable()],
		["ClanAlliance","B_CLUB_B01",Vector2(104,148),Vector2(91,15),"成立星盟",Rect2(125,150,48,12),Callable()],
		["ClanCancelDelegate","B_CLUB_B01",Vector2(10,165),Vector2(91,15),"撤銷委位",Rect2(31,167,48,12),Callable()],
		["ClanKick","B_CLUB_B01",Vector2(10,212),Vector2(91,15),"解除會員",Rect2(31,214,48,12),_clan_kick_selected],
		["ClanWithdraw","B_CLUB_B01",Vector2(104,212),Vector2(91,15),"提取資金",Rect2(125,214,48,12),Callable()],
		["ClanInvest","B_CLUB_B01",Vector2(10,229),Vector2(91,15),"投資開發",Rect2(31,231,48,12),Callable()],
		["ClanUnfriend","B_CLUB_B01",Vector2(104,229),Vector2(91,15),"友會解除",Rect2(125,231,48,12),Callable()],
		["ClanResearch","B_CLUB_B01",Vector2(10,246),Vector2(91,15),"開發研究",Rect2(31,248,48,12),Callable()],
		["ClanIntro","B_CLUB_B01",Vector2(104,246),Vector2(91,15),"學會簡介",Rect2(125,248,48,12),Callable()],
		["ClanUpgrade","B_CLUB_B01",Vector2(10,263),Vector2(91,15),"研究升級",Rect2(31,265,48,12),Callable()],
		["ClanNews","B_CLUB_B01",Vector2(104,263),Vector2(91,15),"學會通訊",Rect2(125,265,48,12),_open_clan_announcement],
		["ClanResign","B_CLUB_B01",Vector2(10,280),Vector2(91,15),"請辭",Rect2(43,282,24,12),Callable()],
		["ClanLeave","B_CLUB_B01",Vector2(10,326),Vector2(91,15),"脫離學會",Rect2(31,328,48,12),_clan_leave],
		["ClanDonate","B_CLUB_B01",Vector2(104,326),Vector2(91,15),"捐款",Rect2(137,328,24,12),Callable()]
	]
	for raw in command_specs:
		var spec:Array=raw
		var callback:Callable=spec[6]
		var button:=_texture_button(
			clan_window,str(spec[0]),str(spec[1]),spec[2],spec[3],callback,str(spec[4]))
		if not callback.is_valid():
			button.disabled=true
		var label:=_itf_text(clan_window,str(spec[0])+"Text",spec[5],9,true)
		label.text=str(spec[4])
	var info:=_texture_button(
		clan_window,"ClanInfo","B_CLUB_B03",Vector2(107,84),Vector2(85,15),
		_open_clan_member_window,"學會資訊")
	var info_text:=_itf_text(clan_window,"ClanInfoText",Rect2(124,86,48,12),9,true)
	info_text.text="學會資訊"

	# 060_CLUB_MemList.itf: this is a source page, not a synthetic VBox.
	_clan_member_window=Control.new()
	_clan_member_window.name="ClanMemberWindow"
	_clan_member_window.position=Vector2(269,113)
	_clan_member_window.size=Vector2(263,255)
	_clan_member_window.visible=false
	stage.add_child(_clan_member_window)
	_make_movable(_clan_member_window)
	_deferred_background(_clan_member_window,"BG_CLUB_MEMLIST",_clan_member_window.size)
	var member_header:=_itf_text(_clan_member_window,"MemberNameHeader",Rect2(72,24,48,12),9)
	member_header.text="會員名字"
	var role_header:=_itf_text(_clan_member_window,"MemberRoleHeader",Rect2(187,24,24,12),9)
	role_header.text="職位"
	for i in range(13):
		var y:=40.0+16.0*i
		_clan_member_state_labels.append(
			_itf_text(_clan_member_window,"MemberState%d"%i,Rect2(9,y,18,12),9,true))
		_clan_member_name_labels.append(
			_itf_text(_clan_member_window,"MemberName%d"%i,Rect2(36,y,120,12),9))
		_clan_member_role_labels.append(
			_itf_text(_clan_member_window,"MemberRole%d"%i,Rect2(164,y,72,12),9))
		_clan_member_actor_ids.append(0)
		var row_hit:=_hit(
			_clan_member_window,"MemberHit%d"%i,Rect2(8,y,228,12),
			_select_clan_member.bind(i),"選擇會員")
		row_hit.modulate=Color(1,1,1,0.01)
	_texture_button(
		_clan_member_window,"MemberScrollUp","SCROLLUP_BUTTON",Vector2(242,21),Vector2(17,36),
		_scroll_clan_members.bind(-1),"向上")
	_texture_button(
		_clan_member_window,"MemberScrollDown","SCROLLDOWN_BUTTON",Vector2(242,210),Vector2(17,36),
		_scroll_clan_members.bind(1),"向下")

	# 070_CLUB_News_EDIT.itf: leader edits, members can read; Server remains
	# authoritative and rejects non-leader mutations.
	_clan_announcement_window=Control.new()
	_clan_announcement_window.name="ClanAnnouncementWindow"
	_clan_announcement_window.position=Vector2(269,110)
	_clan_announcement_window.size=Vector2(263,345)
	_clan_announcement_window.visible=false
	stage.add_child(_clan_announcement_window)
	_make_movable(_clan_announcement_window)
	_deferred_background(_clan_announcement_window,"BG_CLUB_INTRO_EDIT",_clan_announcement_window.size)
	var current_caption:=_itf_text(
		_clan_announcement_window,"CurrentAnnouncementCaption",Rect2(13,25,72,12),9)
	current_caption.text="學會現有通訊"
	var new_caption:=_itf_text(
		_clan_announcement_window,"NewAnnouncementCaption",Rect2(13,179,108,12),9)
	new_caption.text="請輸入新的學會通訊"
	var remain_caption:=_itf_text(
		_clan_announcement_window,"AnnouncementRemainCaption",Rect2(142,179,72,12),9)
	remain_caption.text="餘下可用字數"
	_clan_announcement_remaining=_itf_text(
		_clan_announcement_window,"AnnouncementRemain",Rect2(216,179,20,12),9,true)
	_clan_current_announcement=TextEdit.new()
	_clan_current_announcement.name="CurrentAnnouncement"
	_clan_current_announcement.position=Vector2(8,46)
	_clan_current_announcement.size=Vector2(228,108)
	_clan_current_announcement.editable=false
	_clan_current_announcement.wrap_mode=TextEdit.LINE_WRAPPING_BOUNDARY
	if _character_raster_font!=null:
		_clan_current_announcement.add_theme_font_override("font",_character_raster_font)
	_clan_current_announcement.add_theme_font_size_override("font_size",9)
	_clan_announcement_window.add_child(_clan_current_announcement)
	_clan_announcement_input=TextEdit.new()
	_clan_announcement_input.name="AnnouncementInput"
	_clan_announcement_input.position=Vector2(8,200)
	_clan_announcement_input.size=Vector2(228,108)
	_clan_announcement_input.wrap_mode=TextEdit.LINE_WRAPPING_BOUNDARY
	if _character_raster_font!=null:
		_clan_announcement_input.add_theme_font_override("font",_character_raster_font)
	_clan_announcement_input.add_theme_font_size_override("font_size",9)
	_clan_announcement_input.text_changed.connect(_clan_announcement_changed)
	_clan_announcement_window.add_child(_clan_announcement_input)
	_clan_announcement_confirm=_texture_button(
		_clan_announcement_window,"AnnouncementConfirm","B_CLUE_B05",Vector2(36,326),Vector2(85,15),
		_submit_clan_announcement,"確定")
	var announcement_confirm_text:=_itf_text(
		_clan_announcement_window,"AnnouncementConfirmText",Rect2(67,328,24,12),9,true)
	announcement_confirm_text.text="確定"
	_texture_button(
		_clan_announcement_window,"AnnouncementCancel","B_CLUE_B05",Vector2(142,326),Vector2(85,15),
		_cancel_clan_announcement,"取消")
	var announcement_cancel_text:=_itf_text(
		_clan_announcement_window,"AnnouncementCancelText",Rect2(173,328,24,12),9,true)
	announcement_cancel_text.text="取消"

	# Original right-click handler OnFriendClubInvite feeds the same compact
	# question-family surface used by other social invitations.
	_clan_question.name="ClanInviteQuestion"
	_clan_question.position=Vector2(166,290)
	_clan_question.size=Vector2(469,19)
	_clan_question.visible=false
	_clan_question.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_clan_question)
	_background(_clan_question,"BG_QUESTION_BOX",_clan_question.size)
	_clan_question_text=_itf_text(
		_clan_question,"QuestionText",Rect2(9,3,382,12),12)
	_texture_button(
		_clan_question,"QuestionYes","B_OK_BUTTON",Vector2(424,1),Vector2(16,16),
		_accept_clan_invite,"接受")
	_texture_button(
		_clan_question,"QuestionNo","CLOSESCREEN",Vector2(444,1),Vector2(16,16),
		_reject_clan_invite,"拒絕")

	# HK SGOClient sub_4AC1CD loads 061_CLUB_Name.itf before the create request,
	# then caps its edit control at 10.
	_clan_name_dialog.name="ClanNameDialog"
	_clan_name_dialog.position=Vector2(272,250)
	_clan_name_dialog.size=Vector2(256,100)
	_clan_name_dialog.visible=false
	_clan_name_dialog.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_clan_name_dialog)
	_background(_clan_name_dialog,"BG_RANK_01",_clan_name_dialog.size)
	_texture_button(
		_clan_name_dialog,"ClanNameClose","CLOSESCREEN",Vector2(238,4),Vector2(16,16),
		_close_clan_name,"取消")
	_texture_button(
		_clan_name_dialog,"ClanNameHelp","B_ONLINEHELP",Vector2(220,4),Vector2(16,16),
		Callable(),"說明")
	var prompt:=_itf_text(
		_clan_name_dialog,"ClanNamePrompt",Rect2(10,29,120,14),12)
	prompt.text="請輸入將要成立的皇家戰團名字"
	_clan_name_input.name="ClanNameInput"
	_clan_name_input.position=Vector2(14,53)
	_clan_name_input.size=Vector2(236,12)
	_clan_name_input.max_length=10
	_clan_name_input.display_font=_character_raster_font
	_clan_name_input.display_font_size=12
	_clan_name_input.baseline=10.0
	_clan_name_input.text_changed.connect(func(_value):_update_clan_name_action())
	_clan_name_input.text_submitted.connect(func(_value):_submit_clan_name())
	_clan_name_dialog.add_child(_clan_name_input)
	_clan_name_confirm=_texture_button(
		_clan_name_dialog,"ClanNameConfirm","YELLOW_BUTTON",Vector2(166,73),Vector2(85,21),
		_submit_clan_name,"確定")
	var confirm_text:=_itf_text(
		_clan_name_dialog,"ClanNameConfirmText",Rect2(197,77,24,14),12,true)
	confirm_text.text="確定"
	_update_clan_name_action()

func _build_shop()->void:
	shop_window=_window("ShopWindow","BG_SHOP",Vector2(770,506),Vector2(15,69))
	_shop_buy_rows.name="ShopBuyRows"
	_shop_buy_rows.position=Vector2(36,60)
	_shop_buy_rows.size=Vector2(410,425)
	_shop_buy_rows.add_theme_constant_override("separation",0)
	shop_window.add_child(_shop_buy_rows)
	_shop_sell_rows.name="ShopSellRows"
	_shop_sell_rows.position=Vector2(548,60)
	_shop_sell_rows.size=Vector2(164,425)
	_shop_sell_rows.add_theme_constant_override("separation",0)
	shop_window.add_child(_shop_sell_rows)

func _build_quest()->void:
	quest_window=_window("QuestWindow","BG_QUEST_LIST",Vector2(527,350),Vector2(137,125))
	_quest_title=_classic_label(quest_window,"QuestTitle",Rect2(269,36,230,22),11,false)
	_quest_text=_classic_label(quest_window,"QuestText",Rect2(6,216,247,118),10,false)
	_quest_text.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_quest_choices.name="QuestChoices"
	_quest_choices.position=Vector2(268,214)
	_quest_choices.size=Vector2(233,96)
	_quest_choices.add_theme_constant_override("separation",2)
	quest_window.add_child(_quest_choices)

func _build_player_shop()->void:
	# 092_PShop.itf: 770x506. Keep decoded local rects for the stall name,
	# selling rows and right-side inventory instead of the former generic list.
	player_shop_window=_window("PlayerShopWindow","BG_PSHOP_01",Vector2(770,506),Vector2(15,69))
	_player_shop_name_label=_itf_text(player_shop_window,"PlayerShopName",Rect2(79,37,170,12),10)
	_itf_text(player_shop_window,"PlayerShopNameCaption",Rect2(3,37,72,14),10).text="小商店名稱："
	_itf_text(player_shop_window,"PlayerShopSellTitle",Rect2(63,68,60,14),10,true).text="發售之商品"
	_itf_text(player_shop_window,"PlayerShopPriceTitle",Rect2(197,68,60,14),10,true).text="發售之價格"
	var rename:=_hit(player_shop_window,"PlayerShopRename",Rect2(258,36,62,15),_open_player_shop_name_dialog,"修改小商店名稱")
	rename.text=""
	_itf_text(player_shop_window,"PlayerShopRenameText",Rect2(265,38,48,14),10,true).text="修改名稱"
	_player_shop_rows.name="PlayerShopRows"
	_player_shop_rows.position=Vector2(9,85)
	_player_shop_rows.size=Vector2(306,244)
	_player_shop_rows.add_theme_constant_override("separation",2)
	player_shop_window.add_child(_player_shop_rows)
	_player_shop_inventory_rows.name="PlayerShopInventoryRows"
	_player_shop_inventory_rows.position=Vector2(553,62)
	_player_shop_inventory_rows.size=Vector2(150,432)
	_player_shop_inventory_rows.add_theme_constant_override("separation",2)
	player_shop_window.add_child(_player_shop_inventory_rows)
	_player_shop_action_text=_itf_text(player_shop_window,"PlayerShopActionText",Rect2(9,341,214,14),10)
	_player_shop_buy=_texture_button(
		player_shop_window,"PlayerShopCloseAll","YELLOW_BUTTON",Vector2(230,337),Vector2(85,21),
		_open_player_shop_close_question,"關閉小商店")
	_itf_text(player_shop_window,"PlayerShopCloseAllText",Rect2(246,341,54,12),10,true).text="關閉小商店"

	# 092_PShop_Name.itf.
	_player_shop_name_dialog.name="PlayerShopNameDialog"
	_player_shop_name_dialog.position=Vector2(272,250)
	_player_shop_name_dialog.size=Vector2(256,100)
	_player_shop_name_dialog.visible=false
	_player_shop_name_dialog.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_player_shop_name_dialog)
	_background(_player_shop_name_dialog,"BG_RANK_01",_player_shop_name_dialog.size)
	_texture_button(_player_shop_name_dialog,"PlayerShopNameClose","CLOSESCREEN",Vector2(239,4),Vector2(16,16),_close_player_shop_name_dialog,"取消")
	_texture_button(_player_shop_name_dialog,"PlayerShopNameHelp","B_ONLINEHELP",Vector2(222,4),Vector2(16,16),Callable(),"說明")
	_itf_text(_player_shop_name_dialog,"PlayerShopNamePrompt",Rect2(9,29,220,14),10).text="輸入小商店名稱(上限為20個中文字)"
	_player_shop_name_input.name="PlayerShopNameInput"
	_player_shop_name_input.position=Vector2(15,53)
	_player_shop_name_input.size=Vector2(236,12)
	_player_shop_name_input.max_length=20
	_player_shop_name_input.display_font=_character_raster_font
	_player_shop_name_input.display_font_size=12
	_player_shop_name_input.baseline=10.0
	_player_shop_name_input.text_changed.connect(func(_value):_update_player_shop_name_action())
	_player_shop_name_input.text_submitted.connect(func(_value):_submit_player_shop_name())
	_player_shop_name_dialog.add_child(_player_shop_name_input)
	_player_shop_name_confirm=_texture_button(_player_shop_name_dialog,"PlayerShopNameConfirm","YELLOW_BUTTON",Vector2(167,73),Vector2(85,21),_submit_player_shop_name,"確定")
	_itf_text(_player_shop_name_dialog,"PlayerShopNameConfirmText",Rect2(198,77,24,14),10,true).text="確定"
	_update_player_shop_name_action()

	# 092_PShop_Sell_Item_01.itf: price entry.
	_player_shop_sell_price_dialog.name="PlayerShopSellPriceDialog"
	_player_shop_sell_price_dialog.position=Vector2(289,238)
	_player_shop_sell_price_dialog.size=Vector2(223,125)
	_player_shop_sell_price_dialog.visible=false
	_player_shop_sell_price_dialog.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_player_shop_sell_price_dialog)
	_background(_player_shop_sell_price_dialog,"BG_PSHOP_ITEM_01",_player_shop_sell_price_dialog.size)
	_player_shop_sell_price_input.name="PlayerShopSellPriceInput"
	_player_shop_sell_price_input.position=Vector2(82,70)
	_player_shop_sell_price_input.size=Vector2(94,14)
	_player_shop_sell_price_input.max_length=16
	_player_shop_sell_price_input.display_font=_character_raster_font
	_player_shop_sell_price_input.display_font_size=12
	_player_shop_sell_price_input.baseline=11.0
	_player_shop_sell_price_input.text_changed.connect(func(_value):_update_player_shop_sell_price_action())
	_player_shop_sell_price_input.text_submitted.connect(func(_value):_confirm_player_shop_sell_price())
	_player_shop_sell_price_dialog.add_child(_player_shop_sell_price_input)
	_texture_button(_player_shop_sell_price_dialog,"PlayerShopSellPriceOk","YELLOW_BUTTON",Vector2(39,98),Vector2(85,21),_confirm_player_shop_sell_price,"確定")
	_texture_button(_player_shop_sell_price_dialog,"PlayerShopSellPriceCancel","YELLOW_BUTTON",Vector2(132,98),Vector2(85,21),_cancel_player_shop_sell,"取消")
	_itf_text(_player_shop_sell_price_dialog,"PlayerShopSellPriceOkText",Rect2(69,102,24,12),10,true).text="確定"
	_itf_text(_player_shop_sell_price_dialog,"PlayerShopSellPriceCancelText",Rect2(162,102,24,12),10,true).text="取消"

	# 092_PShop_Sell_Item_02.itf: quantity entry.
	_player_shop_sell_quantity_dialog.name="PlayerShopSellQuantityDialog"
	_player_shop_sell_quantity_dialog.position=Vector2(289,238)
	_player_shop_sell_quantity_dialog.size=Vector2(223,125)
	_player_shop_sell_quantity_dialog.visible=false
	_player_shop_sell_quantity_dialog.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_player_shop_sell_quantity_dialog)
	_background(_player_shop_sell_quantity_dialog,"BG_PSHOP_ITEM_02",_player_shop_sell_quantity_dialog.size)
	_player_shop_sell_quantity_input.name="PlayerShopSellQuantityInput"
	_player_shop_sell_quantity_input.position=Vector2(67,71)
	_player_shop_sell_quantity_input.size=Vector2(69,12)
	_player_shop_sell_quantity_input.max_length=7
	_player_shop_sell_quantity_input.display_font=_character_raster_font
	_player_shop_sell_quantity_input.display_font_size=12
	_player_shop_sell_quantity_input.baseline=10.0
	_player_shop_sell_quantity_input.text_changed.connect(func(_value):_update_player_shop_sell_quantity_action())
	_player_shop_sell_quantity_input.text_submitted.connect(func(_value):_confirm_player_shop_sell_quantity())
	_player_shop_sell_quantity_dialog.add_child(_player_shop_sell_quantity_input)
	_texture_button(_player_shop_sell_quantity_dialog,"PlayerShopSellQuantityOk","YELLOW_BUTTON",Vector2(39,98),Vector2(85,21),_confirm_player_shop_sell_quantity,"確定")
	_texture_button(_player_shop_sell_quantity_dialog,"PlayerShopSellQuantityCancel","YELLOW_BUTTON",Vector2(132,98),Vector2(85,21),_cancel_player_shop_sell,"取消")
	_itf_text(_player_shop_sell_quantity_dialog,"PlayerShopSellQuantityOkText",Rect2(69,102,24,12),10,true).text="確定"
	_itf_text(_player_shop_sell_quantity_dialog,"PlayerShopSellQuantityCancelText",Rect2(162,102,24,12),10,true).text="取消"

	# 092_PShop_Buy_Item_02.itf. V1 Server authority buys one complete listing;
	# quantity is shown from the authoritative listing and is intentionally not
	# made editable until original partial-buy packet semantics are verified.
	_player_shop_buy_dialog.name="PlayerShopBuyDialog"
	_player_shop_buy_dialog.position=Vector2(289,238)
	_player_shop_buy_dialog.size=Vector2(223,125)
	_player_shop_buy_dialog.visible=false
	_player_shop_buy_dialog.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_player_shop_buy_dialog)
	_background(_player_shop_buy_dialog,"BG_PSHOP_BUY_02",_player_shop_buy_dialog.size)
	_player_shop_buy_quantity=_itf_text(_player_shop_buy_dialog,"PlayerShopBuyQuantity",Rect2(66,71,50,12),10,true)
	_itf_text(_player_shop_buy_dialog,"PlayerShopBuyEquals",Rect2(123,71,8,12),10,true).text="="
	_player_shop_buy_total=_itf_text(_player_shop_buy_dialog,"PlayerShopBuyTotal",Rect2(131,71,80,12),10)
	_texture_button(_player_shop_buy_dialog,"PlayerShopBuyOk","YELLOW_BUTTON",Vector2(39,98),Vector2(85,21),_confirm_player_shop_buy,"確定")
	_texture_button(_player_shop_buy_dialog,"PlayerShopBuyCancel","YELLOW_BUTTON",Vector2(132,98),Vector2(85,21),_cancel_player_shop_buy,"取消")
	_itf_text(_player_shop_buy_dialog,"PlayerShopBuyOkText",Rect2(69,102,24,12),10,true).text="確定"
	_itf_text(_player_shop_buy_dialog,"PlayerShopBuyCancelText",Rect2(162,102,24,12),10,true).text="取消"

	# Reuse the original 044 question bar geometry for closing the whole stall.
	_player_shop_close_question.name="PlayerShopCloseQuestion"
	_player_shop_close_question.position=Vector2(166,290)
	_player_shop_close_question.size=Vector2(469,19)
	_player_shop_close_question.visible=false
	_player_shop_close_question.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_player_shop_close_question)
	_background(_player_shop_close_question,"BG_QUESTION_BOX",_player_shop_close_question.size)
	_player_shop_close_question_text=_itf_text(_player_shop_close_question,"QuestionText",Rect2(9,3,382,12),10)
	_texture_button(_player_shop_close_question,"QuestionYes","B_OK_BUTTON",Vector2(424,1),Vector2(16,16),_confirm_player_shop_close_all,"確定")
	_texture_button(_player_shop_close_question,"QuestionNo","CLOSESCREEN",Vector2(444,1),Vector2(16,16),_cancel_player_shop_close_all,"取消")

func _build_storage()->void:
	storage_window=_window("StorageWindow","BG_BANK",Vector2(770,506),Vector2(15,69))
	_storage_rows.name="StorageRows"
	_storage_rows.position=Vector2(35,59)
	_storage_rows.size=Vector2(165,425)
	_storage_rows.add_theme_constant_override("separation",0)
	storage_window.add_child(_storage_rows)
	_storage_inventory_rows.name="StorageInventoryRows"
	_storage_inventory_rows.position=Vector2(548,59)
	_storage_inventory_rows.size=Vector2(165,425)
	_storage_inventory_rows.add_theme_constant_override("separation",0)
	storage_window.add_child(_storage_inventory_rows)

func _build_bank()->void:
	bank_window=_window("BankWindow","BG_NPC_MESSAGE",Vector2(399,172),Vector2(201,214))
	_bank_status=_classic_label(bank_window,"BankStatus",Rect2(28,35,343,45),11,false)
	_bank_status.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_bank_amount.name="BankAmount"
	_bank_amount.position=Vector2(30,88)
	_bank_amount.size=Vector2(140,24)
	_bank_amount.min_value=1
	_bank_amount.max_value=9000000000000000
	_bank_amount.step=1
	_bank_amount.value=1
	bank_window.add_child(_bank_amount)
	var deposit:=_hit(bank_window,"BankDeposit",Rect2(180,88,82,24),func():
		if _panel!=null:_panel.submit_bank_deposit(int(_bank_amount.value)),"存入金錢")
	deposit.text="存入"
	var withdraw:=_hit(bank_window,"BankWithdraw",Rect2(272,88,82,24),func():
		if _panel!=null:_panel.submit_bank_withdraw(int(_bank_amount.value)),"領出金錢")
	withdraw.text="領出"

func _build_repair()->void:
	repair_window=_window("RepairWindow","BG_REPAIR",Vector2(223,506),Vector2(542,63))
	_repair_rows.name="RepairRows"
	_repair_rows.position=Vector2(30,45)
	_repair_rows.size=Vector2(170,420)
	_repair_rows.add_theme_constant_override("separation",1)
	repair_window.add_child(_repair_rows)

func _build_trade()->void:
	trade_window=_window("TradeWindow","BG_TRADE",Vector2(770,506),Vector2(15,69))
	_trade_status=_classic_label(trade_window,"TradeStatus",Rect2(35,35,680,44),10,false)
	_trade_status.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_trade_rows.name="TradeRows"
	_trade_rows.position=Vector2(36,88)
	_trade_rows.size=Vector2(670,315)
	_trade_rows.add_theme_constant_override("separation",1)
	trade_window.add_child(_trade_rows)
	var accept:=_hit(trade_window,"TradeAccept",Rect2(36,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.accept_trade(),"接受交易")
	accept.text="接受"
	var reject:=_hit(trade_window,"TradeReject",Rect2(126,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.reject_trade(),"拒絕交易")
	reject.text="拒絕"
	var lock:=_hit(trade_window,"TradeLock",Rect2(216,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.trade_lock(),"鎖定交易")
	lock.text="鎖定"
	var confirm:=_hit(trade_window,"TradeConfirm",Rect2(306,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.trade_final_confirm(),"確認交易")
	confirm.text="確認"
	var cancel:=_hit(trade_window,"TradeCancel",Rect2(396,430,80,24),func():
		if _panel!=null and _panel.session!=null:_panel.session.cancel_trade(),"取消交易")
	cancel.text="取消"

func _build_dialog()->void:
	# 047_NPC_message.itf is a 399x172 image-backed interface. It does not use
	# the temporary 570x428 BG_NPC_MESSAGE_06 layout or Godot-drawn panels.
	dialog_window=Control.new()
	dialog_window.name="NpcDialogWindow"
	dialog_window.position=Vector2(201,214)
	dialog_window.size=Vector2(399,172)
	stage.add_child(dialog_window)
	_make_movable(dialog_window)
	_background(dialog_window,"BG_NPC_MESSAGE",dialog_window.size)
	# Keep the compatibility title node for existing bindings, but the original
	# 047 interface has no separate title control.
	_dialog_title=_classic_label(dialog_window,"DialogTitle",Rect2(0,0,1,1),10,false)
	_dialog_title.hide()
	_dialog_text=_classic_label(dialog_window,"DialogText",Rect2(25,10,345,80),10,false)
	_dialog_text.add_theme_color_override("font_color",Color("#5b3514"))
	_dialog_text.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	# Four 13px message areas at y=108/123/138/153. VBox separation 2 gives
	# exactly those decoded ITF baselines without drawing replacement buttons.
	_dialog_choices.name="DialogChoices"
	_dialog_choices.position=Vector2(26,108)
	_dialog_choices.size=Vector2(345,58)
	_dialog_choices.add_theme_constant_override("separation",2)
	dialog_window.add_child(_dialog_choices)
	dialog_window.hide()

func _build_settings()->void:
	# 029_SYSTEM.itf + Old CSystemMenuItf. The original stores independent
	# 0..10 BGM/SFX levels and maps them to the engine's 0..255 mixer volume.
	settings_window=_window("SettingsWindow","BG_SYSTEM",Vector2(291,207),Vector2(255,196))
	var close:=settings_window.get_node_or_null("CloseWindow") as TextureButton
	if close!=null:
		close.position=Vector2(270,1)
		close.size=Vector2(16,16)
	var help:=_texture_button(settings_window,"OnlineHelp","B_ONLINEHELP",Vector2(253,1),Vector2(16,16),Callable(),"線上說明")
	help.disabled=true
	help.texture_disabled=_tex("B_ONLINEHELP_0")
	_itf_text(settings_window,"QualityLabel",Rect2(61,37,72,12),10).text="高畫質流暢度"
	_settings_quality_value=_itf_text(settings_window,"QualityValue",Rect2(168,37,22,11),10,true)
	_texture_button(settings_window,"QualityDown","MINISCREEN",Vector2(144,34),Vector2(17,17),_set_high_fps.bind(false),"25 FPS")
	_texture_button(settings_window,"QualityUp","ADD_POINT",Vector2(198,34),Vector2(16,16),_set_high_fps.bind(true),"50 FPS")
	_itf_text(settings_window,"BgmLabel",Rect2(37,71,72,12),10).text="背景音樂音量"
	_settings_bgm_value=_itf_text(settings_window,"BgmValue",Rect2(145,71,22,11),10,true)
	_texture_button(settings_window,"BgmDown","MINISCREEN",Vector2(121,69),Vector2(17,17),_change_bgm.bind(-1),"降低背景音樂")
	_texture_button(settings_window,"BgmUp","ADD_POINT",Vector2(175,69),Vector2(16,16),_change_bgm.bind(1),"提高背景音樂")
	_itf_text(settings_window,"SfxLabel",Rect2(35,105,72,12),10).text="音效音量"
	_settings_sfx_value=_itf_text(settings_window,"SfxValue",Rect2(118,105,22,11),10,true)
	_texture_button(settings_window,"SfxDown","MINISCREEN",Vector2(94,102),Vector2(17,17),_change_sfx.bind(-1),"降低音效")
	_texture_button(settings_window,"SfxUp","ADD_POINT",Vector2(148,102),Vector2(16,16),_change_sfx.bind(1),"提高音效")
	_itf_text(settings_window,"BattleModeLabel",Rect2(12,139,48,12),10).text="戰鬥模式"
	_settings_battle_value=_itf_text(settings_window,"BattleModeValue",Rect2(91,139,79,11),10,true)
	# Paradise CSystemMenuItf::OnPKDOWN/OnPKUP mutates a local 0..2 PKMode
	# profile value with wraparound. SaveProfile persists it under PKMode/Mode.
	_texture_button(settings_window,"BattleModeLeft","B_PVP_LEFT",Vector2(69,136),Vector2(17,17),_change_battle_mode.bind(-1),"上一個戰鬥模式")
	_texture_button(settings_window,"BattleModeRight","B_PVP_RIGHT",Vector2(177,137),Vector2(15,15),_change_battle_mode.bind(1),"下一個戰鬥模式")
	_texture_button(settings_window,"BackToServer","YELLOW_BUTTON",Vector2(27,168),Vector2(85,21),_open_system_action_question.bind(1),"回選擇伺服器")
	_texture_button(settings_window,"ExitGame","YELLOW_BUTTON",Vector2(125,168),Vector2(85,21),_open_system_action_question.bind(2),"離開遊戲")
	_itf_text(settings_window,"BackToServerText",Rect2(32,172,72,12),10,true).text="回選擇伺服器"
	_itf_text(settings_window,"ExitGameText",Rect2(141,172,48,12),10,true).text="離開遊戲"

	# CSystemMenuItf::OnSelectServer/OnExit create CYesNoBox with the default
	# 010_MESSAGE01.itf, then CenterScreen. Reuse the exact 256x119 source
	# geometry and original BG_MESSAGE / YELLOW_BUTTON art for both actions.
	_system_action_blocker.name="SystemActionModalBlocker"
	_system_action_blocker.position=Vector2.ZERO
	_system_action_blocker.size=BASE_SIZE
	_system_action_blocker.visible=false
	_system_action_blocker.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(_system_action_blocker)
	_system_action_question.name="SystemActionQuestion"
	_system_action_question.position=Vector2(272,240)
	_system_action_question.size=Vector2(256,119)
	_system_action_question.mouse_filter=Control.MOUSE_FILTER_STOP
	_system_action_blocker.add_child(_system_action_question)
	_background(_system_action_question,"BG_MESSAGE",_system_action_question.size)
	_system_action_question_text=_itf_text(
		_system_action_question,"SystemActionQuestionText",Rect2(15,11,231,71),12)
	_texture_button(
		_system_action_question,"SystemActionConfirm","YELLOW_BUTTON",Vector2(38,93),Vector2(85,21),
		_confirm_system_action,"確定")
	_texture_button(
		_system_action_question,"SystemActionCancel","YELLOW_BUTTON",Vector2(138,93),Vector2(85,21),
		_cancel_system_action,"取消")
	_itf_text(_system_action_question,"SystemActionConfirmText",Rect2(69,97,24,12),12,true).text="確定"
	_itf_text(_system_action_question,"SystemActionCancelText",Rect2(169,97,24,12),12,true).text="取消"
	_settings_skin_label=_classic_label(settings_window,"UiLabel",Rect2(9,18,80,12),9,false)
	_settings_skin_label.text="介面風格"
	_settings_picker.name="UiProfilePickerSettings"
	_settings_picker.position=Vector2(90,17)
	_settings_picker.size=Vector2(120,20)
	_settings_picker.item_selected.connect(func(index:int):
		var id:=str(_settings_picker.get_item_metadata(index))
		if not id.is_empty():skin_requested.emit(id))
	settings_window.add_child(_settings_picker)
	_settings_hint=_classic_label(settings_window,"SettingsHint",Rect2(212,17,70,20),8,false)
	_load_system_settings()
	_refresh_system_settings()
	set_skin_switch_visible(false)

func _ensure_audio_bus(bus_name:String)->int:
	var index:=AudioServer.get_bus_index(bus_name)
	if index>=0:return index
	AudioServer.add_bus()
	index=AudioServer.bus_count-1
	AudioServer.set_bus_name(index,bus_name)
	AudioServer.set_bus_send(index,"Master")
	return index

func _apply_audio_level(bus_name:String,level:int)->void:
	var index:=_ensure_audio_bus(bus_name)
	var normalized:=clampf(float(level)/10.0,0.0,1.0)
	AudioServer.set_bus_mute(index,level<=0)
	AudioServer.set_bus_volume_db(index,linear_to_db(maxf(normalized,0.0001)))

func _load_system_settings()->void:
	var config:=ConfigFile.new()
	if config.load(LOCAL_SETTINGS_PATH)==OK:
		_settings_bgm_level=clampi(int(config.get_value("audio","bgm",_settings_bgm_level)),0,10)
		_settings_sfx_level=clampi(int(config.get_value("audio","sfx",_settings_sfx_level)),0,10)
		_settings_high_fps=bool(config.get_value("display","high_fps",_settings_high_fps))
		_settings_battle_mode=clampi(int(config.get_value("combat","battle_mode",_settings_battle_mode)),0,2)
	_apply_audio_level("BGM",_settings_bgm_level)
	_apply_audio_level("SFX",_settings_sfx_level)
	Engine.max_fps=50 if _settings_high_fps else 25

func _save_system_settings()->void:
	var config:=ConfigFile.new()
	config.set_value("audio","bgm",_settings_bgm_level)
	config.set_value("audio","sfx",_settings_sfx_level)
	config.set_value("display","high_fps",_settings_high_fps)
	config.set_value("combat","battle_mode",_settings_battle_mode)
	config.save(LOCAL_SETTINGS_PATH)

func _refresh_system_settings()->void:
	if _settings_bgm_value!=null:_settings_bgm_value.text=str(_settings_bgm_level)
	if _settings_sfx_value!=null:_settings_sfx_value.text=str(_settings_sfx_level)
	if _settings_quality_value!=null:_settings_quality_value.text="ON" if _settings_high_fps else "OFF"
	if _settings_battle_value!=null:
		_settings_battle_value.text=["一般模式","團隊戰鬥模式","任意戰鬥模式"][_settings_battle_mode]

func _change_bgm(delta:int)->void:
	_settings_bgm_level=clampi(_settings_bgm_level+delta,0,10)
	_apply_audio_level("BGM",_settings_bgm_level)
	_refresh_system_settings()
	_save_system_settings()

func _change_sfx(delta:int)->void:
	_settings_sfx_level=clampi(_settings_sfx_level+delta,0,10)
	_apply_audio_level("SFX",_settings_sfx_level)
	_refresh_system_settings()
	_save_system_settings()

func _set_high_fps(enabled:bool)->void:
	_settings_high_fps=enabled
	Engine.max_fps=50 if enabled else 25
	_refresh_system_settings()
	_save_system_settings()

func _change_battle_mode(delta:int)->void:
	# Original OnPKUP/OnPKDOWN wraps 0..2 rather than clamping.
	_settings_battle_mode=posmod(_settings_battle_mode+delta,3)
	_refresh_system_settings()
	_save_system_settings()

func system_battle_mode()->int:
	return _settings_battle_mode

func _open_system_action_question(action:int)->void:
	if action not in [1,2] or _system_action_blocker.visible:
		return
	_system_action=action
	_system_action_question_text.text="你要進入其他伺服器？" if action==1 else "你決定離開系統？"
	_system_action_previous_focus=get_viewport().gui_get_focus_owner()
	_system_action_blocker.show()
	_system_action_blocker.move_to_front()

func _close_system_action_question(restore_focus:bool)->void:
	_system_action_blocker.hide()
	_system_action=0
	if restore_focus and is_instance_valid(_system_action_previous_focus) and _system_action_previous_focus.is_inside_tree():
		_system_action_previous_focus.grab_focus()
	_system_action_previous_focus=null

func _cancel_system_action()->void:
	_close_system_action_question(true)

func _confirm_system_action()->void:
	if _system_action not in [1,2]:
		return
	var action:=_system_action
	_close_system_action_question(false)
	_save_system_settings()
	settings_window.hide()
	# Both original confirmation paths first leave the active world/session.
	# SessionPanel owns the actual ClientSession stop and Main reacts by clearing
	# the authoritative world/AOI presentation before we return or terminate.
	if _panel!=null:
		_panel.disconnect_session()
	if action==2:
		get_tree().quit()

func set_skin_switch_visible(enabled:bool)->void:
	# Developer-only profile switching must never move or cover the decoded
	# 029_SYSTEM controls in the normal player layout.
	_skin_switch_visible=enabled
	if _settings_skin_label!=null:_settings_skin_label.visible=enabled
	if _settings_picker!=null:_settings_picker.visible=enabled
	if _settings_hint!=null:
		_settings_hint.visible=enabled
		_settings_hint.text="DEV UI" if enabled else "介面：HK 原版"

func bind_session(panel,asset_store:RefCounted=null,world_view=null)->void:
	_panel=panel
	_asset_store=asset_store
	_world_view=world_view
	_equipment_preview.bind_store(_asset_store)
	_inventory_render_initialized=false
	_social_render_signatures.clear()
	_refresh_profiles()

func _refresh_profiles()->void:
	if _settings_picker==null:return
	_settings_picker.clear()
	for profile in UiSkins.available():
		var i:=_settings_picker.item_count
		_settings_picker.add_item(str(profile.name))
		_settings_picker.set_item_metadata(i,str(profile.id))
		if str(profile.id)=="original_ie_hk2":_settings_picker.select(i)

func resize_view(view_size:Vector2)->void:
	if view_size==_last_view:return
	_last_view=view_size
	size=view_size
	var factor:=maxf(0.05,minf(view_size.x/BASE_SIZE.x,view_size.y/BASE_SIZE.y))
	stage.scale=Vector2.ONE*factor
	stage.position=(view_size-BASE_SIZE*factor)*0.5

func set_world_active(active:bool)->void:
	_active=active
	visible=active
	set_process(active)
	if not active:
		_hotkey_refresh_requested=false
		_pending_hotkey_rebind.clear()
		_inventory_render_initialized=false
		_inventory_render_signature.clear()
		_social_render_signatures.clear()
		_close_system_action_question(false)
		_close_party_leave_question(false)
		_party_question.hide()
		_party_question_actor=0
		_request_menu.hide()
		_request_menu_actor=0
		_friend_editor.hide()
		_friend_message_panel.hide()
		_friend_message_actor=0
		_friend_message_index=-1
		_friend_message_auto_latest=true
		_friend_message_last_count=0
		_cancel_player_shop_buy()
		_cancel_player_shop_sell()
		_cancel_player_shop_close_all()
		_close_player_shop_name_dialog()
		for raw_sign in _player_shop_signs.values():
			if is_instance_valid(raw_sign):
				raw_sign.queue_free()
		_player_shop_signs.clear()
		_hide_windows()
		_photos.clear()

func apply_snapshot(snapshot:Dictionary)->void:
	_stats=snapshot.duplicate(true)
	if not _active:return
	if bool(_stats.get("hotkeys_ready",false)):
		_hotkey_refresh_requested=false
	elif (
		bool(_stats.get("world_ready",false)) and
		int(_stats.get("pending_hotkey_sequence",0))==0 and
		not _hotkey_refresh_requested and _panel!=null
	):
		_hotkey_refresh_requested=_panel.submit_hotkey_refresh()
	if int(_stats.get("pending_hotkey_sequence",0))==0:
		_try_submit_pending_hotkey_rebind()
	_refresh_hud()
	_refresh_hotkeys()
	if _inventory_snapshot_changed():
		_refresh_inventory()
	_refresh_mecha_window()
	_refresh_character()
	_refresh_skill()
	if _social_snapshot_changed("party",[
		"party_id","party_members","party_leader_actor_id","pending_party_invite",
		"pending_party_sequence","player_current_hp","player_max_hp","player_current_mp","player_max_mp"]):
		_refresh_party()
	_refresh_clan()
	_refresh_shop()
	_refresh_quest()
	if _social_snapshot_changed("chat",["chat_messages"]):
		_refresh_chat_history()
	if _social_snapshot_changed("friends",["friends","ignored_friends","pending_friend_sequence","pending_friend_invite"]):
		_refresh_friends()
	if _social_snapshot_changed("friend_messages",["friend_messages"]):
		_refresh_friend_message()
	# Party invite uses the same original question family as other social asks;
	# if both arrive together, the server-authoritative party invite takes focus.
	_sync_party_question()
	_refresh_player_shop()
	_refresh_storage()
	_refresh_bank()
	_refresh_repair()
	_refresh_trade()
	_refresh_dialog()

func _refresh_hud()->void:
	var profile_ready:=bool(_stats.get("player_profile_ready",false))
	var hp:=int(_stats.get("player_current_hp",0));var hpmax:=int(_stats.get("player_max_hp",0))
	var mp:=int(_stats.get("player_current_mp",0));var mpmax:=int(_stats.get("player_max_mp",0))
	_level_label.text=str(int(_stats.get("player_level",0))) if profile_ready else ""
	_hp_label.text="%d/%d"%[hp,hpmax] if hpmax>0 else ""
	_mp_label.text="%d/%d"%[mp,mpmax] if mpmax>0 else ""
	_set_progress(_hud_hp_clip,hp,hpmax)
	_set_progress(_hud_mp_clip,mp,mpmax)
	# WP is a separate original OnlineObj value. Never substitute Wisdom or a
	# synthetic number merely to make the green bar look populated.
	if _stats.has("player_current_wp") and _stats.has("player_max_wp"):
		var wp:=int(_stats.player_current_wp);var wpmax:=int(_stats.player_max_wp)
		_wp_label.text="%d/%d"%[wp,wpmax] if wpmax>0 else ""
		_set_progress(_hud_wp_clip,wp,wpmax)
	else:
		_wp_label.text=""
		_set_progress(_hud_wp_clip,0,0)
	# Old CalEXPBar uses PLAYERLVLUP[level-1] and integer percentage math.
	# The Server now publishes that source-backed result; -1 stays fail-closed
	# when no verified progression threshold is loaded.
	if _stats.has("player_experience_percent") and int(_stats.player_experience_percent)>=0:
		var exp_percent:=clampi(int(_stats.player_experience_percent),0,100)
		_exp_percent_label.text="%d%%"%exp_percent
		_set_progress(_hud_exp_clip,exp_percent,100)
	else:
		_exp_percent_label.text=""
		_set_progress(_hud_exp_clip,0,0)
	# HK StatusLite 2094/s04 is the money row beneath WP (original HUD screenshot).
	var money:=int(_stats.get("player_money",-1))
	_status_value_label.text=str(money) if money>=0 else ""
	_animate_status_bars()

func _clear_rows(box:Container)->void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()

func _classic_row(box:VBoxContainer,text:String,height:float,callback:=Callable())->Button:
	var button:=Button.new()
	button.text=text
	button.flat=true
	button.alignment=HORIZONTAL_ALIGNMENT_LEFT
	button.custom_minimum_size.y=height
	button.focus_mode=Control.FOCUS_NONE
	button.add_theme_font_size_override("font_size",10)
	button.add_theme_color_override("font_color",Color("#f9f2d6"))
	if callback.is_valid():button.pressed.connect(callback)
	box.add_child(button)
	return button

func _apply_item_grade_color(button:Button,item_id:int)->void:
	if _panel==null or not _panel.item_catalog.has_name_color_for(item_id):
		return
	var color:Color=_panel.item_catalog.name_color_for(item_id)
	for state in ["font_color","font_hover_color","font_pressed_color","font_focus_color","font_hover_pressed_color"]:
		button.add_theme_color_override(state,color)

func _change_inventory_tab(index:int)->void:
	_inventory_tab=clampi(index,0,4)
	inventory.set_meta("item_scroll",0)
	inventory.set_meta("equip_scroll",0)
	var equip_view:=_inventory_tab==1
	if _inventory_item_bg!=null:_inventory_item_bg.visible=not equip_view
	if _inventory_equip_bg!=null:
		_inventory_equip_bg.visible=equip_view
		if equip_view and _inventory_equip_bg.texture==null:
			_inventory_equip_bg.texture=_tex("BG_Equip")
	_equipment_rows.visible=equip_view
	_equipment_preview.set_occupied(equip_view)
	# Rebuild the VBox before assigning the 022/023 source rect. Godot caches a
	# Container minimum size until children change; sizing first can retain the
	# previous 16-row BG_ITEM minimum (432 px) for one visible frame.
	_refresh_inventory()
	_inventory_rows.position=Vector2(39,313) if equip_view else Vector2(42,62)
	_inventory_rows.size=Vector2(157,189) if equip_view else Vector2(152,432)
	var up:=inventory.get_node_or_null("InventoryScrollUp") as TextureButton
	var down:=inventory.get_node_or_null("InventoryScrollDown") as TextureButton
	if up!=null:up.position=Vector2(204,310) if equip_view else Vector2(204,60)
	if down!=null:down.position=Vector2(204,466) if equip_view else Vector2(204,459)

func _scroll_inventory(delta:int)->void:
	if inventory==null or _inventory_tab==2:
		return
	if _inventory_tab==1:
		inventory.set_meta(
			"equip_scroll",int(inventory.get_meta("equip_scroll",0))+delta)
	else:
		inventory.set_meta(
			"item_scroll",int(inventory.get_meta("item_scroll",0))+delta)
	_refresh_inventory()

func _set_inventory_scroll_buttons(total:int)->int:
	var max_offset:=maxi(0,total-ITEM_ROW_COUNT)
	var offset:=clampi(int(inventory.get_meta("item_scroll",0)),0,max_offset)
	inventory.set_meta("item_scroll",offset)
	var up:=inventory.get_node_or_null("InventoryScrollUp") as TextureButton
	var down:=inventory.get_node_or_null("InventoryScrollDown") as TextureButton
	if up!=null:up.disabled=offset<=0
	if down!=null:down.disabled=offset>=max_offset
	return offset

func _set_equipment_scroll_buttons(total:int)->int:
	var max_offset:=maxi(0,total-7)
	var offset:=clampi(int(inventory.get_meta("equip_scroll",0)),0,max_offset)
	inventory.set_meta("equip_scroll",offset)
	var up:=inventory.get_node_or_null("InventoryScrollUp") as TextureButton
	var down:=inventory.get_node_or_null("InventoryScrollDown") as TextureButton
	if up!=null:up.disabled=offset<=0
	if down!=null:down.disabled=offset>=max_offset
	return offset

func _on_equipment_slot_pressed(slot_id:int)->void:
	if _panel==null or not _equipment_slot_buttons.has(slot_id):
		return
	var button:=_equipment_slot_buttons[slot_id] as TextureButton
	var item_uid:=int(button.get_meta("item_uid",0))
	if item_uid>0:
		_panel.inventory_panel.submit_equipment_unequip(item_uid)

func _refresh_equipment_slots(equipment_items:Dictionary)->void:
	var by_slot:Dictionary={}
	var slot_definitions:Dictionary={}
	for raw_uid in equipment_items:
		var item:Dictionary=equipment_items[raw_uid]
		var slot_id:=int(item.get("slot_id",0))
		if slot_id<1 or slot_id>9:
			continue
		by_slot[slot_id]={"uid":int(raw_uid),"item":item}
		slot_definitions[slot_id]=int(item.get("item_definition_id",0))
	for slot_id in range(1,10):
		var button:=_equipment_slot_buttons.get(slot_id) as TextureButton
		var icon:=_equipment_slot_icons.get(slot_id) as TextureRect
		if button==null or icon==null:
			continue
		var marker:=button.get_node_or_null("EquippedMarker") as TextureRect
		if by_slot.has(slot_id):
			var entry:Dictionary=by_slot[slot_id]
			var uid:=int(entry.uid)
			var item:Dictionary=entry.item
			var definition_id:=int(item.get("item_definition_id",0))
			var item_name:=str(_panel.item_catalog.name_for(definition_id)) if _panel!=null else "Item %d"%definition_id
			button.set_meta("item_uid",uid)
			button.set_meta("item_definition_id",definition_id)
			button.tooltip_text="%s｜%s｜點擊卸下"%[
				str(button.get_meta("slot_name","")),item_name]
			icon.texture=_photos.photo_for_item(definition_id,_panel.item_catalog) if _panel!=null else null
			if marker!=null:marker.show()
		else:
			button.set_meta("item_uid",0)
			button.set_meta("item_definition_id",0)
			button.tooltip_text="%s：未裝備"%str(
				button.get_meta("slot_name",""))
			icon.texture=null
			if marker!=null:marker.hide()
	var base_appearance:Dictionary={}
	var raw_appearance:Variant=_stats.get("selected_character_appearance",{})
	if raw_appearance is Dictionary:
		base_appearance=raw_appearance
	var presented:=AppearanceProfile.presentation_appearance(
		base_appearance,slot_definitions)
	if not presented.is_empty():
		_equipment_preview.set_appearance(presented)

func _inventory_matches_tab(item:Dictionary,equipped:bool)->bool:
	if equipped:
		return _inventory_tab==1
	if _panel==null:
		return _inventory_tab==0
	var category:=str(_panel.item_catalog.category_for(int(item.get("item_definition_id",0))))
	match _inventory_tab:
		0:return category in ["general","recovery","booster",""]
		1:return category in ["equip","weapon","horseequip"]
		2:return category in ["mecha","armor","mechaequip","horseequip"]
		3:return category in ["pet","elfin"]
		4:return category in ["horse","horsebattery","ride"]
	return false

func _classic_item_definition_desc(a:Dictionary,b:Dictionary)->bool:
	# Original Paradise CItemItf::FilterList QuickSorts PlayerItemData with
	# CSGOMap::CompareItem, whose comparator is (b.item_id - a.item_id).
	# Keep equal definitions equivalent instead of inventing a secondary key.
	var a_item:Dictionary=a.get("item",{})
	var b_item:Dictionary=b.get("item",{})
	return int(a_item.get("item_definition_id",0))>int(b_item.get("item_definition_id",0))

func _social_snapshot_changed(section:String,keys:Array)->bool:
	var signature:Dictionary={}
	for key in keys:
		signature[key]=_stats.get(key,null)
	if _social_render_signatures.has(section) and _social_render_signatures[section]==signature:
		return false
	_social_render_signatures[section]=signature.duplicate(true)
	return true

func _inventory_snapshot_changed()->bool:
	# Only Server-owned item/equipment/mecha data determine this panel's rows.
	# Page, tab and scroll controls still call _refresh_inventory directly.
	var signature:Dictionary={
		"inventory_items":_stats.get("inventory_items",{}),
		"equipment_items":_stats.get("equipment_items",{}),
		"selected_character_appearance":_stats.get("selected_character_appearance",{}),
		"mecha_instances":_stats.get("mecha_instances",{}),
		"mecha_equipment_items":_stats.get("mecha_equipment_items",{}),
		"mounted_mecha_uid":_stats.get("mounted_mecha_uid",0)
	}
	if _inventory_render_initialized and signature==_inventory_render_signature:
		return false
	_inventory_render_signature=signature.duplicate(true)
	_inventory_render_initialized=true
	return true

func _refresh_inventory()->void:
	_clear_rows(_inventory_rows)
	if _inventory_tab==1:
		# Apply the 022_equip list viewport after removing 023_ITEM's 16 rows;
		# otherwise VBox minimum-size keeps the old 432px height for this frame.
		_inventory_rows.position=Vector2(39,313)
		_inventory_rows.size=Vector2(157,189)
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var equipment_items:Dictionary=_stats.get("equipment_items",{})
	if _inventory_tab==2:
		_set_inventory_scroll_buttons(0)
		_refresh_classic_mecha_inventory(inventory_items)
		return
	if _inventory_tab==1:
		_refresh_equipment_slots(equipment_items)
		var candidates:Array=[]
		for raw_uid in inventory_items:
			var item:Dictionary=inventory_items[raw_uid]
			if _inventory_matches_tab(item,false):
				candidates.append({"uid":int(raw_uid),"item":item})
		candidates.sort_custom(_classic_item_definition_desc)
		var equip_offset:=_set_equipment_scroll_buttons(candidates.size())
		for i in range(equip_offset,mini(equip_offset+7,candidates.size())):
			var entry:Dictionary=candidates[i]
			var uid:=int(entry.uid)
			var item:Dictionary=entry.item
			var id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
			var row:=_classic_row(
				_inventory_rows,"     %s ×%d"%[name,int(item.get("quantity",1))],
				27.0,_classic_item_action.bind(uid,id,false))
			_apply_item_grade_color(row,id)
			if _panel!=null:
				row.tooltip_text=_panel.item_catalog.tooltip_text_for(item,equipment_items)+"\n點擊送交伺服器判定裝備槽位"
				var photo:=TextureRect.new()
				photo.name="ItemPhoto"
				photo.texture=_photos.photo_for_item(id,_panel.item_catalog)
				photo.position=Vector2(1,1)
				photo.size=Vector2(24,24)
				photo.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
				photo.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
				photo.mouse_filter=Control.MOUSE_FILTER_IGNORE
				row.add_child(photo)
		if candidates.is_empty():
			_classic_row(_inventory_rows,"沒有可穿戴的裝備",27.0)
		return
	var entries:Array=[]
	for raw_uid in inventory_items:
		var item:Dictionary=inventory_items[raw_uid]
		if _inventory_matches_tab(item,false):
			entries.append({"uid":int(raw_uid),"item":item})
	entries.sort_custom(_classic_item_definition_desc)
	var scroll_offset:=_set_inventory_scroll_buttons(entries.size())
	for i in range(scroll_offset,mini(scroll_offset+ITEM_ROW_COUNT,entries.size())):
		var entry:Dictionary=entries[i]
		var uid:=int(entry.uid);var item:Dictionary=entry.item;var id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		var row:=_classic_row(_inventory_rows,"     %s  ×%d"%[name,int(item.get("quantity",1))],27.0)
		_apply_item_grade_color(row,id)
		row.tooltip_text=_panel.item_catalog.tooltip_text_for(item,equipment_items) if _panel!=null else "UID %d · ID %d"%[uid,id]
		_make_item_hotkey_draggable(row,uid,id)
		if _panel!=null:
			var photo:=TextureRect.new();photo.name="ItemPhoto";photo.texture=_photos.photo_for_item(id,_panel.item_catalog)
			photo.position=Vector2(2,0);photo.size=Vector2(24,24);photo.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
			photo.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED;photo.mouse_filter=Control.MOUSE_FILTER_IGNORE
			row.add_child(photo)
		row.pressed.connect(_classic_item_action.bind(uid,id,false))
	if entries.is_empty():
		_classic_row(_inventory_rows,"此分頁目前沒有可用資料",27.0)

func _select_classic_mecha(uid:int)->void:
	_classic_mecha_uid=uid
	_refresh_inventory()
	_refresh_mecha_window()
	_ensure_window_art(_mecha_window)
	_mecha_window.show()

func _classic_mecha_slot_name(slot_id:int)->String:
	match slot_id:
		1:return "武裝"
		2:return "引擎"
		3,4,5:return "強化卡 %d"%(slot_id-2)
		6:return "守護石"
		_:return "槽位 %d"%slot_id

func _submit_classic_mecha_equip(item_uid:int)->void:
	if _panel!=null:_panel.submit_mecha_equip(_classic_mecha_uid,item_uid)

func _submit_classic_mecha_unequip(item_uid:int)->void:
	if _panel!=null:_panel.submit_mecha_unequip(_classic_mecha_uid,item_uid)

func _submit_selected_mecha_mount()->void:
	if _panel!=null and _classic_mecha_uid>0:
		_panel.submit_mecha_mount(_classic_mecha_uid)

func _submit_selected_mecha_dismount()->void:
	if _panel!=null:
		_panel.submit_mecha_dismount()

func _submit_selected_mecha_charge(item_uid:int)->void:
	if _panel!=null and _classic_mecha_uid>0 and item_uid>0:
		_panel.submit_mecha_charge(_classic_mecha_uid,item_uid)

func _on_mecha_slot_pressed(slot_id:int)->void:
	var button:Button=_mecha_slot_buttons.get(slot_id)
	if button==null:return
	var item_uid:=int(button.get_meta("item_uid",0))
	if item_uid>0:
		_submit_classic_mecha_unequip(item_uid)

func _refresh_mecha_window()->void:
	if _mecha_window==null:return
	var mechas:Dictionary=_stats.get("mecha_instances",{})
	var mounted:=int(_stats.get("mounted_mecha_uid",0))
	if _classic_mecha_uid<=0 or not mechas.has(_classic_mecha_uid):
		_classic_mecha_uid=mounted if mounted>0 and mechas.has(mounted) else 0
		if _classic_mecha_uid==0 and not mechas.is_empty():
			var ids:Array=mechas.keys();ids.sort();_classic_mecha_uid=int(ids[0])
	var selected:Dictionary=mechas.get(_classic_mecha_uid,{})
	var definition:=int(selected.get("definition_id",0))
	var meta:Dictionary=_panel.item_catalog.presentation_for(definition) if _panel!=null and definition>0 else {}
	var display_name:=str(_panel.item_catalog.name_for(definition)) if _panel!=null and definition>0 else ""
	var mecha_type:=str(meta.get("horse_type",""))
	var charge:=int(selected.get("battery_charge",0));var capacity:=int(selected.get("battery_capacity",0))
	(_mecha_info_labels.get("name") as RichTextLabel).text=display_name
	(_mecha_info_labels.get("type") as RichTextLabel).text=mecha_type
	(_mecha_info_labels.get("energy") as RichTextLabel).text=("%d/%d"%[charge,capacity]) if capacity>0 else "—"
	(_mecha_info_labels.get("level") as RichTextLabel).text=str(int(selected.get("level",0))) if not selected.is_empty() else ""
	(_mecha_info_labels.get("experience") as RichTextLabel).text=("EXP %d"%int(selected.get("experience",0))) if not selected.is_empty() else ""
	var pending:=int(_stats.get("pending_mecha_sequence",0))!=0
	_mecha_drive_button.visible=not selected.is_empty() and _classic_mecha_uid!=mounted
	_mecha_exit_button.visible=not selected.is_empty() and _classic_mecha_uid==mounted
	_mecha_drive_button.disabled=pending
	_mecha_exit_button.disabled=pending
	var equipped_by_slot:Dictionary={}
	var mecha_equipment:Dictionary=_stats.get("mecha_equipment_items",{})
	for raw_uid in mecha_equipment:
		var item:Dictionary=mecha_equipment[raw_uid]
		if int(item.get("mecha_uid",0))==_classic_mecha_uid:
			equipped_by_slot[int(item.get("slot_id",0))]=item
	for raw_slot in _mecha_slot_buttons:
		var slot_id:=int(raw_slot);var button:Button=_mecha_slot_buttons[raw_slot]
		button.set_meta("item_uid",0)
		if slot_id==0:
			var body_id:=int(meta.get("body_id",0))
			button.text="主體\n%s"%(_panel.item_catalog.name_for(body_id) if _panel!=null and body_id>0 else "—")
			button.tooltip_text="機甲主體來自原始機甲定義；不作為 Client gameplay authority"
			continue
		var equipped:Dictionary=equipped_by_slot.get(slot_id,{})
		if equipped.is_empty():
			button.text=_classic_mecha_slot_name(slot_id)+"\n—"
			button.tooltip_text="未裝備"
			continue
		var item_uid:=int(equipped.get("item_uid",0));var item_definition:=int(equipped.get("item_definition_id",0))
		button.set_meta("item_uid",item_uid)
		button.text=_classic_mecha_slot_name(slot_id)+"\n"+(_panel.item_catalog.name_for(item_definition) if _panel!=null else str(item_definition))
		button.tooltip_text="UID %d｜點擊要求伺服器卸下"%item_uid
	_clear_rows(_mecha_battery_rows)
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var batteries:Array=[]
	if _panel!=null:
		for raw_uid in inventory_items:
			var item:Dictionary=inventory_items[raw_uid]
			var item_definition:=int(item.get("item_definition_id",0))
			if _panel.item_catalog.category_for(item_definition)=="horsebattery":
				batteries.append({"uid":int(raw_uid),"item":item})
	batteries.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.uid)<int(b.uid))
	for i in mini(3,batteries.size()):
		var entry:Dictionary=batteries[i];var item:Dictionary=entry.item
		var item_definition:=int(item.get("item_definition_id",0))
		var row:=_classic_row(_mecha_battery_rows,"＋ %s ×%d"%[
			_panel.item_catalog.name_for(item_definition),int(item.get("quantity",1))],18.0,
			_submit_selected_mecha_charge.bind(int(entry.uid)))
		row.tooltip_text="補充選中機甲能源；相容性與消耗量由伺服器判定"
	if batteries.is_empty():
		_classic_row(_mecha_battery_rows,"背包沒有可用機甲能源",18.0)

func _refresh_classic_mecha_inventory(inventory_items:Dictionary)->void:
	var mechas:Dictionary=_stats.get("mecha_instances",{})
	var mounted:=int(_stats.get("mounted_mecha_uid",0))
	if _classic_mecha_uid<=0 or not mechas.has(_classic_mecha_uid):
		_classic_mecha_uid=mounted if mounted>0 and mechas.has(mounted) else 0
		if _classic_mecha_uid==0 and not mechas.is_empty():
			var ids:Array=mechas.keys();ids.sort();_classic_mecha_uid=int(ids[0])
	_classic_row(_inventory_rows,"更新機甲資料",23.0,func():
		if _panel!=null:_panel.submit_mecha_refresh())
	if mounted>0:
		_classic_row(_inventory_rows,"★ 離開目前機甲 UID %d"%mounted,23.0,func():
			if _panel!=null:_panel.submit_mecha_dismount())
	var ids:Array=mechas.keys();ids.sort()
	for raw_uid in ids:
		if _inventory_rows.get_child_count()>=ITEM_ROW_COUNT:break
		var uid:=int(raw_uid);var mecha:Dictionary=mechas[raw_uid]
		var selected:="▶ " if uid==_classic_mecha_uid else ""
		var mounted_text:="★ " if uid==mounted else ""
		_classic_row(_inventory_rows,"%s%s機甲 %d Lv.%d"%[
			selected,mounted_text,int(mecha.get("definition_id",0)),int(mecha.get("level",0))
		],23.0,_select_classic_mecha.bind(uid)).tooltip_text="UID %d｜EXP %d｜點擊選擇"%[uid,int(mecha.get("experience",0))]
	if mechas.is_empty():
		_classic_row(_inventory_rows,"尚無伺服器機甲資料",23.0)
		return
	if _classic_mecha_uid>0 and _classic_mecha_uid!=mounted and _inventory_rows.get_child_count()<ITEM_ROW_COUNT:
		_classic_row(_inventory_rows,"騎乘選中機甲",23.0,func():
			if _panel!=null:_panel.submit_mecha_mount(_classic_mecha_uid))
	var mecha_equipment:Dictionary=_stats.get("mecha_equipment_items",{})
	var equipped:Array=[]
	for raw_item_uid in mecha_equipment:
		var item:Dictionary=mecha_equipment[raw_item_uid]
		if int(item.get("mecha_uid",0))==_classic_mecha_uid:
			equipped.append(item)
	equipped.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.get("slot_id",0))<int(b.get("slot_id",0)))
	for item in equipped:
		if _inventory_rows.get_child_count()>=ITEM_ROW_COUNT:break
		var item_uid:=int(item.get("item_uid",0));var definition:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(definition)) if _panel!=null else "Item %d"%definition
		var row:=_classic_row(_inventory_rows,"[%s] %s"%[_classic_mecha_slot_name(int(item.get("slot_id",0))),name],23.0,_submit_classic_mecha_unequip.bind(item_uid))
		row.tooltip_text="機甲裝備 UID %d｜點擊要求卸下；核心部件等限制由伺服器判定"%item_uid
	var candidates:Array=[]
	for raw_uid in inventory_items:
		var item:Dictionary=inventory_items[raw_uid]
		if _inventory_matches_tab(item,false):
			candidates.append({"uid":int(raw_uid),"item":item})
	candidates.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.uid)<int(b.uid))
	for entry in candidates:
		if _inventory_rows.get_child_count()>=ITEM_ROW_COUNT:break
		var uid:=int(entry.uid);var item:Dictionary=entry.item;var definition:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(definition)) if _panel!=null else "Item %d"%definition
		var row:=_classic_row(_inventory_rows,"＋ %s ×%d"%[name,int(item.get("quantity",1))],23.0,_submit_classic_mecha_equip.bind(uid))
		_apply_item_grade_color(row,definition)
		row.tooltip_text="背包 UID %d｜點擊送交伺服器判定機甲槽位與相容性"%uid

func _classic_item_action(uid:int,item_id:int,equipped:=false)->void:
	if _panel==null:return
	var category:=str(_panel.item_catalog.category_for(item_id))
	if equipped:
		_panel.inventory_panel.submit_equipment_unequip(uid)
	elif category=="recovery":
		_panel.inventory_panel.submit_recovery_use(uid)
	elif category in ["equip","weapon","horseequip"]:
		_panel.inventory_panel.submit_equipment_equip(uid)

func _refresh_character()->void:
	var profile_ready:=bool(_stats.get("player_profile_ready",false))
	var creation_profile_ready:=bool(_stats.get("player_creation_profile_ready",profile_ready))
	_refresh_character_vital_fill(_character_hp_fill,"hp")
	_refresh_character_vital_fill(_character_mp_fill,"mp")
	var money:=int(_stats.get("player_money",-1))
	var values={
		"名稱":str(_stats.get("character_name",_stats.get("player_name",""))),
		"HP":_character_vital("hp"),"MP":_character_vital("mp"),
		"金錢":str(money) if money>=0 else "—",
	}
	if creation_profile_ready:
		var creation_fields: Dictionary = {
			"力量":"player_strength","速度":"player_dexterity",
			"體質":"player_constitution","智慧":"player_wisdom"
		}
		for field in creation_fields:
			values[field]=str(_stats.get(creation_fields[field],"—"))
	if profile_ready:
		var fields: Dictionary = {
			"等級":"player_level","職業":"player_job"
		}
		for field in fields:
			values[field]=str(_stats.get(fields[field],"—"))
		var verified_stats_mask:=int(_stats.get("player_verified_stats_mask",0))
		# ProfileOnly values are source-backed only while no runtime equipment can
		# contribute an unintegrated modifier. Full verified profiles remain valid.
		if verified_stats_mask != 0x0F:
			var equipped:Dictionary=_stats.get("equipment_items",{}) as Dictionary
			if not bool(_stats.get("equipment_ready",false)) or not equipped.is_empty():
				verified_stats_mask=0
		var verified_fields: Dictionary = {
			"攻擊":["player_attack",1],"防禦":["player_defense",2],
			"命中":["player_hit",4],"迴避":["player_dodge",8]
		}
		for field in verified_fields:
			var spec:Array=verified_fields[field]
			if (verified_stats_mask & int(spec[1])) != 0:
				values[field]=str(_stats.get(str(spec[0]),"—"))
		values["EXP"]="%s EXP"%str(_stats.get("player_experience","—"))
		values["配點"]=str(maxi(0,int(_stats.get("player_available_attribute_points",0))))
	var can_allocate:=profile_ready and int(_stats.get("player_available_attribute_points",0))>0
	for button in _character_point_buttons:
		button.disabled=not can_allocate
	for key in _character_labels:(_character_labels[key] as CharacterText).text=str(values.get(key,"—"))

func _submit_character_attribute(attribute:int)->void:
	if _panel!=null:
		_panel.submit_attribute_allocate(attribute)

func _character_vital(stat:String)->String:
	var maximum:=int(_stats.get("player_max_"+stat,0))
	if maximum <= 0:
		return "—"
	return "%d/%d"%[int(_stats.get("player_current_"+stat,0)),maximum]

func _refresh_character_vital_fill(fill:Control,stat:String)->void:
	var maximum:=int(_stats.get("player_max_"+stat,0))
	var current:=int(_stats.get("player_current_"+stat,0))
	var fraction:=clampf(float(current)/maximum,0.0,1.0) if maximum>0 else 0.0
	fill.size.x=floorf(92.0*fraction)

func _refresh_skill()->void:
	if skill_window==null or not skill_window.has_method("set_skill_book"):return
	var raw_skills:Variant=_stats.get("skill_book",{})
	var skills:Dictionary=raw_skills if raw_skills is Dictionary else {}
	if bool(_stats.get("skill_book_ready",true)):
		skill_window.call("set_skill_book",skills)
	else:
		skill_window.call("set_skill_book",{})

func _party_create()->void:
	if _panel!=null and _panel.session!=null:
		_panel.session.party_create()


func _party_leave()->void:
	if _panel==null or _panel.session==null or int(_stats.get("party_id",0))<=0:
		return
	_party_leave_previous_focus=get_viewport().gui_get_focus_owner()
	_party_leave_question_text.text="決定離開團隊？"
	_party_leave_blocker.show()
	_party_leave_blocker.move_to_front()


func _confirm_party_leave()->void:
	if not _party_leave_blocker.visible or _panel==null or _panel.session==null:
		return
	if _panel.session.party_leave():
		_close_party_leave_question(true)


func _cancel_party_leave()->void:
	_close_party_leave_question(true)


func _close_party_leave_question(restore_focus:bool)->void:
	_party_leave_blocker.hide()
	if (
		restore_focus and _party_leave_previous_focus!=null and
		is_instance_valid(_party_leave_previous_focus) and _party_leave_previous_focus.is_visible_in_tree()
	):
		_party_leave_previous_focus.grab_focus()
	_party_leave_previous_focus=null


func _party_member_pressed(index:int)->void:
	if index<0 or index>=_party_member_actor_ids.size() or _panel==null or _panel.session==null:
		return
	var target:=_party_member_actor_ids[index]
	var self_actor:=int(_stats.get("actor_id",0))
	if target<=0 or target==self_actor:
		return
	if int(_stats.get("party_leader_actor_id",0))!=self_actor:
		return
	_panel.session.party_kick(target)


func _refresh_party()->void:
	if int(_stats.get("party_id",0))<=0 and _party_leave_blocker.visible:
		_close_party_leave_question(false)
	var members:Dictionary=_stats.get("party_members",{})
	# PartyCoordinator preserves authoritative membership order; do not sort by
	# actor id and accidentally move the leader/member rows between snapshots.
	var ids:Array=members.keys()
	for i in range(6):
		var label:=_party_member_labels[i]
		var hp:=_party_hp_clips[i]
		var mp:=_party_mp_clips[i]
		_party_member_actor_ids[i]=0
		label.text=""
		_set_progress(hp,0,0)
		_set_progress(mp,0,0)
		if i>=ids.size():
			continue
		var id:=int(ids[i])
		var member:Dictionary=members[ids[i]]
		_party_member_actor_ids[i]=id
		label.text=str(member.get("name",""))
		# Current PartySnapshot only proves remote identity/generation. Never
		# synthesize peer HP/MP; consume it only if a future authority adds it.
		if id==int(_stats.get("actor_id",0)):
			_set_progress(hp,int(_stats.get("player_current_hp",0)),int(_stats.get("player_max_hp",0)))
			_set_progress(mp,int(_stats.get("player_current_mp",0)),int(_stats.get("player_max_mp",0)))
		elif member.has("current_hp") and member.has("max_hp"):
			_set_progress(hp,int(member.current_hp),int(member.max_hp))
			if member.has("current_mp") and member.has("max_mp"):
				_set_progress(mp,int(member.current_mp),int(member.max_mp))


func _sync_party_question()->void:
	var invite:Dictionary=_stats.get("pending_party_invite",{})
	if invite.is_empty() or int(_stats.get("pending_party_sequence",0))!=0:
		_party_question_actor=0
		_party_question.hide()
		return
	var inviter:=int(invite.get("inviter_actor_id",0))
	if inviter<=0:
		_party_question_actor=0
		_party_question.hide()
		return
	_party_question_actor=inviter
	# The binary proves inviter identity is inserted into localized copy, but
	# the CP950 sentence itself is not recovered. Show only the proven name.
	_party_question_text.text=str(invite.get("inviter_name",""))
	_friend_question.hide()
	_party_question.show()
	_party_question.move_to_front()


func _accept_party_invite()->void:
	if _panel!=null and _panel.session!=null and _panel.session.party_accept():
		_party_question.hide()
		_sync_friend_question()


func _reject_party_invite()->void:
	if _panel!=null and _panel.session!=null and _panel.session.party_reject():
		_party_question.hide()
		_sync_friend_question()


func open_player_request_menu(actor_id:int,viewport_position:Vector2)->void:
	if not _active or _panel==null or _panel.session==null or actor_id<=0 or actor_id==int(_stats.get("actor_id",0)):
		close_player_request_menu()
		return
	var transform:=stage.get_global_transform_with_canvas().affine_inverse()
	var local:=transform*viewport_position
	local.x=clampf(local.x,0.0,BASE_SIZE.x-_request_menu.size.x)
	local.y=clampf(local.y,0.0,BASE_SIZE.y-_request_menu.size.y)
	_request_menu_actor=actor_id
	_request_menu.position=local
	_request_menu.show()
	_request_menu.move_to_front()


func close_player_request_menu()->void:
	_request_menu_actor=0
	_request_menu.hide()


func _request_team()->void:
	var target:=_request_menu_actor
	close_player_request_menu()
	if target>0 and _panel!=null and _panel.session!=null:
		_panel.session.party_invite(target)


func _request_friend()->void:
	var target:=_request_menu_actor
	close_player_request_menu()
	if target>0 and _panel!=null and _panel.session!=null:
		_panel.session.friend_add_actor(target)


func _request_clan()->void:
	var target:=_request_menu_actor
	close_player_request_menu()
	if target>0 and _panel!=null:
		_panel.submit_clan_invite(target)


func _open_clan_ui()->void:
	if int(_stats.get("clan_id",0))<=0:
		if not (_stats.get("pending_clan_invite",{}) as Dictionary).is_empty():
			_sync_clan_question()
			return
		_open_clan_name()
		return
	_hide_windows()
	_ensure_window_art(clan_window)
	clan_window.show()
	if _panel!=null and int(_stats.get("pending_clan_sequence",0))==0:
		_panel.submit_clan_refresh()


func _open_clan_member_window()->void:
	if int(_stats.get("clan_id",0))<=0:
		return
	_clan_member_offset=0
	_clan_selected_actor=0
	_refresh_clan_member_rows()
	_hide_windows()
	_ensure_window_art(_clan_member_window)
	_clan_member_window.show()


func _scroll_clan_members(delta:int)->void:
	var members:Dictionary=_stats.get("clan_members",{})
	var max_offset:=maxi(0,members.size()-13)
	_clan_member_offset=clampi(_clan_member_offset+delta,0,max_offset)
	_refresh_clan_member_rows()


func _select_clan_member(row:int)->void:
	if row<0 or row>=_clan_member_actor_ids.size():
		return
	var actor:=_clan_member_actor_ids[row]
	if actor<=0:
		return
	_clan_selected_actor=actor
	_refresh_clan_member_rows()


func _clan_transfer_selected()->void:
	if (
		_panel==null or _clan_selected_actor<=0 or
		_clan_selected_actor==int(_stats.get("actor_id",0))
	):
		return
	_panel.submit_clan_transfer_leadership(_clan_selected_actor)


func _clan_kick_selected()->void:
	if (
		_panel==null or _clan_selected_actor<=0 or
		_clan_selected_actor==int(_stats.get("actor_id",0))
	):
		return
	_panel.submit_clan_kick(_clan_selected_actor)


func _clan_leave()->void:
	if _panel!=null:
		_panel.submit_clan_leave()


func _open_clan_announcement()->void:
	if int(_stats.get("clan_id",0))<=0:
		return
	var current:=str(_stats.get("clan_announcement",""))
	_clan_current_announcement.text=current
	_clan_announcement_input.text=current
	var leader:=int(_stats.get("clan_leader_actor_id",0))==int(_stats.get("actor_id",0))
	_clan_announcement_input.editable=leader
	_clan_announcement_confirm.visible=leader
	_clan_announcement_changed()
	_hide_windows()
	_ensure_window_art(_clan_announcement_window)
	_clan_announcement_window.show()
	if leader:
		_clan_announcement_input.grab_focus()


func _cancel_clan_announcement()->void:
	_clan_announcement_window.hide()
	_open_clan_ui()


func _clan_announcement_changed()->void:
	if _clan_announcement_remaining==null or _clan_announcement_confirm==null:
		return
	var value:=_clan_announcement_input.text
	var byte_count:=value.to_utf8_buffer().size()
	_clan_announcement_remaining.text=str(maxi(0,WireCodec.CLAN_MAX_ANNOUNCEMENT_BYTES-byte_count))
	var leader:=int(_stats.get("clan_leader_actor_id",0))==int(_stats.get("actor_id",0))
	_clan_announcement_confirm.disabled=(
		not leader or int(_stats.get("pending_clan_sequence",0))!=0 or
		byte_count>WireCodec.CLAN_MAX_ANNOUNCEMENT_BYTES or value!=value.strip_edges()
	)


func _submit_clan_announcement()->void:
	if _panel==null:
		return
	var value:=_clan_announcement_input.text
	if (
		value!=value.strip_edges() or
		value.to_utf8_buffer().size()>WireCodec.CLAN_MAX_ANNOUNCEMENT_BYTES
	):
		return
	if _panel.submit_clan_set_announcement(value):
		_clan_announcement_confirm.disabled=true


func _sync_clan_question()->void:
	var invite:Dictionary=_stats.get("pending_clan_invite",{})
	if (
		invite.is_empty() or int(_stats.get("clan_id",0))!=0 or
		int(_stats.get("pending_clan_sequence",0))!=0
	):
		_clan_question_actor=0
		_clan_question.hide()
		return
	var inviter:=int(invite.get("inviter_actor_id",0))
	if inviter<=0:
		_clan_question.hide()
		return
	_clan_question_actor=inviter
	_clan_question_text.text="加入戰團邀請　%s　%s"%[
		str(invite.get("inviter_name","")),str(invite.get("clan_name",""))]
	_party_question.hide()
	_friend_question.hide()
	_clan_question.show()
	_clan_question.move_to_front()


func _accept_clan_invite()->void:
	var invite:Dictionary=_stats.get("pending_clan_invite",{})
	if _panel==null or invite.is_empty():
		return
	if _panel.submit_clan_join(int(invite.get("clan_id",0))):
		_clan_question.hide()


func _reject_clan_invite()->void:
	if _panel!=null and _panel.submit_clan_reject_invite():
		_clan_question.hide()


func _open_clan_name()->void:
	if (
		int(_stats.get("clan_id",0))!=0 or int(_stats.get("pending_clan_sequence",0))!=0 or
		not (_stats.get("pending_clan_invite",{}) as Dictionary).is_empty()
	):
		return
	_clan_name_input.text=""
	_clan_name_input.caret_column=0
	_update_clan_name_action()
	_clan_name_dialog.show()
	_clan_name_dialog.move_to_front()
	_clan_name_input.focus_editor()


func _close_clan_name()->void:
	_clan_name_dialog.hide()
	_clan_name_input.text=""


func _update_clan_name_action()->void:
	if _clan_name_confirm==null:
		return
	var clean:=_clan_name_input.text.strip_edges()
	_clan_name_confirm.disabled=(
		int(_stats.get("clan_id",0))!=0 or int(_stats.get("pending_clan_sequence",0))!=0 or
		clean!=_clan_name_input.text or clean.length()<2 or clean.length()>10 or
		clean.to_utf8_buffer().size()>WireCodec.CLAN_MAX_NAME_BYTES
	)


func _submit_clan_name()->void:
	if _panel==null or int(_stats.get("clan_id",0))!=0 or int(_stats.get("pending_clan_sequence",0))!=0:
		return
	var clean:=_clan_name_input.text.strip_edges()
	if (
		clean!=_clan_name_input.text or clean.length()<2 or clean.length()>10 or
		clean.to_utf8_buffer().size()>WireCodec.CLAN_MAX_NAME_BYTES
	):
		return
	if _panel.submit_clan_create(clean):
		# Keep the dialog present until Server result/snapshot. This prevents a
		# locally queued request from looking authoritative before SQLite accepts it.
		_clan_name_confirm.disabled=true


func _refresh_clan()->void:
	var clan_id:=int(_stats.get("clan_id",0))
	var pending:=int(_stats.get("pending_clan_sequence",0))
	if clan_id>0 and _clan_name_dialog.visible:
		_close_clan_name()
		_hide_windows()
		_ensure_window_art(clan_window)
		clan_window.show()
	elif _clan_name_dialog.visible:
		_update_clan_name_action()
	if _clan_name_value!=null:
		_clan_name_value.text=str(_stats.get("clan_name","")) if clan_id>0 else ""
	var members:Dictionary=_stats.get("clan_members",{})
	if _clan_member_count_value!=null:
		_clan_member_count_value.text=str(members.size()) if clan_id>0 else ""
	var actor:=int(_stats.get("actor_id",0))
	var leader:=int(_stats.get("clan_leader_actor_id",0))
	var is_leader:=clan_id>0 and actor>0 and leader==actor
	var selected_valid:=(
		_clan_selected_actor>0 and _clan_selected_actor!=actor and
		members.has(_clan_selected_actor)
	)
	var transfer:=clan_window.get_node_or_null("ClanTransfer") as TextureButton
	var kick:=clan_window.get_node_or_null("ClanKick") as TextureButton
	var news:=clan_window.get_node_or_null("ClanNews") as TextureButton
	var leave:=clan_window.get_node_or_null("ClanLeave") as TextureButton
	var info:=clan_window.get_node_or_null("ClanInfo") as TextureButton
	if transfer!=null:transfer.disabled=not is_leader or not selected_valid or pending!=0
	if kick!=null:kick.disabled=not is_leader or not selected_valid or pending!=0
	if news!=null:news.disabled=clan_id<=0 or pending!=0
	if leave!=null:leave.disabled=clan_id<=0 or pending!=0
	if info!=null:info.disabled=clan_id<=0
	if clan_id<=0:
		_clan_selected_actor=0
		if clan_window.visible:clan_window.hide()
		if _clan_member_window!=null:_clan_member_window.hide()
		if _clan_announcement_window!=null:_clan_announcement_window.hide()
	_refresh_clan_member_rows()
	if _clan_current_announcement!=null:
		_clan_current_announcement.text=str(_stats.get("clan_announcement",""))
	if _clan_announcement_window!=null and _clan_announcement_window.visible:
		_clan_announcement_changed()
	_sync_clan_question()


func _refresh_clan_member_rows()->void:
	if _clan_member_name_labels.is_empty():
		return
	var members:Dictionary=_stats.get("clan_members",{})
	var ids:Array=members.keys()
	ids.sort()
	if _clan_selected_actor>0 and not members.has(_clan_selected_actor):
		_clan_selected_actor=0
	var max_offset:=maxi(0,ids.size()-13)
	_clan_member_offset=clampi(_clan_member_offset,0,max_offset)
	for i in range(13):
		var idx:=_clan_member_offset+i
		var state_label:=_clan_member_state_labels[i]
		var name_label:=_clan_member_name_labels[i]
		var role_label:=_clan_member_role_labels[i]
		if idx>=ids.size():
			_clan_member_actor_ids[i]=0
			state_label.text=""
			name_label.text=""
			role_label.text=""
			name_label.modulate=Color.WHITE
			role_label.modulate=Color.WHITE
			continue
		var raw_id=ids[idx]
		var member:Dictionary=members[raw_id]
		var member_id:=int(raw_id)
		var online:=bool(member.get("online",false))
		_clan_member_actor_ids[i]=member_id
		state_label.text="上" if online else "離"
		name_label.text=str(member.get("name","玩家"))
		role_label.text="會長" if int(member.get("role",0))==1 else "會員"
		var row_alpha:=1.0 if online else 0.55
		name_label.modulate=Color(1.0,1.0,1.0,row_alpha)
		role_label.modulate=Color(1.0,1.0,1.0,row_alpha)
		state_label.modulate=Color(1.0,1.0,1.0,row_alpha)
		if member_id==_clan_selected_actor:
			name_label.add_theme_color_override("default_color",Color("#ffe073"))
		else:
			name_label.add_theme_color_override("default_color",Color("#f9f2d6"))
	var up:=_clan_member_window.get_node_or_null("MemberScrollUp") as TextureButton
	var down:=_clan_member_window.get_node_or_null("MemberScrollDown") as TextureButton
	if up!=null:up.disabled=_clan_member_offset<=0
	if down!=null:down.disabled=_clan_member_offset>=max_offset

func _refresh_shop()->void:
	_clear_rows(_shop_buy_rows);_clear_rows(_shop_sell_rows)
	var shop:Dictionary=_stats.get("shop_items",{})
	var ids:Array=shop.keys();ids.sort()
	for i in mini(17,ids.size()):
		var id:=int(ids[i]);var item:Dictionary=shop[ids[i]]
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		_classic_row(_shop_buy_rows,"%s　$%d"%[name,int(item.get("buy_price",0))],24.3,func():
			if _panel!=null:_panel.submit_shop_buy(id,1))
	var inv:Dictionary=_stats.get("inventory_items",{})
	var uids:Array=inv.keys();uids.sort();var count:int=0
	for raw in uids:
		if count>=17:break
		var uid:=int(raw);var owned:Dictionary=inv[raw];var id:=int(owned.get("item_definition_id",0))
		if not shop.has(id) or int((shop[id] as Dictionary).get("sell_price",0))<=0:continue
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		_classic_row(_shop_sell_rows,"%s ×%d"%[name,int(owned.get("quantity",0))],24.3,func():
			if _panel!=null:_panel.submit_shop_sell(uid,1))
		count+=1

func _refresh_quest()->void:
	_clear_rows(_quest_choices)
	var quest:Dictionary=_stats.get("quest_view",{})
	_quest_title.text=str(quest.get("title",""))
	_quest_text.text=str(quest.get("text",""))
	for raw in quest.get("choices",[]):
		var choice:Dictionary=raw;var id:=int(choice.get("choice_id",0))
		_classic_row(_quest_choices,str(choice.get("label","選項")),22.0,func():
			if _panel!=null:_panel.submit_quest_choice(id))

func _send_chat()->void:
	if _panel==null or _panel.session==null:
		return
	var message:=_chat_input.text.strip_edges()
	if message.is_empty():
		return
	var queued:=false
	# Old CChatItf::ProcessPrivateMsg parses private chat before the normal
	# channel route. /name targets the first space-delimited character name;
	# /uin targets the numeric player identifier. Keep these commands in the
	# original 016/017 input instead of inventing a separate whisper widget.
	if message.begins_with("/name "):
		var payload:=message.substr(6)
		var split:=payload.find(" ")
		if split<=0 or split>=payload.length()-1:
			return
		var target_name:=payload.left(split).strip_edges()
		var private_message:=payload.substr(split+1).strip_edges()
		if target_name.is_empty() or private_message.is_empty():
			return
		queued=_panel.session.send_whisper_name(target_name,private_message,_chat_bubble_type)
	elif message.begins_with("/uin "):
		var payload:=message.substr(5)
		var split:=payload.find(" ")
		if split<=0 or split>=payload.length()-1:
			return
		var target_text:=payload.left(split).strip_edges()
		var private_message:=payload.substr(split+1).strip_edges()
		if not target_text.is_valid_int() or int(target_text)<=0 or private_message.is_empty():
			return
		queued=_panel.session.send_whisper(int(target_text),private_message,_chat_bubble_type)
	else:
		match _chat_send_channel:
			0: queued=_panel.session.send_world_chat(message,_chat_bubble_type)
			2: queued=_panel.session.send_warband_chat(message,_chat_bubble_type)
			3: queued=_panel.session.send_party_chat(message,_chat_bubble_type)
			4: queued=_panel.session.send_nearby_chat(message,_chat_bubble_type)
			_: return
	if queued:
		_chat_input.clear()

func _entity_name(actor_id:int,value:Dictionary)->String:
	var name:=str(value.get("name",value.get("character_name","")))
	return name if not name.is_empty() else "玩家 %d"%actor_id

func _refresh_chat_and_friends()->void:
	_refresh_chat_history()
	_refresh_friends()

func _refresh_chat_history()->void:
	var messages:Array=_stats.get("chat_messages",[])
	var rows:=PackedStringArray()
	var start:=maxi(0,messages.size()-30)
	for i in range(start,messages.size()):
		var entry:Dictionary=messages[i]
		var channel:=int(entry.get("channel",-1))
		var sender:=str(entry.get("sender_name","玩家 %d"%int(entry.get("sender_actor_id",0))))
		match channel:
			0:
				if _chat_show_nearby:rows.append("[附近] %s：%s"%[sender,str(entry.get("message",""))])
			1:
				if _chat_show_world:rows.append("[世界] %s：%s"%[sender,str(entry.get("message",""))])
			2:
				if _chat_show_warband:rows.append("[戰團] %s：%s"%[sender,str(entry.get("message",""))])
			3:
				if _chat_show_party:rows.append("[隊伍] %s：%s"%[sender,str(entry.get("message",""))])
			4:
				if not _chat_show_private:continue
				if str(entry.get("direction",""))=="outgoing":
					rows.append("[私訊] 你 → %s：%s"%[str(entry.get("target_name","")),str(entry.get("message",""))])
				else:
					rows.append("[私訊] %s → 你：%s"%[sender,str(entry.get("message",""))])
	var text:="\n".join(rows)
	if text!=_chat_history.text:
		var scroll:=_chat_history.get_v_scroll_bar()
		var at_end:=scroll.value>=maxf(scroll.min_value,scroll.max_value-scroll.page)-0.01
		var previous:=_chat_history.get_v_scroll()
		_chat_history.text=text
		_chat_history.set_v_scroll(_chat_history.get_line_count() if at_end else previous)
	_update_chat_scroll_buttons.call_deferred()

func _refresh_friends()->void:
	_clear_rows(_friend_rows)
	var entries:Dictionary=_stats.get("ignored_friends",{}) if _friend_show_ignored else _stats.get("friends",{})
	if _friend_selected_actor>0 and not entries.has(_friend_selected_actor):
		_friend_selected_actor=0
	var ordered:Array=entries.keys()
	ordered.sort_custom(func(a,b):
		var left:Dictionary=entries[a]
		var right:Dictionary=entries[b]
		if not _friend_show_ignored:
			var lo:=bool(left.get("online",false))
			var ro:=bool(right.get("online",false))
			if lo!=ro:return lo and not ro
		return str(left.get("name",""))<str(right.get("name","")))
	_friend_ordered_actors.clear()
	if not _friend_show_ignored:
		for raw_actor in ordered:
			_friend_ordered_actors.append(int(raw_actor))
	for raw_actor in ordered:
		var actor_id:=int(raw_actor)
		var entry:Dictionary=entries[raw_actor]
		var friend_name:=str(entry.get("name","玩家 %d"%actor_id))
		var prefix:="" if _friend_show_ignored else ("● " if bool(entry.get("online",false)) else "○ ")
		_style_friend_row(_classic_row(
			_friend_rows,prefix+friend_name,15.0,_select_friend.bind(actor_id,friend_name)))
	if ordered.is_empty():
		_style_friend_row(_classic_row(
			_friend_rows,"黑名單為空" if _friend_show_ignored else "尚無好友",15.0))
	_update_friend_action()
	_sync_friend_question()
	_refresh_friend_message()
	_update_friend_scroll_buttons.call_deferred()
	_update_friend_mail_slots()


func _sync_friend_question()->void:
	var invite:Dictionary=_stats.get("pending_friend_invite",{})
	if invite.is_empty():
		_friend_question_actor=0
		_friend_question.visible=false
		return
	if int(_stats.get("pending_friend_sequence",0))!=0:
		_friend_question.visible=false
		return
	var peer_actor:=int(invite.get("actor_id",0))
	if peer_actor<=0:
		_friend_question.visible=false
		return
	var peer_name:=str(invite.get("name",""))
	if peer_actor!=_friend_question_actor or not _friend_question.visible:
		_friend_question_actor=peer_actor
		# The Old binary proves the question contains the inviter identity but its
		# encoded localized sentence has not been recovered. Do not invent that copy.
		_friend_question_text.text=peer_name
		_friend_question.visible=true
		_friend_question.move_to_front()


func _accept_friend_invite()->void:
	if _panel==null or _panel.session==null:return
	if _panel.session.friend_accept():
		_friend_question.visible=false


func _reject_friend_invite()->void:
	if _panel==null or _panel.session==null:return
	if _panel.session.friend_reject():
		_friend_question.visible=false


func _friend_actor_for_name(target_name:String)->int:
	var clean:=target_name.strip_edges()
	if clean.is_empty():return 0
	var friends:Dictionary=_stats.get("friends",{})
	for raw_actor in friends:
		var entry:Dictionary=friends[raw_actor]
		if str(entry.get("name",""))==clean:
			return int(raw_actor)
	return 0


func _select_friend(actor_id:int,friend_name:String)->void:
	_friend_selected_actor=actor_id
	_friend_name_input.text=friend_name
	_friend_name_input.caret_column=friend_name.length()
	_update_friend_action()


func _update_friend_action()->void:
	var clean:=_friend_name_input.text.strip_edges()
	var actor:=_friend_actor_for_name(clean)
	var pending:=int(_stats.get("pending_friend_sequence",0))!=0
	_friend_action.text="移除" if actor>0 else "新增"
	_friend_action.tooltip_text="移除好友" if actor>0 else "新增好友"
	_friend_action.disabled=clean.is_empty() or pending
	var friend_add:=friend_panel.get_node_or_null("FriendAdd") as TextureButton
	if friend_add!=null:
		friend_add.disabled=not _friend_show_ignored or _friend_selected_actor<=0 or pending
		friend_add.tooltip_text="還原好友"
	if _friend_delete!=null:
		_friend_delete.disabled=_friend_show_ignored or _friend_selected_actor<=0 or pending
	if _friend_ignore!=null:
		_friend_ignore.disabled=_friend_show_ignored or _friend_selected_actor<=0 or pending
		_friend_ignore.tooltip_text="移至黑名單"
	if _friend_edit!=null:
		_friend_edit.disabled=_friend_selected_actor<=0 or pending
	_update_friend_rename_action()

func _friend_action_pressed()->void:
	if _panel==null or _panel.session==null:return
	var clean:=_friend_name_input.text.strip_edges()
	if clean.is_empty():return
	var actor:=_friend_actor_for_name(clean)
	var queued:bool=_panel.session.friend_remove(actor) if actor>0 else _panel.session.friend_add_name(clean)
	if queued:
		_friend_name_input.clear()
		_friend_editor.hide()
		_update_friend_action()


func _open_player_shop_ui()->void:
	var show:=not player_shop_window.visible
	_hide_windows()
	if not show:
		return
	_ensure_window_art(player_shop_window)
	player_shop_window.show()
	var actor_id:=int(_stats.get("actor_id",0))
	var presentations:Dictionary=_stats.get("player_shop_presentations",{})
	if actor_id>0 and not presentations.has(actor_id):
		_open_player_shop_name_dialog()


func _open_player_shop_name_dialog()->void:
	if _panel==null or _panel.session==null:
		return
	var actor_id:=int(_stats.get("actor_id",0))
	var presentations:Dictionary=_stats.get("player_shop_presentations",{})
	var current:Dictionary=presentations.get(actor_id,{})
	_player_shop_name_input.text=str(current.get("shop_name",""))
	_update_player_shop_name_action()
	_player_shop_name_dialog.show()
	_player_shop_name_input.focus_editor()
	_player_shop_name_input.caret_column=_player_shop_name_input.text.length()


func _close_player_shop_name_dialog()->void:
	_player_shop_name_dialog.hide()
	_player_shop_name_input.release_editor_focus()


func _update_player_shop_name_action()->void:
	if _player_shop_name_confirm==null:
		return
	var clean:=_player_shop_name_input.text.strip_edges()
	_player_shop_name_confirm.disabled=(
		clean.is_empty() or clean.length()>20 or
		clean.to_utf8_buffer().size()>WireCodec.PLAYER_SHOP_MAX_NAME_BYTES or
		int(_stats.get("pending_player_shop_sequence",0))!=0)


func _submit_player_shop_name()->void:
	if _player_shop_name_confirm==null or _player_shop_name_confirm.disabled:
		return
	if _panel==null or _panel.session==null:
		return
	var clean:=_player_shop_name_input.text.strip_edges()
	if _panel.session.set_player_shop_name(clean):
		_update_player_shop_name_action()


func _decimal_value(text:String)->int:
	var clean:=text.strip_edges()
	if clean.is_empty():
		return 0
	for index in clean.length():
		var code:=clean.unicode_at(index)
		if code<48 or code>57:
			return 0
	return int(clean)


func _open_player_shop_item(uid:int)->void:
	var inventory:Dictionary=_stats.get("inventory_items",{})
	if uid<=0 or not inventory.has(uid) or int(_stats.get("pending_player_shop_sequence",0))!=0:
		return
	_player_shop_sell_uid=uid
	_player_shop_sell_price_value=0
	_player_shop_sell_price_input.text="1"
	_update_player_shop_sell_price_action()
	_player_shop_sell_price_dialog.show()
	_player_shop_sell_price_input.focus_editor()
	_player_shop_sell_price_input.select_all()


func _update_player_shop_sell_price_action()->void:
	var button:=_player_shop_sell_price_dialog.get_node_or_null("PlayerShopSellPriceOk") as BaseButton
	if button!=null:
		var price:=_decimal_value(_player_shop_sell_price_input.text)
		button.disabled=price<=0 or price>WireCodec.MAX_MONEY


func _confirm_player_shop_sell_price()->void:
	var price:=_decimal_value(_player_shop_sell_price_input.text)
	if price<=0 or price>WireCodec.MAX_MONEY:
		return
	var inventory:Dictionary=_stats.get("inventory_items",{})
	if _player_shop_sell_uid<=0 or not inventory.has(_player_shop_sell_uid):
		_cancel_player_shop_sell()
		return
	var owned:=int((inventory[_player_shop_sell_uid] as Dictionary).get("quantity",0))
	if owned<=0:
		_cancel_player_shop_sell()
		return
	_player_shop_sell_price_value=price
	_player_shop_sell_price_dialog.hide()
	_player_shop_sell_quantity_input.text=str(owned)
	_update_player_shop_sell_quantity_action()
	_player_shop_sell_quantity_dialog.show()
	_player_shop_sell_quantity_input.focus_editor()
	_player_shop_sell_quantity_input.select_all()


func _update_player_shop_sell_quantity_action()->void:
	var button:=_player_shop_sell_quantity_dialog.get_node_or_null("PlayerShopSellQuantityOk") as BaseButton
	if button==null:
		return
	var inventory:Dictionary=_stats.get("inventory_items",{})
	var owned:=0
	if inventory.has(_player_shop_sell_uid):
		owned=int((inventory[_player_shop_sell_uid] as Dictionary).get("quantity",0))
	var quantity:=_decimal_value(_player_shop_sell_quantity_input.text)
	button.disabled=quantity<=0 or quantity>owned or _player_shop_sell_price_value<=0


func _confirm_player_shop_sell_quantity()->void:
	if _panel==null or _panel.session==null:
		return
	var inventory:Dictionary=_stats.get("inventory_items",{})
	if not inventory.has(_player_shop_sell_uid):
		_cancel_player_shop_sell()
		return
	var owned:=int((inventory[_player_shop_sell_uid] as Dictionary).get("quantity",0))
	var quantity:=_decimal_value(_player_shop_sell_quantity_input.text)
	if quantity<=0 or quantity>owned or _player_shop_sell_price_value<=0:
		return
	if _panel.session.open_player_shop(_player_shop_sell_uid,_player_shop_sell_price_value,quantity):
		_cancel_player_shop_sell()


func _cancel_player_shop_sell()->void:
	_player_shop_sell_price_dialog.hide()
	_player_shop_sell_quantity_dialog.hide()
	_player_shop_sell_price_input.release_editor_focus()
	_player_shop_sell_quantity_input.release_editor_focus()
	_player_shop_sell_uid=0
	_player_shop_sell_price_value=0


func _inspect_player_shop(actor_id:int)->void:
	if _panel==null or _panel.session==null or actor_id<=0:
		return
	_hide_windows()
	_ensure_window_art(player_shop_window)
	player_shop_window.show()
	_panel.session.inspect_player_shop(actor_id)


func _close_player_shop_listing(listing_id:int)->void:
	if _panel!=null and _panel.session!=null and listing_id>0:
		_panel.session.close_player_shop(listing_id)


func _buy_player_shop_listing(seller_actor_id:int,listing_id:int)->void:
	if seller_actor_id<=0 or listing_id<=0 or int(_stats.get("pending_player_shop_sequence",0))!=0:
		return
	var listings:Dictionary=_stats.get("player_shop_inspected_listings",{})
	var listing:Dictionary=listings.get(listing_id,{})
	if listing.is_empty():
		var fallback:Dictionary=_stats.get("player_shop_inspected",{})
		if int(fallback.get("listing_id",0))==listing_id:
			listing=fallback
	if listing.is_empty() or int(listing.get("seller_actor_id",0))!=seller_actor_id:
		return
	_player_shop_buy_seller=seller_actor_id
	_player_shop_buy_listing=listing_id
	_player_shop_buy_quantity.text=str(int(listing.get("quantity",0)))
	_player_shop_buy_total.text=str(int(listing.get("price",0)))
	_player_shop_buy_dialog.show()


func _confirm_player_shop_buy()->void:
	if _panel!=null and _panel.session!=null and _player_shop_buy_seller>0 and _player_shop_buy_listing>0:
		if _panel.session.buy_player_shop(_player_shop_buy_seller,_player_shop_buy_listing):
			_cancel_player_shop_buy()


func _cancel_player_shop_buy()->void:
	_player_shop_buy_dialog.hide()
	_player_shop_buy_seller=0
	_player_shop_buy_listing=0


func _open_player_shop_close_question()->void:
	var actor_id:=int(_stats.get("actor_id",0))
	var presentations:Dictionary=_stats.get("player_shop_presentations",{})
	if actor_id<=0 or not presentations.has(actor_id):
		return
	_player_shop_close_question_text.text="確定要關閉小商店？未售出的商品會回到背包。"
	_player_shop_close_question.show()


func _confirm_player_shop_close_all()->void:
	if _panel!=null and _panel.session!=null and _panel.session.close_player_shop_all():
		_player_shop_close_question.hide()


func _cancel_player_shop_close_all()->void:
	_player_shop_close_question.hide()


func _refresh_player_shop()->void:
	_clear_rows(_player_shop_rows)
	_clear_rows(_player_shop_inventory_rows)
	var actor_id:=int(_stats.get("actor_id",0))
	var presentations:Dictionary=_stats.get("player_shop_presentations",{})
	var own_presentation:Dictionary=presentations.get(actor_id,{})
	var own_name:=str(own_presentation.get("shop_name",""))
	_player_shop_name_label.text=own_name if not own_name.is_empty() else "尚未開設"
	var pending:=int(_stats.get("pending_player_shop_sequence",0))!=0
	_player_shop_buy.disabled=own_presentation.is_empty() or pending
	_player_shop_action_text.text=(
		"點右側背包商品上架；點自己的發售商品可取消。" if not own_presentation.is_empty()
		else "先設定小商店名稱，才能上架商品。")
	if not own_presentation.is_empty() and _player_shop_name_dialog.visible and not pending:
		_player_shop_name_dialog.hide()

	var own:Dictionary=_stats.get("player_shop_own_listing",{})
	var own_listings:Dictionary=_stats.get("player_shop_own_listings",{})
	if own_listings.is_empty() and not own.is_empty():
		own_listings[int(own.get("listing_id",0))]=own
	var own_ids:Array=own_listings.keys();own_ids.sort()
	for raw_listing_id in own_ids:
		var listing_id:=int(raw_listing_id)
		var listing:Dictionary=own_listings[raw_listing_id]
		var item_id:=int(listing.get("item_definition_id",0))
		var item_name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_player_shop_rows,"%s ×%d　$%d"%[
			item_name,int(listing.get("quantity",0)),int(listing.get("price",0))],
			25.0,_close_player_shop_listing.bind(listing_id))

	var inspected:Dictionary=_stats.get("player_shop_inspected",{})
	var inspected_listings:Dictionary=_stats.get("player_shop_inspected_listings",{})
	if inspected_listings.is_empty() and not inspected.is_empty():
		inspected_listings[int(inspected.get("listing_id",0))]=inspected
	if not inspected_listings.is_empty() and own_listings.is_empty():
		var ids:Array=inspected_listings.keys();ids.sort()
		for raw_listing_id in ids:
			var listing_id:=int(raw_listing_id)
			var listing:Dictionary=inspected_listings[raw_listing_id]
			var item_id:=int(listing.get("item_definition_id",0))
			var item_name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
			_classic_row(_player_shop_rows,"%s ×%d　$%d"%[
				item_name,int(listing.get("quantity",0)),int(listing.get("price",0))],
				25.0,_buy_player_shop_listing.bind(int(listing.get("seller_actor_id",0)),listing_id))
		_player_shop_action_text.text="點商品後確認購買；交易由伺服器核准。"

	if not own_presentation.is_empty() and own_listings.size()<16:
		var inventory:Dictionary=_stats.get("inventory_items",{})
		var uids:Array=inventory.keys();uids.sort()
		for index in mini(16,uids.size()):
			var uid:=int(uids[index])
			var item:Dictionary=inventory[uids[index]]
			var item_id:=int(item.get("item_definition_id",0))
			var item_name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
			_classic_row(_player_shop_inventory_rows,"%s ×%d"%[
				item_name,int(item.get("quantity",0))],25.0,_open_player_shop_item.bind(uid))
	elif own_presentation.is_empty():
		_classic_row(_player_shop_inventory_rows,"請先設定小商店名稱",25.0)


func _player_shop_actor_world_position(actor_id:int)->Vector2:
	if actor_id==int(_stats.get("actor_id",0)):
		return Vector2(float(_stats.get("x",0.0)),float(_stats.get("y",0.0)))
	var entities:Dictionary=_stats.get("entities",{})
	if not entities.has(actor_id):
		return Vector2.INF
	var entity:Dictionary=entities[actor_id]
	return Vector2(float(entity.get("x",0.0)),float(entity.get("y",0.0)))


func _refresh_player_shop_signs()->void:
	if not _active or _world_view==null or not is_instance_valid(_world_view):
		for raw in _player_shop_signs.values():
			if is_instance_valid(raw):raw.hide()
		return
	var presentations:Dictionary=_stats.get("player_shop_presentations",{})
	var live:Dictionary={}
	for raw_actor in presentations.keys():
		var actor_id:=int(raw_actor)
		var presentation:Dictionary=presentations[raw_actor]
		if actor_id<=0 or not bool(presentation.get("present",true)):
			continue
		var world_pos:=_player_shop_actor_world_position(actor_id)
		if not world_pos.is_finite():
			continue
		live[actor_id]=true
		var sign:Control=_player_shop_signs.get(actor_id,null)
		if sign==null or not is_instance_valid(sign):
			sign=Control.new()
			sign.name="PlayerShopSign_%d"%actor_id
			sign.size=Vector2(119,32)
			sign.mouse_filter=Control.MOUSE_FILTER_PASS
			_player_shop_sign_layer.add_child(sign)
			var button:=TextureButton.new()
			button.name="Hit"
			button.size=Vector2(119,32)
			button.ignore_texture_size=true
			button.stretch_mode=TextureButton.STRETCH_SCALE
			button.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
			button.texture_normal=_tex("BG_PSHOP_03")
			button.texture_hover=button.texture_normal
			button.texture_pressed=button.texture_normal
			var sign_actor_id:=actor_id
			button.pressed.connect(
				_open_player_shop_ui if sign_actor_id==int(_stats.get("actor_id",0))
				else _inspect_player_shop.bind(sign_actor_id))
			sign.add_child(button)
			var label:=_classic_label(sign,"Name",Rect2(4,2,109,18),10,true)
			label.clip_text=true
			_player_shop_signs[actor_id]=sign
		var label:=sign.get_node_or_null("Name") as Label
		if label!=null:
			label.text=str(presentation.get("shop_name",""))
		var screen:Vector2=_world_view.camera.world_to_screen(world_pos)
		var scale_factor:=maxf(0.05,stage.scale.x)
		var local:Vector2=(screen-stage.position)/scale_factor
		sign.position=local+Vector2(-59.5,-52.0)
		sign.visible=Rect2(Vector2(-119,-32),BASE_SIZE+Vector2(238,64)).has_point(sign.position)
	for raw_actor in _player_shop_signs.keys():
		var actor_id:=int(raw_actor)
		if live.has(actor_id):
			continue
		var sign:Control=_player_shop_signs[raw_actor]
		_player_shop_signs.erase(raw_actor)
		if is_instance_valid(sign):sign.queue_free()

func _refresh_storage()->void:
	_clear_rows(_storage_rows)
	_clear_rows(_storage_inventory_rows)
	var ready:=bool(_stats.get("storage_ready",false))
	var active_npc:=int(_stats.get("active_storage_npc",0))
	if not ready or active_npc<=0:
		_classic_row(_storage_rows,"請先與倉庫 NPC 互動",24.4)
		_classic_row(_storage_inventory_rows,"倉庫尚未開啟",24.4)
		return
	var storage:Dictionary=_stats.get("storage_items",{})
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var stored_uids:Array=storage.keys();stored_uids.sort()
	for i in mini(17,stored_uids.size()):
		var uid:=int(stored_uids[i]);var item:Dictionary=storage[stored_uids[i]]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_storage_rows,"%s ×%d"%[name,int(item.get("quantity",0))],24.4,func():
			if _panel!=null:_panel.submit_storage_withdraw(uid))
	if stored_uids.is_empty():_classic_row(_storage_rows,"倉庫目前沒有物品",24.4)
	var inv_uids:Array=inventory_items.keys();inv_uids.sort()
	for i in mini(17,inv_uids.size()):
		var uid:=int(inv_uids[i]);var item:Dictionary=inventory_items[inv_uids[i]]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_storage_inventory_rows,"%s ×%d"%[name,int(item.get("quantity",0))],24.4,func():
			if _panel!=null:_panel.submit_storage_deposit(uid))
	if inv_uids.is_empty():_classic_row(_storage_inventory_rows,"背包目前沒有物品",24.4)

func _refresh_bank()->void:
	var active_npc:=int(_stats.get("active_bank_npc",0))
	if active_npc<=0:
		_bank_status.text="請先與銀行 NPC 互動"
		_bank_amount.editable=false
		return
	var scope:=int(_stats.get("bank_scope",0))
	var wallet:=int(_stats.get("bank_wallet_money",0))
	var balance:=int(_stats.get("bank_money",0))
	_bank_status.text="錢包 %d　銀行 %d　%s"%[wallet,balance,"帳號共用" if scope==2 else "角色專屬"]
	_bank_amount.max_value=maxi(1,maxi(wallet,balance))
	_bank_amount.editable=int(_stats.get("pending_bank_sequence",0))==0

func _refresh_repair()->void:
	_clear_rows(_repair_rows)
	if not bool(_stats.get("repair_ready",false)) or int(_stats.get("active_repair_npc",0))<=0:
		_classic_row(_repair_rows,"請先與修理 NPC 互動",24.0)
		return
	var items:Dictionary=_stats.get("repair_items",{})
	var uids:Array=items.keys();uids.sort()
	for raw_uid in uids:
		var uid:=int(raw_uid);var item:Dictionary=items[raw_uid]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_repair_rows,"%s　%d/%d　$%d"%[
			name,int(item.get("current_durability",0)),int(item.get("max_durability",0)),int(item.get("cost",0))],
			24.0,func():if _panel!=null:_panel.submit_repair_item(uid))
	if uids.is_empty():
		_classic_row(_repair_rows,"沒有需要修理的裝備",24.0)

func _trade_state_text(state:int)->String:
	match state:
		1:return "等待接受"
		3:return "交易進行中"
		4:return "等待最終確認"
		_:return "未交易"

func _refresh_trade()->void:
	_clear_rows(_trade_rows)
	var trade_id:=int(_stats.get("trade_id",0))
	var state:=int(_stats.get("trade_state",0))
	if trade_id<=0:
		_trade_status.text="選擇目前可見玩家提出交易"
		var entities:Dictionary=_stats.get("entities",{})
		var ids:Array=entities.keys();ids.sort()
		for raw_actor in ids:
			var actor_id:=int(raw_actor)
			if actor_id<=0 or actor_id==int(_stats.get("actor_id",0)):continue
			_classic_row(_trade_rows,"邀請 %s 交易"%_entity_name(actor_id,entities[raw_actor]),23.0,func():
				if _panel!=null and _panel.session!=null:_panel.session.request_trade(actor_id))
		return
	_trade_status.text="交易 #%d　對象 %d　%s　我/對方金錢 %d/%d"%[
		trade_id,int(_stats.get("trade_peer_actor_id",0)),_trade_state_text(state),
		int(_stats.get("trade_my_money",0)),int(_stats.get("trade_other_money",0))]
	if state==3 and not bool(_stats.get("trade_my_locked",false)):
		var inventory:Dictionary=_stats.get("inventory_items",{})
		var mine:Dictionary=_stats.get("trade_my_items",{})
		var uids:Array=inventory.keys();uids.sort()
		for raw_uid in uids:
			var uid:=int(raw_uid)
			if mine.has(uid):continue
			var item:Dictionary=inventory[raw_uid]
			var item_id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
			_classic_row(_trade_rows,"加入：%s ×%d"%[name,int(item.get("quantity",0))],22.0,func():
				if _panel!=null and _panel.session!=null:_panel.session.trade_add_item(uid))
	var my_items:Dictionary=_stats.get("trade_my_items",{})
	for raw_uid in my_items:
		var item:Dictionary=my_items[raw_uid]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_trade_rows,"我方：%s ×%d"%[name,int(item.get("quantity",0))],20.0)
	var other:Dictionary=_stats.get("trade_other_items",{})
	for raw_uid in other:
		var item:Dictionary=other[raw_uid]
		var item_id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
		_classic_row(_trade_rows,"對方：%s ×%d"%[name,int(item.get("quantity",0))],20.0)

func _refresh_dialog()->void:
	var quest:Dictionary=_stats.get("quest_view",{})
	var response_sequence:=int(_stats.get("last_quest_response_sequence",0))
	var key:="%d/%d/%d"%[response_sequence,int(quest.get("quest_id",0)),int(quest.get("progress_revision",0))]
	if quest.is_empty():
		_dialog_key=""
		_clear_rows(_dialog_choices)
		dialog_window.hide()
		return
	_dialog_title.text=str(quest.get("title","NPC 對話"))
	_dialog_text.text=str(quest.get("text",""))
	_clear_rows(_dialog_choices)
	for raw in quest.get("choices",[]):
		if _dialog_choices.get_child_count()>=4:break
		var choice:Dictionary=raw;var choice_id:=int(choice.get("choice_id",0))
		var row:=_classic_row(_dialog_choices,str(choice.get("label","選項")),13.0,func():
			if _panel!=null:_panel.submit_quest_choice(choice_id))
		row.add_theme_font_size_override("font_size",10)
		row.add_theme_color_override("font_color",Color("#5b3514"))
		row.add_theme_color_override("font_hover_color",Color("#8b4f16"))
	if key!=_dialog_key and response_sequence>0:
		_dialog_key=key
		_hide_windows()
		_ensure_window_art(dialog_window)
		dialog_window.show()

func _hide_windows()->void:
	for window in _windows:window.hide()

func _toggle(window:Control)->void:
	var show:=not window.visible
	_hide_windows()
	if show:
		_ensure_window_art(window)
		window.show()

func _secondary_menu_action(index:int)->void:
	match index:
		0:_toggle(quest_window)
		1:_toggle(shop_window)
		2:_toggle(storage_window)
		3:_toggle(bank_window)
		4:_toggle(repair_window)
		5:_toggle(trade_window)

func _menu_action(index:int)->void:
	match index:
		0:_open_clan_ui()
		1:_toggle(character)
		2:_toggle(inventory)
		3:_open_player_shop_ui()
		4:_toggle(settings_window)
