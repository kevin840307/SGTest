extends Control
signal skin_requested(id:String)
## Original IE HK2 in-world presentation.
## This shell owns only presentation/hitboxes. All state and actions delegate
## to the same SGTest SessionPanel/ClientSession as every other UI profile.
const UiTextures = preload("res://ui/ui_texture_loader.gd")
const UiSkins = preload("res://ui/ui_skin_catalog.gd")
const Photos = preload("res://ui/local_item_photo_cache.gd")

const PRIVATE := "res://ui/private_ie_hk2"
const BASE_SIZE := Vector2(800,600)
const ITEM_ROW_COUNT := 16
const SKILL_ROW_COUNT := 16
const HK_SHARED_FONT_FACE := "新細明體"
const HK_SHARED_FONT_HEIGHT := 12
const HK_SHARED_FONT_WEIGHT := 500
const HK_SHARED_FONT_BASELINE := 10.0
const HK_SHARED_FONT_BITMAP := PRIVATE + "/HK_SHARED_UI_FONT.fnt"

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
var _equipment_rows:=VBoxContainer.new()
var _inventory_item_bg:TextureRect
var _inventory_equip_bg:TextureRect
var _inventory_tab:=0
# Preserve interactive inventory rows across unchanged AOI/status snapshots.
# Rebuilding every presentation tick invalidates active item click targets.
var _inventory_render_signature:Dictionary={}
var _inventory_render_initialized:=false
# Party and social lists are player-facing UI: unrelated 30Hz World updates
# should not destroy their buttons or force the chat viewport to scroll.
var _social_render_signatures:Dictionary={}
var _classic_mecha_uid:=0
var _inventory_tab_buttons:Array[TextureButton]=[]
var _character_labels:Dictionary={}
var _character_point_buttons:Array[TextureButton]=[]
var _character_hp_fill:Control
var _character_mp_fill:Control
# Skill rows are owned by classic_skill_window.gd; do not allocate an
# unparented legacy VBoxContainer (orphaned on every UI teardown).
var _party_rows:=VBoxContainer.new()
var _clan_rows:=VBoxContainer.new()
var _shop_buy_rows:=VBoxContainer.new()
var _shop_sell_rows:=VBoxContainer.new()
var _quest_title: Label
var _quest_text: Label
var _quest_choices:=VBoxContainer.new()
var _chat_history:=TextEdit.new()
var _chat_input:=LineEdit.new()
var _friend_rows:=VBoxContainer.new()
var _friend_name_input:=LineEdit.new()
var _friend_action:Button
var _player_shop_rows:=VBoxContainer.new()
var _player_shop_price:=SpinBox.new()
var _player_shop_buy:=Button.new()
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
	_windows=[inventory,character,skill_window,party_window,clan_window,shop_window,quest_window,player_shop_window,storage_window,bank_window,repair_window,trade_window,dialog_window,settings_window]
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
	button.pressed.connect(callback)
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
	status_panel.position=Vector2(1,518)
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
		hotkey_label.text="F%d"%(i+1)
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
	_texture_button(secondary_menu,"GuildCommand","B_BASIC_GUILD",Vector2(3,113),Vector2(24,23),func():_toggle(clan_window),"戰團")
	_texture_button(secondary_menu,"PlayerShopCommand","B_BASIC_PSHOP",Vector2(4,142),Vector2(21,19),func():_toggle(player_shop_window),"小商店")
	_texture_button(secondary_menu,"QuestCommand","B_BASIC_QUEST",Vector2(2,169),Vector2(25,22),func():_toggle(quest_window),"任務")
	_texture_button(secondary_menu,"GmCommand","B_BASIC_GM",Vector2(2,196),Vector2(25,22),func():pass,"GM")
	_texture_button(secondary_menu,"SystemCommand","B_BASIC_SYSTEM",Vector2(2,222),Vector2(24,23),func():_toggle(settings_window),"系統")

	chat_panel.name="ClassicChatPanel"
	chat_panel.position=Vector2(130,484)
	chat_panel.size=Vector2(478,116)
	chat_panel.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(chat_panel)
	var chat_bg:=NinePatchRect.new()
	chat_bg.name="BG_MESSAGE_INGAMEBackground"
	chat_bg.texture=_tex("BG_MESSAGE_INGAME")
	chat_bg.size=chat_panel.size
	chat_bg.patch_margin_left=6
	chat_bg.patch_margin_top=22
	chat_bg.patch_margin_right=6
	chat_bg.patch_margin_bottom=8
	chat_bg.mouse_filter=Control.MOUSE_FILTER_IGNORE
	chat_panel.add_child(chat_bg)
	_chat_history.name="ChatHistory"
	_chat_history.position=Vector2(6,8)
	_chat_history.size=Vector2(466,78)
	_chat_history.editable=false
	_chat_history.wrap_mode=TextEdit.LINE_WRAPPING_BOUNDARY
	_chat_history.mouse_filter=Control.MOUSE_FILTER_IGNORE
	_chat_history.add_theme_stylebox_override("normal",StyleBoxEmpty.new())
	_chat_history.add_theme_color_override("font_color",Color("#f9f2d6"))
	_chat_history.add_theme_font_size_override("font_size",10)
	chat_panel.add_child(_chat_history)
	_chat_input.name="ChatInput"
	_chat_input.position=Vector2(7,89)
	_chat_input.size=Vector2(410,19)
	_chat_input.placeholder_text="輸入附近聊天"
	_chat_input.max_length=256
	_transparent_line_edit(_chat_input)
	_chat_input.text_submitted.connect(func(_value):_send_chat())
	chat_panel.add_child(_chat_input)
	var send:=_hit(chat_panel,"SendChat",Rect2(421,88,50,20),_send_chat,"送出聊天")
	send.text="送出"
	send.add_theme_font_size_override("font_size",9)

	friend_panel.name="ClassicFriendPanel"
	friend_panel.position=Vector2(714,501)
	friend_panel.size=Vector2(86,99)
	friend_panel.mouse_filter=Control.MOUSE_FILTER_STOP
	hud.add_child(friend_panel)
	_background(friend_panel,"BG_RIGHTBOX",friend_panel.size)
	_friend_name_input.name="FriendNameInput"
	_friend_name_input.position=Vector2(3,18)
	_friend_name_input.size=Vector2(57,16)
	_friend_name_input.placeholder_text="好友名稱"
	_friend_name_input.max_length=32
	_transparent_line_edit(_friend_name_input)
	_friend_name_input.add_theme_font_size_override("font_size",9)
	_friend_name_input.text_changed.connect(func(_value):_update_friend_action())
	_friend_name_input.text_submitted.connect(func(_value):_friend_action_pressed())
	friend_panel.add_child(_friend_name_input)
	_friend_action=_hit(friend_panel,"FriendAction",Rect2(61,18,21,16),_friend_action_pressed,"新增好友")
	_friend_action.text="+"
	_friend_action.add_theme_font_size_override("font_size",9)
	var friend_scroll:=ScrollContainer.new()
	friend_scroll.name="FriendScroll"
	friend_scroll.position=Vector2(3,35)
	friend_scroll.size=Vector2(80,60)
	friend_panel.add_child(friend_scroll)
	_friend_rows.name="FriendRows"
	_friend_rows.custom_minimum_size.x=76
	_friend_rows.add_theme_constant_override("separation",0)
	friend_scroll.add_child(_friend_rows)

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
	_equipment_rows.name="EquippedRows"
	_equipment_rows.position=Vector2(70,61)
	_equipment_rows.size=Vector2(153,235)
	_equipment_rows.add_theme_constant_override("separation",0)
	_equipment_rows.hide()
	inventory.add_child(_equipment_rows)

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
	party_window=_window("PartyWindow","BG_TEAM",Vector2(139,144),Vector2(626,424))
	_party_rows.name="PartyRows"
	_party_rows.position=Vector2(7,5)
	_party_rows.size=Vector2(124,132)
	_party_rows.add_theme_constant_override("separation",1)
	party_window.add_child(_party_rows)

