extends Control
## Original 800x600 IE HK2 authentication presentation.
## This shell owns only visuals/hitboxes. Login/character state remains in the
## shared SessionPanel/ClientSession so UI profile switching never reconnects.
const Layout = preload("res://ui/session_layout.gd")
const PlayerPreview = preload("res://player/character_preview.gd")
const AppearanceLabels = preload("res://ui/character_appearance_picker.gd")
const UiTextures = preload("res://ui/ui_texture_loader.gd")

const SOURCE := Vector2(800,600)
const ART := "res://ui/private_ie_hk2"
const CREATE_FIELDS := ["race","hair_index","hair_color","body_index","sleeve_color","leg_index"]
const CREATE_ATTRIBUTE_FIELDS := ["strength","dexterity","constitution","wisdom"]
const CREATE_ATTRIBUTE_TOTAL := 8
const HK_SHARED_FONT_FACE := "新細明體"
const HK_SHARED_FONT_HEIGHT := 12
const HK_SHARED_FONT_WEIGHT := 500
const HK_SHARED_FONT_BASELINE := 10.0

class LoginCaption extends Control:
	var text := ""
	var font: Font

	func _draw() -> void:
		# ITF positions identify the top-left of a 12px text cell. Canvas
		# drawing keeps the same origin as the background and input hitboxes.
		draw_string(font,Vector2(1,10),text,HORIZONTAL_ALIGNMENT_LEFT,-1,12,Color.WHITE)

class HkSharedText extends Control:
	var text := "":
		set(value):
			if text != value:
				text = value
				queue_redraw()
	var text_font: Font
	var centered := true
	var color := Color.WHITE
	var font_size := HK_SHARED_FONT_HEIGHT
	var baseline := HK_SHARED_FONT_BASELINE

	func _draw() -> void:
		var alignment := HORIZONTAL_ALIGNMENT_CENTER if centered else HORIZONTAL_ALIGNMENT_LEFT
		var draw_font := text_font if text_font != null else get_theme_default_font()
		draw_string(draw_font,Vector2(0,baseline),text,alignment,int(size.x),font_size,color)

var stage := Control.new()
var login_page := Control.new()
var select_page := Control.new()
var create_page := Control.new()
var _panel
var _asset_store
var _username := LineEdit.new()
var _password := LineEdit.new()
var _name := LineEdit.new()
var _login_confirm: Button
var _login_exit: Button
var _login_account_label: LoginCaption
var _login_password_label: LoginCaption
var _login_confirm_label: LoginCaption
var _login_exit_label: LoginCaption
var _login_font := SystemFont.new()
var _login_input_font: Font
var _shared_ui_font: Font
var _status: Label
var _create_note: Label
var _create_password := LineEdit.new()
var _create_password_confirm := LineEdit.new()
var _create_stat_values: Dictionary = {}
var _create_remaining: Label
var _create_button: Button
var _create_back_button: Button
var _slots: Array[Button] = []
var _slot_names: Array[HkSharedText] = []
var _select_fixed_texts: Array[HkSharedText] = []
var _slot_empty_art: Array[TextureRect] = []
var _slot_play_art: Array[TextureRect] = []
var _slot_password_buttons: Array[Button] = []
var _slot_enter_buttons: Array[Button] = []
var _slot_delete_buttons: Array[Button] = []
var _select_back_button: Button
var _password_modal := Control.new()
var _password_modal_input := LineEdit.new()
var _password_modal_confirm: Button
var _password_modal_cancel: Button
var _change_password_modal := Control.new()
var _change_old_password := LineEdit.new()
var _change_new_password := LineEdit.new()
var _change_confirm_password := LineEdit.new()
var _password_prompt: Label
var _select_status: Label
var _delete_step := 0
var _modal_character_id := 0
var _previews: Array = []
var _values: Dictionary = {}
var _current_page := -1
var _character_key := ""

func _init() -> void:
	name = "ClassicAuthenticationUI"
	visible = false
	mouse_filter = Control.MOUSE_FILTER_STOP
	stage.size = SOURCE
	add_child(stage)
	for page in [login_page,select_page,create_page]:
		page.size = SOURCE
		stage.add_child(page)
		page.hide()
	_build_login()
	_build_select()
	_build_create()
	set_process(false)
	resize_view(Vector2(1152,620))

