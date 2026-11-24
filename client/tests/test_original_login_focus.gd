extends SceneTree
## Original login keyboard-focus regression.
## Fresh Paradise native A/B confirms Tab cycles account -> password -> account.
## HK build 128 has no login-specific Tab override, so keep the original two-edit
## traversal and prevent buttons from entering the keyboard focus loop.
const Main = preload("res://app/main.gd")
const Layout = preload("res://ui/session_layout.gd")
const State = preload("res://world/world_state.gd")

func _fail(reason: String) -> void:
	push_error("ORIGINAL_LOGIN_FOCUS_FAIL " + reason)
	quit(1)

func _initialize() -> void:
	root.size = Vector2i(800,600)
	_run.call_deferred()

func _run() -> void:
	var app = Main.new()
	app.auto_start = false
	root.add_child(app)
	var shell = app.classic_auth
	app.session_panel.set_process(false)
	app.session_panel.active = true
	app.session_panel.show()
	app.session_panel._latest = {"state":State.STATE_SELECTING_CHARACTER}
	app.auto_start = true
	app.session_panel._layout.show_page(Layout.Page.LOGIN)
	app._update_ui_stage(true)
	if shell._username.focus_mode != Control.FOCUS_ALL or shell._password.focus_mode != Control.FOCUS_ALL:
		_fail("login edits must participate in the original two-edit Tab loop")
		return
	if shell._login_confirm.focus_mode != Control.FOCUS_NONE or shell._login_exit.focus_mode != Control.FOCUS_NONE:
		_fail("login buttons must stay outside the original Tab loop")
		return
	shell._username.grab_focus()
	await process_frame
	var login_tab := InputEventKey.new()
	login_tab.keycode = KEY_TAB
	login_tab.physical_keycode = KEY_TAB
	login_tab.pressed = true
	Input.parse_input_event(login_tab)
	await process_frame
	login_tab.pressed = false
	Input.parse_input_event(login_tab)
	await process_frame
	if root.gui_get_focus_owner() != shell._password:
		_fail("first Tab must move account -> password")
		return
	login_tab.pressed = true
	Input.parse_input_event(login_tab)
	await process_frame
	login_tab.pressed = false
	Input.parse_input_event(login_tab)
	await process_frame
	if root.gui_get_focus_owner() != shell._username:
		_fail("second Tab must move password -> account")
		return
	print("ORIGINAL_LOGIN_FOCUS_PASS")
	quit(0)