func _build_clan()->void:
	clan_window=_window("ClanWindow","BG_CLUB",Vector2(263,360),Vector2(269,113))
	var header:=_classic_label(clan_window,"ClanHeader",Rect2(9,36,245,77),10,false)
	header.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	_clan_rows.name="ClanMemberRows"
	_clan_rows.position=Vector2(5,138)
	_clan_rows.size=Vector2(238,190)
	_clan_rows.add_theme_constant_override("separation",0)
	clan_window.add_child(_clan_rows)
	var create:=_hit(clan_window,"ClanCreateButton",Rect2(7,330,75,24),func():
		if _panel!=null:
			var requested="SGTest戰團"
			_panel.submit_clan_create(requested)
	,"建立戰團")
	create.modulate=Color(1,1,1,0.01)
	var refresh:=_hit(clan_window,"ClanRefreshButton",Rect2(92,330,75,24),func():
		if _panel!=null:_panel.submit_clan_refresh()
	,"更新")
	refresh.modulate=Color(1,1,1,0.01)
	var leave:=_hit(clan_window,"ClanLeaveButton",Rect2(177,330,75,24),func():
		if _panel!=null:_panel.submit_clan_leave()
	,"離開戰團")
	leave.modulate=Color(1,1,1,0.01)

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
	player_shop_window=_window("PlayerShopWindow","BG_PSHOP_01",Vector2(770,506),Vector2(15,69))
	_player_shop_price.name="PlayerShopPrice"
	_player_shop_price.position=Vector2(565,32)
	_player_shop_price.size=Vector2(135,24)
	_player_shop_price.min_value=1
	_player_shop_price.max_value=9000000000000000
	_player_shop_price.step=1
	_player_shop_price.value=100
	player_shop_window.add_child(_player_shop_price)
	_player_shop_rows.name="PlayerShopRows"
	_player_shop_rows.position=Vector2(36,62)
	_player_shop_rows.size=Vector2(670,385)
	_player_shop_rows.add_theme_constant_override("separation",1)
	player_shop_window.add_child(_player_shop_rows)
	_player_shop_buy.text="購買目前查看中的商品"
	_player_shop_buy.position=Vector2(525,461)
	_player_shop_buy.size=Vector2(180,26)
	_player_shop_buy.flat=true
	_player_shop_buy.pressed.connect(_buy_inspected_player_shop)
	player_shop_window.add_child(_player_shop_buy)

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
	settings_window=_window("SettingsWindow","BG_SYSTEM",Vector2(291,207),Vector2(255,196))
	_settings_skin_label=_classic_label(settings_window,"UiLabel",Rect2(9,35,102,20),10,false)
	_settings_skin_label.text="介面風格"
	_settings_picker.name="UiProfilePickerSettings"
	_settings_picker.position=Vector2(112,34)
	_settings_picker.size=Vector2(154,23)
	_settings_picker.item_selected.connect(func(index:int):
		var id:=str(_settings_picker.get_item_metadata(index))
		if not id.is_empty():skin_requested.emit(id))
	settings_window.add_child(_settings_picker)
	_settings_hint=_classic_label(settings_window,"SettingsHint",Rect2(10,42,268,146),10,false)
	_settings_hint.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	set_skin_switch_visible(false)