func _ready() -> void:
	# Once inherited theme metrics are final, remove the unparented fallback
	# minimum retained by LineEdit and restore the exact HK ITF edit boxes.
	_username.size = Vector2(170,16)
	_password.size = Vector2(170,16)
	_name.size = Vector2(125,15)
	_create_password.size = Vector2(43,16)
	_create_password_confirm.size = Vector2(43,16)

func _tex(name: String) -> Texture2D:
	return UiTextures.get_texture(ART.path_join(name + ".png"))

func _art(parent: Control,file: String) -> TextureRect:
	var picture := TextureRect.new()
	picture.name = file.trim_suffix(".png")
	picture.size = SOURCE
	picture.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	picture.stretch_mode = TextureRect.STRETCH_SCALE
	picture.mouse_filter = Control.MOUSE_FILTER_IGNORE
	picture.texture = UiTextures.get_texture(ART.path_join(file))
	parent.add_child(picture)
	return picture

func _input_at(parent: Control,node: Control,x: float,y: float,w: float,h: float) -> void:
	node.position = Vector2(x,y)
	node.size = Vector2(w,h)
	parent.add_child(node)

func _empty_style() -> StyleBoxEmpty:
	return StyleBoxEmpty.new()

func _configure_hk_gdi_raster(font: Resource) -> void:
	font.set("antialiasing",TextServer.FONT_ANTIALIASING_NONE)
	font.set("hinting",TextServer.HINTING_NORMAL)
	font.set("subpixel_positioning",TextServer.SUBPIXEL_POSITIONING_DISABLED)
	font.set("disable_embedded_bitmaps",false)
	font.set("keep_rounding_remainders",false)

func _make_login_input_font() -> Font:
	# Godot 4.7.2 resolves all mingliu.ttc face indexes to the same MingLiU
	# raster on this Windows build. Use the local private GDI-generated BMFont
	# when available so PMingLiU pixels/advances stay identical to HK without
	# shipping the proprietary Windows font file.
	var bitmap_path := "res://ui/private_ie_hk2/ACCOUNT_LOGIN_HK_INPUT_FONT.fnt"
	if FileAccess.file_exists(bitmap_path):
		var bitmap := FontFile.new()
		if bitmap.load_bitmap_font(bitmap_path) == OK:
			return bitmap
	var fallback := SystemFont.new()
	fallback.font_names = PackedStringArray(["PMingLiU"])
	_configure_hk_gdi_raster(fallback)
	return fallback

func _make_hk_shared_ui_font() -> Font:
	# HK sub_4E92A9 -> sub_561330 creates the shared UI GDI font as
	# 新細明體, lfHeight=12, lfWeight=500, charset=1, quality=4. Character
	# Select obtains this same renderer through sub_490774. Prefer the private
	# Windows-GDI BMFont so Godot does not silently substitute a TTC face.
	var bitmap_path := "res://ui/private_ie_hk2/HK_SHARED_UI_FONT.fnt"
	if FileAccess.file_exists(bitmap_path):
		var bitmap := FontFile.new()
		if bitmap.load_bitmap_font(bitmap_path) == OK:
			return bitmap
	var fallback := SystemFont.new()
	fallback.font_names = PackedStringArray([HK_SHARED_FONT_FACE])
	fallback.font_weight = HK_SHARED_FONT_WEIGHT
	_configure_hk_gdi_raster(fallback)
	return fallback

func _shared_text(parent: Control,text: String,x: float,y: float,w: float,h: float,centered:=true) -> HkSharedText:
	var label := HkSharedText.new()
	label.text = text
	label.text_font = _shared_ui_font
	label.centered = centered
	label.font_size = HK_SHARED_FONT_HEIGHT
	label.baseline = HK_SHARED_FONT_BASELINE
	label.clip_contents = true
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_input_at(parent,label,x,y,w,h)
	return label

func _style_line_edit(input: LineEdit) -> void:
	input.add_theme_stylebox_override("normal",_empty_style())
	input.add_theme_stylebox_override("focus",_empty_style())
	input.add_theme_stylebox_override("read_only",_empty_style())
	input.add_theme_color_override("font_color",Color("#fff7d8"))
	input.add_theme_color_override("font_placeholder_color",Color("#c9b889"))
	input.add_theme_color_override("caret_color",Color("#ffffff"))
	input.add_theme_font_size_override("font_size",13)
	input.select_all_on_focus = false

