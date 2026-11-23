extends RefCounted
## Optional runtime bridge from SGTest-owned Pet DefinitionId/EvolutionId to
## source-backed Enemy atlases. Missing catalog is allowed; malformed catalog
## fails closed so a broken release cannot silently show the wrong Pet.
const SCHEMA := "sgtest.pet_visual_catalog/v1"

var definitions: Dictionary = {}
var action_semantics_status := ""
var source_identity_status := ""


func clear() -> void:
	definitions.clear()
	action_semantics_status = ""
	source_identity_status = ""


func load_path(path: String) -> Error:
	clear()
	if not FileAccess.file_exists(path):
		return ERR_FILE_NOT_FOUND
	var document = JSON.parse_string(FileAccess.get_file_as_string(path))
	if not document is Dictionary:
		return ERR_INVALID_DATA
	return load_document(document)


func load_document(document: Dictionary) -> Error:
	clear()
	if (
		str(document.get("schema","")) != SCHEMA or
		str(document.get("status","")) != "pass" or
		str(document.get("source_identity_status","")) != "CONFIRMED" or
		str(document.get("definition_assignment","")) != "SGTEST_OWNED" or
		bool(document.get("unknown_fields_promoted_to_gameplay",true))
	):
		return ERR_INVALID_DATA
	var semantics := str(document.get("action_semantics_status",""))
	if semantics not in ["INFERRED","CONFIRMED"]:
		return ERR_INVALID_DATA
	var raw_definitions = document.get("definitions",{})
	if not raw_definitions is Dictionary:
		return ERR_INVALID_DATA
	var parsed := {}
	for raw_definition in raw_definitions:
		var definition_id := int(raw_definition)
		var record = raw_definitions[raw_definition]
		if definition_id <= 0 or not record is Dictionary:
			return ERR_INVALID_DATA
		var raw_evolutions = record.get("evolutions",{})
		if not raw_evolutions is Dictionary or raw_evolutions.is_empty():
			return ERR_INVALID_DATA
		var evolutions := {}
		for raw_evolution in raw_evolutions:
			var evolution_id := int(raw_evolution)
			var visual = raw_evolutions[raw_evolution]
			if evolution_id < 0 or evolution_id > 65535 or not visual is Dictionary:
				return ERR_INVALID_DATA
			var asset := str(visual.get("asset",""))
			var clip := str(visual.get("clip",""))
			var idle_view := str(visual.get("idle_view",""))
			var attack_view := str(visual.get("attack_view",""))
			if (
				not _valid_asset_id(asset) or clip.is_empty() or
				idle_view.is_empty() or attack_view.is_empty() or
				int(visual.get("frame",-1)) < 0
			):
				return ERR_INVALID_DATA
			evolutions[evolution_id] = {
				"asset":asset,
				"clip":clip,
				"frame":int(visual.frame),
				"idle_view":idle_view,
				"attack_view":attack_view
			}
		parsed[definition_id] = evolutions
	definitions = parsed
	action_semantics_status = semantics
	source_identity_status = "CONFIRMED"
	return OK


func visual_for(definition_id: int, evolution_id: int) -> Dictionary:
	if not definitions.has(definition_id):
		return {}
	var evolutions: Dictionary = definitions[definition_id]
	return (evolutions.get(evolution_id,{}) as Dictionary).duplicate(true)


func asset_ids_for_snapshot(snapshot: Dictionary) -> Array:
	var ids := {}
	var entities = snapshot.get("pet_entities",{})
	if not entities is Dictionary:
		return []
	for value in entities.values():
		if not value is Dictionary:
			continue
		var visual := visual_for(
			int(value.get("definition_id",0)),int(value.get("evolution_id",-1)))
		if not visual.is_empty():
			ids[str(visual.asset)] = true
	var result: Array = ids.keys()
	result.sort()
	return result


func definitions_for_assets(asset_ids: Dictionary) -> Dictionary:
	var result := {}
	for definition_id in definitions:
		var selected := {}
		var evolutions: Dictionary = definitions[definition_id]
		for evolution_id in evolutions:
			var visual: Dictionary = evolutions[evolution_id]
			if asset_ids.has(str(visual.asset)):
				selected[evolution_id] = visual.duplicate(true)
		if not selected.is_empty():
			result[definition_id] = selected
	return result


static func _valid_asset_id(value: String) -> bool:
	if not value.begins_with("Enemy/") or value.contains("\\") or value.contains(":"):
		return false
	for part in value.split("/"):
		if part.is_empty() or part in [".",".."]:
			return false
	return true
