extends RefCounted
## Classifies a decoded message against the visible actor or party membership.
## The caller owns epoch validation, replay counters, and the message history.

const STALE := "stale"


static func check_sender(
	message: Dictionary, actor_id: int, sent_sequence: int,
	entities: Dictionary, party_id: int, party_members: Dictionary
) -> String:
	var sender := int(message.sender_actor_id)
	var connection := int(message.sender_connection_epoch)
	var sequence := int(message.chat_sequence)
	match int(message.channel):
		0:
			if sender == actor_id:
				return "chat_self_delivery_without_send" if sequence > sent_sequence else ""
			return "" if _matches_connection(entities, sender, connection) else STALE
		3:
			if party_id <= 0:
				return STALE
			if sender == actor_id:
				return "party_chat_self_delivery_without_send" if sequence > sent_sequence else ""
			return "" if _matches_connection(party_members, sender, connection) else STALE
		4:
			# A direct message can cross maps and the sender may be outside AOI.
			return "whisper_delivery_from_self" if sender == actor_id else ""
		_:
			return "unsupported_chat_delivery_channel"


static func _matches_connection(members: Dictionary, sender: int, connection: int) -> bool:
	return members.has(sender) and int(members[sender].get("connection_epoch", 0)) == connection