func _style_hit(button: Button,visible_text:=true) -> void:
	button.flat = true
	button.focus_mode = Control.FOCUS_NONE
	for state in ["normal","hover","pressed","focus","disabled"]:
		button.add_theme_stylebox_override(state,_empty_style())
	button.add_theme_color_override("font_color",Color("#fff4c6") if visible_text else Color.TRANSPARENT)
	button.add_theme_color_override("font_hover_color",Color.WHITE if visible_text else Color.TRANSPARENT)
	button.add_theme_color_override("font_pressed_color",Color("#ffe49b") if visible_text else Color.TRANSPARENT)
	button.add_theme_color_override("font_shadow_color",Color(0,0,0,0.9))
	button.add_theme_constant_override("shadow_offset_x",1)
	button.add_theme_constant_override("shadow_offset_y",1)
	button.add_theme_font_size_override("font_size",12)

func _sprite_at(parent: Control,name: String,x: float,y: float,w: float,h: float) -> TextureRect:
	var art := TextureRect.new()
	art.name = name + "Art"
	art.texture = _tex(name)
	art.position = Vector2(x,y)
	art.size = Vector2(w,h)
	art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	art.stretch_mode = TextureRect.STRETCH_SCALE
	art.mouse_filter = Control.MOUSE_FILTER_IGNORE
	parent.add_child(art)
	return art

func _button(parent: Control,text: String,x: float,y: float,w: float,h: float,callback: Callable,art_name: String="") -> Button:
	if not art_name.is_empty():
		var art := TextureRect.new()
		art.name = art_name + "Art"
		art.texture = _tex(art_name + "_0")
		art.position = Vector2(x,y)
		art.size = Vector2(w,h)
		art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		parent.add_child(art)
	var button := Button.new()
	button.text = text
	_style_hit(button,true)
	button.pressed.connect(callback)
	_input_at(parent,button,x,y,w,h)
	return button

func _label(parent: Control,text: String,x: float,y: float,w: float,h: float) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color",Color("#5a3212"))
	l.add_theme_color_override("font_shadow_color",Color("#fff0b8"))
	l.add_theme_constant_override("shadow_offset_x",1)
	l.add_theme_constant_override("shadow_offset_y",1)
	l.add_theme_font_size_override("font_size",12)
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_input_at(parent,l,x,y,w,h)
	return l

func _login_caption(text: String,x: int,y: int,width: int) -> LoginCaption:
	var caption := LoginCaption.new()
	caption.text = text
	caption.font = _login_font
	caption.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_input_at(login_page,caption,x,y,width,12)
	return caption

func _login_button(x: int,callback: Callable) -> Button:
	var button := _button(login_page,"",x,314,85,21,callback)
	button.flat = false
	var states := ["normal","hover","pressed","disabled"]
	for frame in states.size():
		var texture := _tex("YELLOW_BUTTON_%d" % frame)
		if texture != null:
			var style := StyleBoxTexture.new()
			style.texture = texture
			button.add_theme_stylebox_override(states[frame],style)
			if states[frame] == "pressed":
				button.add_theme_stylebox_override("hover_pressed",style)
	return button

