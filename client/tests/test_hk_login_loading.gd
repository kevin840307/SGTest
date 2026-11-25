extends SceneTree
## Diagnostic-only UI geometry and loading-state check, NOT the v15 native gate.
const Shell = preload("res://ui/classic_auth_shell.gd")

func _initialize() -> void:
	_run.call_deferred()

func _fail(shell: Node, reason: String) -> void:
	if shell != null:
		shell.queue_free()
	push_error("HK_LOGIN_LOADING_FAIL " + reason)
	quit(1)

func _run() -> void:
	var shell = Shell.new()
	root.add_child(shell)
	shell.login_page.show()
	if shell._login_loading.visible:
		_fail(shell,"modal is visible outside an actual connection")
		return
	if not FileAccess.file_exists("res://ui/private_ie_hk2/BG_LOGIN_LOADING.png"):
		_fail(shell,"source-verified HK background is missing")
		return
	var backdrop: TextureRect = shell._login_loading.get_node("HkConnectingBackground")
	var clip: Control = shell._login_loading.get_node("HkConnectingProgressClip")
	if backdrop.position != Vector2(286,276) or backdrop.size != Vector2(233,49):
		_fail(shell,"original SPR background position / dimensions drifted")
		return
	if clip.position != Vector2(290,305) or clip.size != Vector2(225,10) or not clip.clip_contents:
		_fail(shell,"original ITF EXP rect drifted")
		return
	shell._set_login_loading(true)
	if not shell._login_loading.visible or not shell._login_confirm.disabled or shell._username.editable or shell._password.editable:
		_fail(shell,"pending TLS state failed to block duplicate input")
		return
	if shell._login_loading_progress.texture == null or shell._login_loading_progress.size != Vector2(225,12):
		_fail(shell,"source EXP.spr frame not loaded in its native geometry")
		return
	# The original 120s timer belongs to the connecting UI, not authentication.
	# At its visual deadline, this presentation must still wait on the real session.
	shell._login_loading_since = Time.get_ticks_msec()-Shell.HK_CONNECTING_TIMEOUT_MS
	shell._set_login_loading(true)
	if not shell._login_loading.visible or shell._login_loading_frame != 14:
		_fail(shell,"cosmetic timeout must not auto-authenticate or dismiss the modal")
		return
	shell._set_login_loading(false)
	if shell._login_loading.visible or shell._login_confirm.disabled or not shell._username.editable or not shell._password.editable:
		_fail(shell,"failed/cancelled login must restore the real form")
		return
	shell.queue_free()
	await process_frame
	print("HK_LOGIN_LOADING_DIAGNOSTIC_PASS original_spr=true original_itf_rect=true pending_inputs_blocked=true cosmetic_timer_does_not_auth=true cancel_restores=true")
	quit(0)