func set_skin_switch_visible(enabled:bool)->void:
	_skin_switch_visible=enabled
	if _settings_skin_label!=null:_settings_skin_label.visible=enabled
	if _settings_picker!=null:_settings_picker.visible=enabled
	if _settings_hint!=null:
		_settings_hint.position=Vector2(10,70) if enabled else Vector2(10,42)
		_settings_hint.size=Vector2(268,118) if enabled else Vector2(268,146)
		_settings_hint.text="畫面與介面設定共用 SGTest 系統設定。\n切換 UI 不會重新登入或重載地圖。" if enabled else "介面：HK 原版"

func bind_session(panel)->void:
	_panel=panel
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
		_inventory_render_initialized=false
		_inventory_render_signature.clear()
		_social_render_signatures.clear()
		_hide_windows()
		_photos.clear()

func apply_snapshot(snapshot:Dictionary)->void:
	_stats=snapshot.duplicate(true)
	if not _active:return
	_refresh_hud()
	if _inventory_snapshot_changed():
		_refresh_inventory()
	_refresh_character()
	_refresh_skill()
	if _social_snapshot_changed("party",["party_members","party_leader_actor_id"]):
		_refresh_party()
	_refresh_clan()
	_refresh_shop()
	_refresh_quest()
	if _social_snapshot_changed("chat_friends",["chat_messages","friends","pending_friend_sequence"]):
		_refresh_chat_and_friends()
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
	# Old CalEXPBar uses level-up table thresholds. Current snapshots expose raw
	# EXP but not that verified threshold, so only render a percent when the
	# authority explicitly supplies one rather than inventing a formula.
	if _stats.has("player_experience_percent"):
		var exp_percent:=clampi(int(_stats.player_experience_percent),0,100)
		_exp_percent_label.text="%d%%"%exp_percent
		_set_progress(_hud_exp_clip,exp_percent,100)
	else:
		_exp_percent_label.text=""
		_set_progress(_hud_exp_clip,0,0)
	# ITF s04 semantics are not yet proven in the current wire snapshot.
	_status_value_label.text=""
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
	var equip_view:=_inventory_tab==1
	if _inventory_item_bg!=null:_inventory_item_bg.visible=not equip_view
	if _inventory_equip_bg!=null:
		_inventory_equip_bg.visible=equip_view
		if equip_view and _inventory_equip_bg.texture==null:
			_inventory_equip_bg.texture=_tex("BG_Equip")
	_equipment_rows.visible=equip_view
	_inventory_rows.position=Vector2(39,325) if equip_view else Vector2(42,62)
	_inventory_rows.size=Vector2(166,165) if equip_view else Vector2(152,432)
	_refresh_inventory()