func _build_login() -> void:
	_art(login_page,"ACCOUNT_LOGIN_HK.png")
	# OH01 in 001_ACCOUNT_LOGIN.ITF. The local HK test did not open a
	# help window when clicked, so its action remains unverified.
	_sprite_at(login_page,"B_ONLINEHELP_0",537,228,16,16)
	# Fixed captions use the separately rendered HK caption layer when available.
	# The fallback stays isolated from the dynamic edit-control font below.
	_login_font.font_names = PackedStringArray(["MingLiU"])
	# HK build 128 client-DC glyph evidence: TEST01 is a 36x9 / 111-pixel
	# binary mask. Replaying sub_561330's 16-bit GDI path matches PMingLiU
	# height 12 exactly (0-pixel XOR); Arial/MingLiU/MS Sans do not.
	_login_input_font = _make_login_input_font()
	# 001_ACCOUNT_LOGIN.ITF is the geometry/label authority for the HK login.
	# Keep the text controls separate from the decoded background/button SPRs,
	# exactly as the original ITF does; do not replace them with placeholders.
	_login_account_label = _login_caption("帳戶名稱",281,257,48)
	_login_password_label = _login_caption("帳戶密碼",281,281,48)
	_username.placeholder_text = ""
	# HK sub_42997A configures control 0x7D1 with a 0x0C character limit.
	_username.max_length = 12
	_password.secret = true
	_password.secret_character = "*"
	_password.max_length = 256
	_password.placeholder_text = ""
	_style_line_edit(_username)
	_style_line_edit(_password)
	_username.add_theme_font_override("font",_login_input_font)
	_username.add_theme_font_size_override("font_size",12)
	# L01/L02 are the same HK line-edit control type; L02 only enables secret
	# mode. Its six-star client-DC mask also matches PMingLiU height 12 exactly.
	_password.add_theme_font_override("font",_login_input_font)
	_password.add_theme_font_size_override("font_size",12)
	for input in [_username,_password]:
		# Fresh Paradise native A/B confirms the original login cycles Tab only
		# between its two edit controls: account -> password -> account. The login
		# buttons are intentionally FOCUS_NONE, so Godot's native FOCUS_ALL
		# traversal reproduces that two-edit loop without a custom Tab handler.
		input.focus_mode = Control.FOCUS_ALL
		input.add_theme_color_override("font_color",Color8(241,245,241))
		for state in ["normal","focus","read_only"]:
			var style: StyleBox = input.get_theme_stylebox(state)
			# The BMFont preserves GDI's own +1 glyph x/y offset. Keep the edit
			# control at the ITF rect and only remove LineEdit's extra centering.
			style.content_margin_left = 0.0
			style.content_margin_bottom = 4.0
	_input_at(login_page,_username,373,256,170,16)
	_input_at(login_page,_password,373,280,170,16)
	# _input_at sizes before parenting; LineEdit otherwise retains the fallback
	# unparented theme's 31px minimum instead of the zero-style HK height.
	_username.size = Vector2(170,16)
	_password.size = Vector2(170,16)
	_password.text_submitted.connect(func(_unused): _login())
	_login_confirm = _login_button(368,_login)
	_login_confirm.name = "LoginConfirm"
	_login_exit = _login_button(463,_exit_login)
	_login_exit.name = "LoginExit"
	_login_confirm_label = _login_caption("確定",399,318,24)
	_login_exit_label = _login_caption("離開",494,318,24)
	var native_captions := _art(login_page,"ACCOUNT_LOGIN_HK_CAPTIONS.png")
	for caption in [_login_account_label,_login_password_label,_login_confirm_label,_login_exit_label]:
		caption.visible = native_captions.texture == null
	_status = _label(login_page,"",237,343,326,32)
	_status.add_theme_color_override("font_color",Color("#7a3517"))

func _build_select() -> void:
	_art(select_page,"SELECT_CHARACTER.png")
	_shared_ui_font = _make_hk_shared_ui_font()
	# 004_SELECT_CHARACTER.ITF: each of the three HK slots owns three distinct
	# actions. The HK .lst maps 密碼設定/進入遊戲/刪除角色 to states 9/6/7.
	var name_x := [150.0,338.0,525.0]
	var preview_x := [169.0,358.0,544.0]
	var action_x := [168.0,358.0,544.0]
	var label_x := [187.0,377.0,563.0]
	for i in 3:
		# HK Select uses the process-wide shared GDI font. Keep the ITF rect
		# and draw at the original 10px baseline instead of Label vertical-center.
		var name_label := _shared_text(select_page,"",name_x[i],260,124,16)
		_slot_names.append(name_label)

		var preview := PlayerPreview.new()
		_input_at(select_page,preview,preview_x[i],284,84,130)
		preview.bind_store(_asset_store)
		_previews.append(preview)

		# 004_Select_Character.itf keeps the slot control/hitbox at 84x130, but
		# NEW_CHARACTER.spr itself is 85x132 with origin (0,0). Preserve the
		# original frame pixels instead of scaling the SPR down to the hitbox.
		var empty_art := TextureRect.new()
		empty_art.texture = _tex("NEW_CHARACTER_0")
		empty_art.position = Vector2(preview_x[i],284)
		empty_art.size = Vector2(85,132)
		empty_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		empty_art.stretch_mode = TextureRect.STRETCH_SCALE
		empty_art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		select_page.add_child(empty_art)
		_slot_empty_art.append(empty_art)

		# PLAY_GAME.spr is anchored to the same 84x130 character control. HK
		# source metadata records a 67x23 frame with origin (-8,-101); the
		# renderer convention is draw_at = control - origin, hence +8,+101.
		var play_art := TextureRect.new()
		play_art.texture = _tex("PLAY_GAME_0")
		play_art.position = Vector2(preview_x[i]+8,385)
		play_art.size = Vector2(67,23)
		play_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		play_art.stretch_mode = TextureRect.STRETCH_SCALE
		play_art.mouse_filter = Control.MOUSE_FILTER_IGNORE
		play_art.visible = false
		select_page.add_child(play_art)
		_slot_play_art.append(play_art)

		var password := _button(select_page,"",action_x[i],420,85,21,_open_change_password.bind(i),"YELLOW_BUTTON")
		var enter := _button(select_page,"",action_x[i],444,85,21,_enter_slot.bind(i),"YELLOW_BUTTON")
		_style_hit(enter,false)
		var remove := _button(select_page,"",action_x[i],468,85,21,_open_delete.bind(i),"YELLOW_BUTTON")
		_slot_password_buttons.append(password)
		_slot_enter_buttons.append(enter)
		_slot_delete_buttons.append(remove)
		_slots.append(enter)
		var create_hit := _button(select_page,"",preview_x[i],284,84,130,_select_slot.bind(i))
		create_hit.name = "CreateCharacterSlot%d" % i

		for spec in [
			["密碼設定",label_x[i],424,48,12],
			["進入遊戲",label_x[i],448,48,12],
			["刪除角色",label_x[i],472,48,12],
		]:
			_select_fixed_texts.append(_shared_text(
				select_page,str(spec[0]),float(spec[1]),float(spec[2]),float(spec[3]),float(spec[4])))

	_select_back_button = _button(select_page,"",544,499,85,21,func(): _panel.disconnect_session(),"YELLOW_BUTTON")
	_select_fixed_texts.append(_shared_text(select_page,"上一頁",569,504,36,12))

	_select_status = _label(select_page,"",237,532,326,24)
	_build_select_password_modal()
	_build_change_password_modal()

