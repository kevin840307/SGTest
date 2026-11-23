extends SceneTree
const Catalog = preload("res://pet/pet_visual_catalog.gd")

func _initialize() -> void:
	var catalog := Catalog.new()
	var document := {
		"schema":"sgtest.pet_visual_catalog/v1",
		"status":"pass",
		"source_identity_status":"CONFIRMED",
		"definition_assignment":"SGTEST_OWNED",
		"action_semantics_status":"INFERRED",
		"unknown_fields_promoted_to_gameplay":false,
		"definitions":{
			"100001":{
				"source_pet_id":5100,
				"evolutions":{
					"2":{
						"asset":"Enemy/en011a",
						"clip":"EN011A_NS1",
						"frame":0,
						"idle_view":"EN011A_NS1",
						"attack_view":"EN011A_FA1"
					}
				}
			}
		}
	}
	if catalog.load_document(document) != OK:
		_fail("valid catalog rejected")
		return
	var visual := catalog.visual_for(100001,2)
	if str(visual.get("asset",""))!="Enemy/en011a" or catalog.visual_for(5100,2)!={}:
		_fail("runtime DefinitionId was confused with HK source PetID")
		return
	var snapshot := {"pet_entities":{
		1:{"definition_id":100001,"evolution_id":2},
		2:{"definition_id":999999,"evolution_id":0}
	}}
	if catalog.asset_ids_for_snapshot(snapshot)!=["Enemy/en011a"]:
		_fail("snapshot asset selection was not exact")
		return
	var selected := catalog.definitions_for_assets({"Enemy/en011a":true})
	if not selected.has(100001) or not (selected[100001] as Dictionary).has(2):
		_fail("loaded asset definition filter lost exact evolution")
		return

	var invalid := document.duplicate(true)
	invalid["source_identity_status"]="INFERRED"
	if catalog.load_document(invalid)!=ERR_INVALID_DATA or not catalog.definitions.is_empty():
		_fail("unconfirmed source identity did not fail closed")
		return
	invalid = document.duplicate(true)
	invalid["definitions"]["100001"]["evolutions"]["2"]["asset"]="../wrong"
	if catalog.load_document(invalid)!=ERR_INVALID_DATA:
		_fail("unsafe asset id accepted")
		return
	print("PET_VISUAL_CATALOG_PASS modern_definition=true exact_evolution=true fail_closed=true")
	quit(0)

func _fail(message:String)->void:
	push_error("PET_VISUAL_CATALOG_FAIL "+message)
	quit(1)