func _scroll_inventory(delta:int)->void:
	if inventory==null or _inventory_tab in [1,2]:
		return
	inventory.set_meta("item_scroll",int(inventory.get_meta("item_scroll",0))+delta)
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
	_clear_rows(_equipment_rows)
	var inventory_items:Dictionary=_stats.get("inventory_items",{})
	var equipment_items:Dictionary=_stats.get("equipment_items",{})
	if _inventory_tab==2:
		_set_inventory_scroll_buttons(0)
		_refresh_classic_mecha_inventory(inventory_items)
		return
	if _inventory_tab==1:
		_set_inventory_scroll_buttons(0)
		var equipped_uids:Array=equipment_items.keys();equipped_uids.sort()
		for i in mini(10,equipped_uids.size()):
			var uid:=int(equipped_uids[i]);var item:Dictionary=equipment_items[equipped_uids[i]]
			var id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
			var row:=_classic_row(_equipment_rows,name,23.0,_classic_item_action.bind(uid,id,true))
			_apply_item_grade_color(row,id)
			row.tooltip_text=(_panel.item_catalog.tooltip_text_for(item)+"\n點擊卸下") if _panel!=null else "點擊卸下｜UID %d"%uid
		if equipped_uids.is_empty():
			_classic_row(_equipment_rows,"目前沒有穿戴裝備",23.0)
		var candidates:Array=[]
		for raw_uid in inventory_items:
			var item:Dictionary=inventory_items[raw_uid]
			if _inventory_matches_tab(item,false):
				candidates.append({"uid":int(raw_uid),"item":item})
		candidates.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.uid)<int(b.uid))
		for i in mini(7,candidates.size()):
			var entry:Dictionary=candidates[i];var uid:=int(entry.uid);var item:Dictionary=entry.item
			var id:=int(item.get("item_definition_id",0))
			var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
			var row:=_classic_row(_inventory_rows,"     %s ×%d"%[name,int(item.get("quantity",1))],23.0,_classic_item_action.bind(uid,id,false))
			_apply_item_grade_color(row,id)
			if _panel!=null:
				row.tooltip_text=_panel.item_catalog.tooltip_text_for(item,equipment_items)
				var photo:=TextureRect.new();photo.texture=_photos.photo_for_item(id,_panel.item_catalog)
				photo.position=Vector2(1,1);photo.size=Vector2(21,21);photo.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
				photo.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED;photo.mouse_filter=Control.MOUSE_FILTER_IGNORE
				row.add_child(photo)
		if candidates.is_empty():
			_classic_row(_inventory_rows,"沒有可穿戴的裝備",23.0)
		return
	var entries:Array=[]
	for raw_uid in inventory_items:
		var item:Dictionary=inventory_items[raw_uid]
		if _inventory_matches_tab(item,false):
			entries.append({"uid":int(raw_uid),"item":item})
	entries.sort_custom(func(a:Dictionary,b:Dictionary):return int(a.uid)<int(b.uid))
	var scroll_offset:=_set_inventory_scroll_buttons(entries.size())
	for i in range(scroll_offset,mini(scroll_offset+ITEM_ROW_COUNT,entries.size())):
		var entry:Dictionary=entries[i]
		var uid:=int(entry.uid);var item:Dictionary=entry.item;var id:=int(item.get("item_definition_id",0))
		var name:=str(_panel.item_catalog.name_for(id)) if _panel!=null else "Item %d"%id
		var row:=_classic_row(_inventory_rows,"     %s  ×%d"%[name,int(item.get("quantity",1))],27.0)
		_apply_item_grade_color(row,id)
		row.tooltip_text=_panel.item_catalog.tooltip_text_for(item,equipment_items) if _panel!=null else "UID %d · ID %d"%[uid,id]
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
	var values={
		"名稱":str(_stats.get("character_name",_stats.get("player_name",""))),
		"HP":_character_vital("hp"),"MP":_character_vital("mp"),
		"金錢":str(_stats.get("wallet",_stats.get("player_money","—"))),
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

func _refresh_party()->void:
	_clear_rows(_party_rows)
	var members:Dictionary=_stats.get("party_members",{})
	var ids:Array=members.keys();ids.sort()
	for i in mini(6,ids.size()):
		var id:=int(ids[i]);var member:Dictionary=members[ids[i]]
		_classic_row(_party_rows,"%s%s"%["★ " if id==int(_stats.get("party_leader_actor_id",0)) else "",str(member.get("name","玩家"))],21.5)
	if ids.is_empty():_classic_row(_party_rows,"尚未組隊",21.5)

func _refresh_clan()->void:
	var header=clan_window.get_node_or_null("ClanHeader")
	if header is Label:
		(header as Label).text=("%s\nID %d　團長 %d\n公告：%s"%[
			str(_stats.get("clan_name","")),int(_stats.get("clan_id",0)),
			int(_stats.get("clan_leader_actor_id",0)),
			str(_stats.get("clan_announcement","")) if not str(_stats.get("clan_announcement","")).is_empty() else "（無）"
		]) if int(_stats.get("clan_id",0))>0 else "尚未加入戰團"
	_clear_rows(_clan_rows)
	var members:Dictionary=_stats.get("clan_members",{})
	var ids:Array=members.keys();ids.sort()
	for i in mini(9,ids.size()):
		var id:=int(ids[i]);var member:Dictionary=members[ids[i]]
		_classic_row(_clan_rows,"%s　%s"%["團長" if id==int(_stats.get("clan_leader_actor_id",0)) else "成員",str(member.get("name","玩家"))],20.5)
	if ids.is_empty():_classic_row(_clan_rows,"尚無成員資料",20.5)

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
	if _panel.session.send_nearby_chat(message,0):
		_chat_input.clear()

func _entity_name(actor_id:int,value:Dictionary)->String:
	var name:=str(value.get("name",value.get("character_name","")))
	return name if not name.is_empty() else "玩家 %d"%actor_id

func _refresh_chat_and_friends()->void:
	var messages:Array=_stats.get("chat_messages",[])
	var rows:=PackedStringArray()
	var start:=maxi(0,messages.size()-30)
	for i in range(start,messages.size()):
		var entry:Dictionary=messages[i]
		var channel:=int(entry.get("channel",-1))
		var sender:=str(entry.get("sender_name","玩家 %d"%int(entry.get("sender_actor_id",0))))
		match channel:
			0: rows.append("[附近] %s：%s"%[sender,str(entry.get("message",""))])
			3: rows.append("[隊伍] %s：%s"%[sender,str(entry.get("message",""))])
			4:
				if str(entry.get("direction",""))=="outgoing":
					rows.append("[私訊] 你 → %s：%s"%[str(entry.get("target_name","")),str(entry.get("message",""))])
				else:
					rows.append("[私訊] %s → 你：%s"%[sender,str(entry.get("message",""))])
	_chat_history.text="\n".join(rows)
	if not rows.is_empty():
		_chat_history.set_v_scroll(_chat_history.get_line_count())
	_clear_rows(_friend_rows)
	var friends:Dictionary=_stats.get("friends",{})
	var ordered:Array=friends.keys()
	ordered.sort_custom(func(a,b):
		var left:Dictionary=friends[a]
		var right:Dictionary=friends[b]
		var lo:=bool(left.get("online",false))
		var ro:=bool(right.get("online",false))
		if lo!=ro:return lo and not ro
		return str(left.get("name",""))<str(right.get("name","")))
	for raw_actor in ordered:
		var actor_id:=int(raw_actor)
		var entry:Dictionary=friends[raw_actor]
		var friend_name:=str(entry.get("name","玩家 %d"%actor_id))
		var prefix:="● " if bool(entry.get("online",false)) else "○ "
		_classic_row(_friend_rows,prefix+friend_name,16.0,_select_friend.bind(friend_name))
	if ordered.is_empty():
		_classic_row(_friend_rows,"尚無好友",16.0)
	_update_friend_action()


func _friend_actor_for_name(target_name:String)->int:
	var clean:=target_name.strip_edges()
	if clean.is_empty():return 0
	var friends:Dictionary=_stats.get("friends",{})
	for raw_actor in friends:
		var entry:Dictionary=friends[raw_actor]
		if str(entry.get("name",""))==clean:
			return int(raw_actor)
	return 0


func _select_friend(friend_name:String)->void:
	_friend_name_input.text=friend_name
	_friend_name_input.caret_column=friend_name.length()
	_update_friend_action()


func _update_friend_action()->void:
	var clean:=_friend_name_input.text.strip_edges()
	var actor:=_friend_actor_for_name(clean)
	_friend_action.text="-" if actor>0 else "+"
	_friend_action.tooltip_text="移除好友" if actor>0 else "新增好友"
	_friend_action.disabled=clean.is_empty() or int(_stats.get("pending_friend_sequence",0))!=0


func _friend_action_pressed()->void:
	if _panel==null or _panel.session==null:return
	var clean:=_friend_name_input.text.strip_edges()
	if clean.is_empty():return
	var actor:=_friend_actor_for_name(clean)
	var queued:bool=_panel.session.friend_remove(actor) if actor>0 else _panel.session.friend_add_name(clean)
	if queued:
		_friend_name_input.clear()
		_update_friend_action()


func _open_player_shop_item(uid:int)->void:
	if _panel!=null and _panel.session!=null:
		_panel.session.open_player_shop(uid,int(_player_shop_price.value))

func _inspect_player_shop(actor_id:int)->void:
	if _panel!=null and _panel.session!=null:
		_panel.session.inspect_player_shop(actor_id)

func _close_player_shop_listing(listing_id:int)->void:
	if _panel!=null and _panel.session!=null and listing_id>0:
		_panel.session.close_player_shop(listing_id)

func _buy_player_shop_listing(seller_actor_id:int,listing_id:int)->void:
	if _panel!=null and _panel.session!=null and seller_actor_id>0 and listing_id>0:
		_panel.session.buy_player_shop(seller_actor_id,listing_id)

func _buy_inspected_player_shop()->void:
	if _panel==null or _panel.session==null:
		return
	var listing:Dictionary=_stats.get("player_shop_inspected",{})
	if listing.is_empty():
		return
	_buy_player_shop_listing(
		int(listing.get("seller_actor_id",0)),int(listing.get("listing_id",0)))

func _refresh_player_shop()->void:
	_clear_rows(_player_shop_rows)
	var own:Dictionary=_stats.get("player_shop_own_listing",{})
	var own_listings:Dictionary=_stats.get("player_shop_own_listings",{})
	if own_listings.is_empty() and not own.is_empty():
		own_listings[int(own.get("listing_id",0))]=own
	var inspected:Dictionary=_stats.get("player_shop_inspected",{})
	var inspected_listings:Dictionary=_stats.get("player_shop_inspected_listings",{})
	if inspected_listings.is_empty() and not inspected.is_empty():
		inspected_listings[int(inspected.get("listing_id",0))]=inspected

	var listed_uids:={}
	var own_ids:Array=own_listings.keys();own_ids.sort()
	for raw_listing_id in own_ids:
		var own_listing_id:=int(raw_listing_id)
		var own_listing:Dictionary=own_listings[raw_listing_id]
		listed_uids[int(own_listing.get("item_uid",0))]=true
		var own_id:=int(own_listing.get("item_definition_id",0))
		var own_name:=str(_panel.item_catalog.name_for(own_id)) if _panel!=null else "道具 %d"%own_id
		_classic_row(_player_shop_rows,"我的商店：%s ×%d　$%d"%[
			own_name,int(own_listing.get("quantity",0)),int(own_listing.get("price",0))],
			24.0,_close_player_shop_listing.bind(own_listing_id))

	if own_listings.size()<16:
		var inventory:Dictionary=_stats.get("inventory_items",{})
		var uids:Array=inventory.keys();uids.sort()
		var offer_count:=0
		for raw_uid in uids:
			if offer_count>=8:break
			var uid:=int(raw_uid)
			if listed_uids.has(uid):
				continue
			var item:Dictionary=inventory[raw_uid]
			var item_id:=int(item.get("item_definition_id",0))
			var item_name:=str(_panel.item_catalog.name_for(item_id)) if _panel!=null else "道具 %d"%item_id
			_classic_row(_player_shop_rows,"上架：%s ×%d"%[
				item_name,int(item.get("quantity",0))],22.0,_open_player_shop_item.bind(uid))
			offer_count+=1

	var entities:Dictionary=_stats.get("entities",{})
	var entity_ids:Array=entities.keys();entity_ids.sort()
	for raw_actor in entity_ids:
		var actor_id:=int(raw_actor)
		if actor_id<=0 or actor_id==int(_stats.get("actor_id",0)):
			continue
		_classic_row(_player_shop_rows,"查看 %s 的小商店"%_entity_name(actor_id,entities[raw_actor]),20.0,_inspect_player_shop.bind(actor_id))

	var inspected_ids:Array=inspected_listings.keys();inspected_ids.sort()
	for raw_listing_id in inspected_ids:
		var inspected_listing_id:=int(raw_listing_id)
		var inspected_listing:Dictionary=inspected_listings[raw_listing_id]
		var inspected_item_id:=int(inspected_listing.get("item_definition_id",0))
		var inspected_name:=str(_panel.item_catalog.name_for(inspected_item_id)) if _panel!=null else "道具 %d"%inspected_item_id
		_classic_row(_player_shop_rows,"查看中：%s ×%d　$%d"%[
			inspected_name,int(inspected_listing.get("quantity",0)),
			int(inspected_listing.get("price",0))],24.0,
			_buy_player_shop_listing.bind(
				int(inspected_listing.get("seller_actor_id",0)),inspected_listing_id))
	_player_shop_buy.disabled=inspected_listings.is_empty() or int(_stats.get("pending_player_shop_sequence",0))!=0

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
		0:_toggle(clan_window)
		1:_toggle(character)
		2:_toggle(inventory)
		3:_toggle(player_shop_window)
		4:_toggle(settings_window)
