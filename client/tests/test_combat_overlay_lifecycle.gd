extends SceneTree
const WorldView = preload("res://world/world_view.gd")
const PositionHistory = preload("res://movement/position_history.gd")
var errors: Array[String] = []

func require(value: bool, message: String) -> void:
	if not value:
		errors.append(message)
		push_error(message)

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var view := WorldView.new()
	root.add_child(view)
	# A malformed earlier event must not consume a trusted attack identity.
	var invalid_attack := {"player_attack_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,
		"attack_sequence":15,"target_entity_id":0,"status":0,"damage":12}]}
	var valid_attack := {"player_attack_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,
		"attack_sequence":15,"target_entity_id":1001,"status":0,"damage":12}]}
	view._consume_player_attack_events(invalid_attack,100000)
	view._consume_player_attack_events(valid_attack,110000)
	require(view.combat_overlay_count()==1,
		"invalid attack consumed a later valid authoritative event")
	view._consume_player_attack_events(valid_attack,120000)
	require(view.combat_overlay_count()==1,"attack duplicate was drawn twice")

	var invalid_damage := {"player_damage_visual_events":[{
		"victim_actor_id":5,"victim_connection_epoch":6,
		"damage_sequence":7,"damage":0}]}
	var valid_damage := {"player_damage_visual_events":[{
		"victim_actor_id":5,"victim_connection_epoch":6,
		"damage_sequence":7,"damage":3}]}
	view._consume_player_damage_visual_events(invalid_damage,130000)
	view._consume_player_damage_visual_events(valid_damage,140000)
	require(view.combat_overlay_count()==2,
		"invalid player damage consumed a later valid death/hit event")
	view._consume_player_damage_visual_events(valid_damage,150000)
	require(view.combat_overlay_count()==2,"victim damage duplicate was drawn twice")

	var invalid_pvp := {"pvp_combat_visual_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,"attack_sequence":16,
		"victim_actor_id":5,"victim_connection_epoch":6,"damage_sequence":8,"damage":0}]}
	var valid_pvp := {"pvp_combat_visual_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,"attack_sequence":16,
		"victim_actor_id":5,"victim_connection_epoch":6,"damage_sequence":8,"damage":9}]}
	view._consume_pvp_combat_visual_events(invalid_pvp,160000)
	view._consume_pvp_combat_visual_events(valid_pvp,170000)
	require(view.combat_overlay_count()==3,
		"invalid PvP event consumed a later valid event")
	view._consume_pvp_combat_visual_events(valid_pvp,180000)
	require(view.combat_overlay_count()==3,"PvP damage duplicate was drawn twice")

	# Skill opcode 46 carries committed per-target numbers. Same AoE sequence
	# on another target must render too; a repeated target must not render twice.
	var invalid_skill := {"player_skill_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,
		"skill_sequence":40,"skill_id":99001,"target_entity_id":1001,
		"status":0,"damage":0}]}
	var valid_skill := {"player_skill_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,
		"skill_sequence":40,"skill_id":99001,"target_entity_id":1001,
		"status":0,"damage":38}]}
	view._consume_player_skill_events(invalid_skill,181000)
	view._consume_player_skill_events(valid_skill,182000)
	require(view.combat_overlay_count()==4 and
		str(view._combat_overlays[-1].text)=="38",
		"authoritative single-target skill damage not displayed")
	view._consume_player_skill_events(valid_skill,183000)
	require(view.combat_overlay_count()==4,"same skill target duplicate displayed")
	valid_skill.player_skill_events[0].target_entity_id=1002
	view._consume_player_skill_events(valid_skill,184000)
	require(view.combat_overlay_count()==5 and
		str(view._combat_overlays[-1].text)=="38",
		"area skill second target not rendered independently")
	valid_skill.player_skill_events[0].skill_sequence=41
	valid_skill.player_skill_events[0].status=1
	valid_skill.player_skill_events[0].damage=0
	view._consume_player_skill_events(valid_skill,185000)
	require(view.combat_overlay_count()==6 and
		bool(view._combat_overlays[-1].miss) and
		str(view._combat_overlays[-1].text)=="MISS",
		"server-authoritative skill MISS lacks HK sprite presentation")

	# A real previously presented monster position is captured at the time
	# its committed hit arrives. After AOI despawn, the float keeps that point.
	var history := PositionHistory.new()
	require(history.push(31,Vector2(40,60),185000),
		"test monster position history rejected a valid source position")
	view.monsters._monsters[1001] = {
		"history":history,"value":{"x":40.0,"y":60.0}}
	var kill_event := {"player_attack_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,
		"attack_sequence":99,"target_entity_id":1001,"status":0,"damage":50}]}
	view._consume_player_attack_events(kill_event,190000)
	var killed: Dictionary = view._combat_overlays[-1]
	require(killed.has("last_target_position") and
		Vector2(killed.last_target_position)==Vector2(40,60),
		"committed damage did not capture visible monster position")
	view.monsters._monsters.erase(1001)
	require(view._combat_overlay_position(killed,200000)==Vector2(40,60),
		"kill float disappeared when dead monster left AOI")
	require(not view._combat_overlay_position(
		{"target_entity_id":9999},200000).is_finite(),
		"fabricated a combat number location for unseen target")

	var before_miss := view.combat_overlay_count()
	view._consume_player_damage_visual_events({"player_damage_visual_events":[{
		"victim_actor_id":5,"victim_connection_epoch":6,
		"damage_sequence":8,"status":1,"damage":0}]},205000)
	require(view.combat_overlay_count()==before_miss+1,
		"authoritative monster MISS overlay was not created")
	var monster_miss: Dictionary=view._combat_overlays[-1]
	require(bool(monster_miss.get("miss",false)) and
		str(monster_miss.get("text",""))=="MISS",
		"authoritative monster MISS did not use MISS presentation")

	# WorldState's five bounded combat visual event windows are each <=4096.
	# Presentation must not accumulate every attack for hours of gameplay.
	for i in range(view.COMBAT_DEDUP_MAX+50):
		view._remember_combat_visual("combat-test:%d" % i)
	require(view._combat_seen.size()<=view.COMBAT_DEDUP_MAX,
		"per-map duplicate filter grew without bound")
	var replay := {"player_attack_events":[{
		"attacker_actor_id":4,"attacker_connection_epoch":2,
		"attack_sequence":100,"target_entity_id":1001,"status":0,"damage":12}]}
	# A new authoritative event must still draw after pruning the ring.
	var before_new := view.combat_overlay_count()
	view._consume_player_attack_events(replay,210000)
	require(view.combat_overlay_count()==before_new+1,
		"new authoritative combat feedback suppressed after bounded pruning")
	view._consume_player_attack_events(replay,220000)
	require(view.combat_overlay_count()==before_new+1,
		"recent committed damage was replayed after ring eviction")
	view.free()
	if errors.is_empty():
		print("COMBAT_OVERLAY_LIFECYCLE_PASS")
	quit(0 if errors.is_empty() else 1)
