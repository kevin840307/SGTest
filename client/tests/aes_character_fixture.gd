extends RefCounted

const WorldState = preload("res://world/world_state.gd")
const DEFAULT_PROOF := "2468"

# Test-only migration helper for synthetic characters created before the
# mandatory secondary-character-password contract. Production never guesses
# or injects a character password: the helper uses the same authenticated
# CharacterManage command exposed to the real UI, waits for the authoritative
# CharacterList refresh, then chooses with the fixture-known proof.
static func provision_and_choose(session, character_id: int, timeout_ms: int = 7000, proof: String = DEFAULT_PROOF) -> bool:
	if character_id <= 0:
		return false
	var before := int(session.snapshot().get("character_manage_result_revision", 0))
	if not session.change_character_password(character_id, "", proof):
		return false
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var value: Dictionary = session.snapshot()
		if int(value.get("state", -1)) == WorldState.STATE_FAILED:
			return false
		var revision := int(value.get("character_manage_result_revision", 0))
		var status := int(value.get("character_manage_status", -1))
		var selecting := int(value.get("state", -1)) == WorldState.STATE_SELECTING_CHARACTER
		if revision > before and selecting:
			if status == 0:
				return session.choose_character(character_id, proof)
			# InvalidPassword means this synthetic character was already
			# migrated by an earlier login. Keep the credential unchanged and
			# prove the fixture-known value on CharacterChoose.
			if status == 2:
				return session.choose_character(character_id, proof)
			return false
		OS.delay_msec(2)
	return false