func _modal_background(modal: Control, image: String, size: Vector2) -> void:
	modal.size = SOURCE
	modal.mouse_filter = Control.MOUSE_FILTER_STOP
	select_page.add_child(modal)
	var origin := (SOURCE-size)/2
	_sprite_at(modal,image,origin.x,origin.y,size.x,size.y)
	modal.hide()

func _modal_password(modal: Control, input: LineEdit, origin: Vector2, x: int, y: int) -> void:
	input.secret = true
	input.max_length = 4
	_style_line_edit(input)
	input.add_theme_font_size_override("font_size",11)
	_input_at(modal,input,origin.x+x,origin.y+y,170,12)
	input.size = Vector2(170,12)

func _modal_label(modal: Control, origin: Vector2, value: String, x: int, y: int, width: int) -> Label:
	var label := _label(modal,value,origin.x+x,origin.y+y,width,12)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.add_theme_color_override("font_color",Color.WHITE)
	label.add_theme_color_override("font_shadow_color",Color.TRANSPARENT)
	return label

func _build_select_password_modal() -> void:
	# 082_LOGIN_PASSWORD.ITF uses coordinates local to the 352×102 SPR.
	var origin := Vector2(224,249)
	_modal_background(_password_modal,"LOGIN_PASSWORD",Vector2(352,102))
	_password_prompt = _modal_label(_password_modal,origin,"角色密碼",50,59,48)
	_modal_password(_password_modal,_password_modal_input,origin,134,59)
	_password_modal_input.text_submitted.connect(func(_text): _submit_slot_password())
	_password_modal_confirm = _button(_password_modal,"",origin.x+164,origin.y+78,85,21,_submit_slot_password,"YELLOW_BUTTON")
	_password_modal_cancel = _button(_password_modal,"",origin.x+259,origin.y+78,85,21,_close_slot_modals,"YELLOW_BUTTON")
	_modal_label(_password_modal,origin,"確認",194,82,24)
	_modal_label(_password_modal,origin,"取消",289,82,24)

func _build_change_password_modal() -> void:
	# 081_LOGIN_CHANGE_PASSWORD.ITF uses coordinates local to the 352×142 SPR.
	var origin := Vector2(224,229)
	_modal_background(_change_password_modal,"LOGIN_CHANGE_PASSWORD",Vector2(352,142))
	_modal_password(_change_password_modal,_change_old_password,origin,149,42)
	_modal_password(_change_password_modal,_change_new_password,origin,149,66)
	_modal_password(_change_password_modal,_change_confirm_password,origin,149,90)
	_modal_label(_change_password_modal,origin,"原有密碼",60,43,48)
	_modal_label(_change_password_modal,origin,"新的密碼",60,67,48)
	_modal_label(_change_password_modal,origin,"確認密碼",59,91,48)
	_button(_change_password_modal,"",origin.x+147,origin.y+116,85,21,_submit_change_password,"YELLOW_BUTTON")
	_button(_change_password_modal,"",origin.x+240,origin.y+116,85,21,_close_slot_modals,"YELLOW_BUTTON")
	_modal_label(_change_password_modal,origin,"確認",178,120,24)
	_modal_label(_change_password_modal,origin,"取消",270,120,24)

