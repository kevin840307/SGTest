extends SceneTree

func _init() -> void:
	var loader = preload("res://scripts/asset_pack.gd")
	var windows := loader.default_candidates("C:/Games/SGTest/SGTest.exe","Windows")
	assert(windows == ["C:/Games/SGTest/assets/assets.pck"])
	var macos := loader.default_candidates("/Games/SGTest/Stargate Reborn.app/Contents/MacOS/Stargate Reborn","macOS")
	assert(macos.has("/Games/SGTest/Stargate Reborn.app/Contents/MacOS/assets/assets.pck"))
	assert(macos.has("/Games/SGTest/Stargate Reborn.app/Contents/Resources/assets/assets.pck"))
	assert(macos.has("/Games/SGTest/assets/assets.pck"))
	print("asset_pack_paths PASS")
	quit(0)
