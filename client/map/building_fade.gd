extends RefCounted
## Completed fixed updates since SetInOut. Caller supplies lifecycle/time state.
## DIRECT Step 0x0055050A, Draw 0x005506CE, alpha clamp 0x005188F5.


static func layers(inside: bool, completed_updates: int) -> Dictionary:
	var remaining := 256 - 16 * clampi(completed_updates,0,16)
	return {"primary_alpha":255 if inside else mini(remaining,255),
		"alternate_alpha":mini(remaining if inside else 256 - remaining,255),
		"children":inside or remaining > 0}
