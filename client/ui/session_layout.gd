extends VBoxContainer
## Pure presentation composition: four screens and swappable gameplay layout.
## All actual controls are owned by SessionPanel; no alternate gameplay paths,
## sessions, reducers or command handlers are created per skin.

enum Page { LOGIN, CHARACTER_SELECT, CHARACTER_CREATE, WORLD }

var login_page := VBoxContainer.new()
var select_page := VBoxContainer.new()
var create_page := VBoxContainer.new()
var world_page := VBoxContainer.new()
var world_tabs := TabContainer.new()
var world_stack := VBoxContainer.new()
var current_page := Page.LOGIN
var layout_id := "tabs"
var _groups: Array[VBoxContainer] = []
var _login_grid: GridContainer
var _create_grid: GridContainer

func _init() -> void:
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for pair in [
		[login_page, "LoginPage"],
		[select_page, "CharacterSelectPage"],
		[create_page, "CharacterCreatePage"],
		[world_page, "WorldPage"],
	]:
		var screen: VBoxContainer = pair[0]
		screen.name = str(pair[1])
		screen.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		screen.add_theme_constant_override("separation",8)
		add_child(screen)
	world_tabs.name = "GameplayTabs"
	world_tabs.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	world_stack.name = "GameplayStack"
	world_stack.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var hud_title := Label.new()
	hud_title.text = "遊戲選單"
	world_page.add_child(hud_title)
	world_page.add_child(world_tabs)
	world_page.add_child(world_stack)
	world_stack.hide()
	show_page(Page.LOGIN)

func bind_forms(login_grid: GridContainer, create_grid: GridContainer) -> void:
	_login_grid = login_grid
	_create_grid = create_grid
	_set_form_geometry()

func _set_form_geometry() -> void:
	if _login_grid != null:
		_login_grid.columns = 2 if layout_id == "tabs" else 1
	if _create_grid != null:
		_create_grid.columns = 2 if layout_id == "tabs" else 1
	for page in [login_page,select_page,create_page,world_page]:
		page.add_theme_constant_override("separation",12 if layout_id == "tabs" else 6)

func show_page(page: int) -> bool:
	if page < Page.LOGIN or page > Page.WORLD:
		return false
	current_page = page
	login_page.visible = page == Page.LOGIN
	select_page.visible = page == Page.CHARACTER_SELECT
	create_page.visible = page == Page.CHARACTER_CREATE
	world_page.visible = page == Page.WORLD
	return true

func add_group(label: String, controls: Array) -> void:
	var page := VBoxContainer.new()
	page.name = "GameplayGroup%d" % _groups.size()
	page.set_meta("caption",label)
	page.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	page.add_theme_constant_override("separation",8)
	if layout_id == "tabs":
		world_tabs.add_child(page)
		world_tabs.set_tab_title(world_tabs.get_tab_count()-1,label)
	else:
		world_stack.add_child(page)
		var title := Label.new()
		title.name = "SectionHeading"
		title.text = label
		page.add_child(title)
	for control in controls:
		page.add_child(control)
	_groups.append(page)

func set_layout(id: String) -> bool:
	if id not in ["tabs","stacked"]:
		return false
	if layout_id != id:
		for index in _groups.size():
			var group := _groups[index]
			var target: Control = world_tabs if id == "tabs" else world_stack
			if group.get_parent() != target:
				group.reparent(target)
			# TabContainer owns its own tab label. Stacked mode uses a section
			# header, but no gameplay control is destroyed or rebuilt.
			var heading: Label = group.get_node_or_null("SectionHeading")
			if id == "tabs":
				if heading != null:
					heading.visible = false
				world_tabs.set_tab_title(index,group.get_meta("caption","操作"))
			else:
				if heading == null:
					heading = Label.new()
					heading.name = "SectionHeading"
					heading.text = str(group.get_meta("caption","操作"))
					group.add_child(heading)
					group.move_child(heading,0)
				heading.visible = true
		layout_id = id
	world_tabs.visible = id == "tabs"
	world_stack.visible = id == "stacked"
	_set_form_geometry()
	return true

func group_count() -> int:
	return _groups.size()