func _character_index_at_slot(slot: int) -> int:
	for index in _panel._characters.size():
		if int(_panel._characters[index].get("slot",-1)) == slot:
			return index
	return -1

func _close_slot_modals() -> void:
	_password_modal.hide()
	_change_password_modal.hide()
	for input in [_password_modal_input,_change_old_password,_change_new_password,_change_confirm_password]:
		input.clear()
	_modal_character_id = 0
	_delete_step = 0

func _enter_slot(slot: int) -> void:
	_open_slot_password(slot,false)

func _open_delete(slot: int) -> void:
	_open_slot_password(slot,true)

func _open_slot_password(slot: int, deleting: bool) -> void:
	if _panel == null or not _panel.active or _panel._chosen:
		return
	var index := _character_index_at_slot(slot)
	if index < 0:
		return
	_close_slot_modals()
	_modal_character_id = int(_panel._characters[index].character_id)
	_delete_step = 1 if deleting else 0
	_password_prompt.text = "刪除角色" if deleting else "角色密碼"
	_password_modal.show()
	_password_modal_input.grab_focus()

func _submit_slot_password() -> void:
	if _modal_character_id <= 0:
		return
	var password := _password_modal_input.text
	if password.length() != 4:
		_panel.status.text = "請先設定或輸入 4 位角色密碼"
		return
	if _delete_step == 1:
		_delete_step = 2
		_password_prompt.text = "確認刪除"
		return
	if _delete_step == 2:
		if _panel.delete_character(_modal_character_id,password):
			_close_slot_modals()
		return
	for index in _panel._characters.size():
		if int(_panel._characters[index].character_id) == _modal_character_id:
			_panel.characters.select(index)
			_panel.choose(password)
			if _panel._chosen:
				_close_slot_modals()
			return
	_close_slot_modals()

func _open_change_password(slot: int) -> void:
	if _panel == null or not _panel.active or _panel._chosen:
		return
	var index := _character_index_at_slot(slot)
	if index < 0:
		return
	_close_slot_modals()
	_modal_character_id = int(_panel._characters[index].character_id)
	_change_password_modal.show()
	_change_old_password.grab_focus()

func _submit_change_password() -> void:
	var old_password := _change_old_password.text
	var new_password := _change_new_password.text
	if _modal_character_id <= 0 or new_password.length() != 4 or new_password != _change_confirm_password.text:
		return
	if not old_password.is_empty() and old_password.length() != 4:
		return
	if _panel.change_character_password(_modal_character_id,old_password,new_password):
		_close_slot_modals()

