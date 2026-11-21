extends VBoxContainer
## Read-only HUD shared by all world UI styles. Data always comes from
## ClientSession/WorldState; UI skins may restyle/rearrange this same node.

var actor_label := Label.new()
var hp := ProgressBar.new()
var mp := ProgressBar.new()
var map_label := Label.new()

func _init() -> void:
	name = "GameplayHUD"
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	actor_label.text = "尚未進入世界"
	add_child(actor_label)
	hp.show_percentage = false
	mp.show_percentage = false
	hp.min_value = 0
	mp.min_value = 0
	hp.max_value = 1
	mp.max_value = 1
	hp.value = 0
	mp.value = 0
	hp.custom_minimum_size.y = 12
	mp.custom_minimum_size.y = 12
	add_child(hp)
	add_child(mp)
	add_child(map_label)
	reset()

func reset() -> void:
	actor_label.text = "尚未進入世界"
	map_label.text = ""
	hp.value = 0
	mp.value = 0
	hp.hide()
	mp.hide()

func apply_snapshot(snapshot: Dictionary) -> void:
	if not bool(snapshot.get("world_ready", false)):
		reset()
		return
	actor_label.text = "角色 #%d" % int(snapshot.get("actor_id",0))
	map_label.text = "地圖 %d｜世代 %d" % [
		int(snapshot.get("map_id",0)),int(snapshot.get("map_epoch",0))]
	var max_hp := maxi(0,int(snapshot.get("player_max_hp",0)))
	var max_mp := maxi(0,int(snapshot.get("player_max_mp",0)))
	hp.visible = max_hp > 0
	mp.visible = max_mp > 0
	if max_hp > 0:
		hp.max_value = max_hp
		hp.value = clampi(int(snapshot.get("player_current_hp",0)),0,max_hp)
		hp.tooltip_text = "HP %d / %d" % [int(hp.value),max_hp]
	if max_mp > 0:
		mp.max_value = max_mp
		mp.value = clampi(int(snapshot.get("player_current_mp",0)),0,max_mp)
		mp.tooltip_text = "MP %d / %d" % [int(mp.value),max_mp]
