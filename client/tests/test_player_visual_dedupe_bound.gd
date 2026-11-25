extends SceneTree
## Isolated presentation-replay memory bound; NOT v15 multiplayer/soak Gate.
const Players = preload("res://player/player_presentation.gd")

func _initialize() -> void:
	_run.call_deferred()

func _failure(message: String) -> void:
	push_error("PLAYER_VISUAL_DEDUPE_BOUND_FAIL " + message)
	quit(1)

func _run() -> void:
	var players = Players.new()
	var cap: int = Players.COMBAT_VISUAL_DEDUPE_MAX
	for sequence in range(cap + 19):
		if not players._remember_visual_event("attack", "1:1:%d" % sequence):
			_failure("new sequence was rejected")
			return
	if players._seen_visual_events.size() != cap or players._seen_visual_ring.size() != cap:
		_failure("visual replay keys grow without bound")
		return
	if players._remember_visual_event("attack","1:1:19"):
		_failure("recent replay produced a second animation")
		return
	if not players._remember_visual_event("attack","1:1:0"):
		_failure("old evicted replay key remained pinned")
		return
	if not players._remember_visual_event("damage","1:1:19"):
		_failure("attack sequence suppressed a different damage domain")
		return
	if not players._remember_visual_event("respawn","1:1:19"):
		_failure("attack/damage sequence suppressed a respawn visual")
		return
	if players._seen_visual_events.size() != cap:
		_failure("cross-domain replay exceeded retention bound")
		return
	players.clear()
	if not players._seen_visual_events.is_empty() or not players._seen_visual_ring.is_empty() or players._seen_visual_ring_head != 0:
		_failure("disconnect/reconfigure retained old animation replays")
		return
	print("PLAYER_VISUAL_DEDUPE_BOUND_PASS cap=%d scoped_replay=true eviction=true epoch_reset=true" % cap)
	quit(0)