func _build_create() -> void:
	_art(create_page,"CREATE_CHARACTER.png")
	# 005_CREATE_CHARACTER.ITF is the source of truth for labels/hitboxes.
	var static_labels := [
		["名稱",169,261,24,12],["種族",169,285,24,12],
		["頭部",169,309,24,12],["頭部顏色",157,333,48,12],
		["上身顏色",157,357,48,12],["手袖顏色",157,381,48,12],
		["下身顏色",157,405,48,12],["剩餘點數",498,357,48,12],
		["力量",510,261,24,12],["體質",510,309,24,12],
		["速度",510,285,24,12],["智慧",510,333,24,12],
		["角色密碼",498,391,48,12],["確認密碼",498,415,48,12],
	]
	for spec in static_labels:
		var label := _label(create_page,str(spec[0]),float(spec[1]),float(spec[2]),float(spec[3]),float(spec[4]))
		label.add_theme_color_override("font_color",Color.WHITE)
		label.add_theme_color_override("font_shadow_color",Color.TRANSPARENT)

	_name.placeholder_text = ""
	_name.max_length = 24
	_style_line_edit(_name)
	_name.add_theme_font_size_override("font_size",10)
	_input_at(create_page,_name,233,260,125,15)

	var appearance_y := [281.0,304.0,328.0,352.0,376.0,400.0]
	for i in CREATE_FIELDS.size():
		var field: String = CREATE_FIELDS[i]
		_button(create_page,"",228,appearance_y[i],24,21,_cycle.bind(field,-1),"LEFT_BUTTON")
		_button(create_page,"",257,appearance_y[i],24,21,_cycle.bind(field,1),"RIGHT_BUTTON")

	var stat_y := {
		"strength":256.0,
		"dexterity":280.0,
		"constitution":304.0,
		"wisdom":328.0,
	}
	var value_y := {
		"strength":261.0,
		"dexterity":285.0,
		"constitution":309.0,
		"wisdom":333.0,
	}
	for field in CREATE_ATTRIBUTE_FIELDS:
		_button(create_page,"",566,stat_y[field],24,21,_change_creation_stat.bind(field,-1),"LEFT_BUTTON")
		var value := _label(create_page,"1",596,value_y[field],43,16)
		value.add_theme_color_override("font_color",Color.WHITE)
		value.add_theme_color_override("font_shadow_color",Color.TRANSPARENT)
		_create_stat_values[field] = value
		_button(create_page,"",646,stat_y[field],24,21,_change_creation_stat.bind(field,1),"RIGHT_BUTTON")
	_create_remaining = _label(create_page,str(CREATE_ATTRIBUTE_TOTAL-4),596,357,43,16)
	_create_remaining.add_theme_color_override("font_color",Color.WHITE)
	_create_remaining.add_theme_color_override("font_shadow_color",Color.TRANSPARENT)

	for input in [_create_password,_create_password_confirm]:
		input.secret = true
		input.max_length = 4
		input.placeholder_text = ""
		_style_line_edit(input)
		input.add_theme_font_size_override("font_size",11)
	_input_at(create_page,_create_password,596,391,43,16)
	_input_at(create_page,_create_password_confirm,596,415,43,16)

	var doll := PlayerPreview.new()
	_input_at(create_page,doll,346,291,76,126)
	doll.bind_store(_asset_store)
	_previews.append(doll)

	# The original lower-left region is retained as a temporary error fallback;
	# neutral SGTest prose is never painted over the HK creation screen.
	var note := _label(create_page,"",140,434,277,55)
	note.add_theme_color_override("font_color",Color("#5a3212"))
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_create_note = note

	_create_button = _button(create_page,"",478,471,85,21,_create,"YELLOW_BUTTON")
	_create_back_button = _button(create_page,"",583,471,85,21,func(): _panel.back_to_character_button.pressed.emit(),"YELLOW_BUTTON")
	for spec in [["建立角色",497,475,48,12],["上一頁",608,475,36,12]]:
		var label := _label(create_page,str(spec[0]),float(spec[1]),float(spec[2]),float(spec[3]),float(spec[4]))
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		label.add_theme_color_override("font_color",Color.WHITE)
		label.add_theme_color_override("font_shadow_color",Color.TRANSPARENT)

func bind_session(panel, store) -> void:
	_panel = panel
	_asset_store = store
	for preview in _previews:
		preview.bind_store(store)
	panel.creation_options.changed.connect(_update_appearance)

func _login() -> void:
	if _panel == null:
		return
	_panel.username_input.text = _username.text
	_panel.password_input.text = _password.text
	_password.clear()
	_panel.login()

func _exit_login() -> void:
	get_tree().quit()

func _select_slot(index: int) -> void:
	if _panel == null or not _panel.active:
		return
	if _character_index_at_slot(index) >= 0:
		_enter_slot(index)
	else:
		_panel.character_slot_input.value = index
		_panel.new_character_button.pressed.emit()

func _cycle(field: String,delta: int) -> void:
	if _panel != null:
		_panel.creation_options.cycle(field,delta)

func _creation_stat_input(field: String):
	if _panel == null:
		return null
	match field:
		"strength": return _panel.character_strength_input
		"dexterity": return _panel.character_dexterity_input
		"constitution": return _panel.character_constitution_input
		"wisdom": return _panel.character_wisdom_input
	return null

func _creation_attribute_sum() -> int:
	var total := 0
	for field in CREATE_ATTRIBUTE_FIELDS:
		var input = _creation_stat_input(field)
		if input != null:
			total += int(input.value)
	return total

func _sync_create_stats() -> void:
	if _panel == null:
		return
	for field in CREATE_ATTRIBUTE_FIELDS:
		var input = _creation_stat_input(field)
		if input != null and _create_stat_values.has(field):
			_create_stat_values[field].text = str(int(input.value))
	if _create_remaining != null:
		_create_remaining.text = str(maxi(0,CREATE_ATTRIBUTE_TOTAL-_creation_attribute_sum()))

func _change_creation_stat(field: String,delta: int) -> void:
	var input = _creation_stat_input(field)
	if input == null or delta == 0:
		return
	var current := int(input.value)
	if delta > 0 and _creation_attribute_sum() >= CREATE_ATTRIBUTE_TOTAL:
		return
	var next := current + delta
	if next < int(input.min_value) or next > int(input.max_value):
		return
	input.value = next
	_sync_create_stats()

func _create() -> void:
	if _panel == null:
		return
	_panel.character_name_input.text = _name.text
	_panel.character_password_input.text = _create_password.text
	_panel.character_password_confirm_input.text = _create_password_confirm.text
	_panel.create_character()
	# SessionPanel clears secrets only after an accepted request. Mirror that
	# lifecycle here instead of creating a second state owner.
	_create_password.text = _panel.character_password_input.text
	_create_password_confirm.text = _panel.character_password_confirm_input.text

func enable_for_page(page: int,on: bool) -> void:
	var entering_login := on and page == Layout.Page.LOGIN and (not visible or _current_page != page)
	visible = on and _panel != null
	set_process(visible)
	if not visible or page != Layout.Page.CHARACTER_SELECT:
		_close_slot_modals()
	if not visible:
		return
	_current_page = page
	login_page.visible = page == Layout.Page.LOGIN
	select_page.visible = page == Layout.Page.CHARACTER_SELECT
	create_page.visible = page == Layout.Page.CHARACTER_CREATE
	if create_page.visible:
		_name.text = _panel.character_name_input.text
		_create_password.text = _panel.character_password_input.text
		_create_password_confirm.text = _panel.character_password_confirm_input.text
		_sync_create_stats()
		_update_appearance()
	if select_page.visible:
		_character_key = ""
		_update_characters()
	if login_page.visible:
		_username.text = _panel.username_input.text
		if entering_login:
			_username.grab_focus()

func _process(_delta: float) -> void:
	if _panel == null:
		return
	if _current_page != _panel._layout.current_page:
		enable_for_page(_panel._layout.current_page,true)
	if login_page.visible:
		var login_status := str(_panel.status.text)
		# 001_ACCOUNT_LOGIN.ITF has no neutral instructional status label.
		# Keep actual failures visible as a temporary usability fallback until
		# the HK message-modal path is mapped, but do not paint SGTest-only idle
		# or connection-progress prose over the original login artwork.
		_status.text = "" if login_status in ["請登入伺服器","安全連線中…"] else login_status
	if create_page.visible:
		_create_note.text = _panel.character_create_status.text
	if select_page.visible:
		var status := str(_panel.status.text)
		_select_status.text = "" if status == "請選擇角色或建立新角色" else status
		_update_characters()

func _update_characters() -> void:
	var key := str(_panel._characters)+":"+str(_panel._chosen)
	if key == _character_key:
		return
	_character_key = key
	for i in _slots.size():
		var index := _character_index_at_slot(i)
		var occupied := index >= 0
		var c: Dictionary = _panel._characters[index] if occupied else {}
		_slots[i].text = str(c.get("name","")) if occupied else ""
		for button in [_slot_enter_buttons[i],_slot_password_buttons[i],_slot_delete_buttons[i]]:
			button.disabled = not occupied or _panel._chosen
		_slot_names[i].text = str(c.get("name","")) if occupied else ""
		_slot_empty_art[i].visible = not occupied
		_slot_play_art[i].visible = occupied
		_previews[i].visible = occupied
		_previews[i].set_occupied(occupied)
		_previews[i].set_appearance(c.get("appearance",{}) if occupied else {})

func _update_appearance() -> void:
	if _panel == null:
		return
	var selected: Dictionary = _panel.creation_options.selection()
	for field in _values:
		_values[field].text = AppearanceLabels.value_label(field,int(selected.get(field,-1))) if not selected.is_empty() else "未開放"
	_previews[3].set_occupied(not selected.is_empty())
	_previews[3].set_appearance(selected)

func resize_view(view: Vector2) -> void:
	size = view
	var scale_factor := minf(view.x/SOURCE.x,view.y/SOURCE.y)
	stage.scale = Vector2.ONE*scale_factor
	stage.position = (view-SOURCE*scale_factor)*0.5
