extends SceneTree
const Wire = preload("res://network/wire_codec.gd")
const World = preload("res://network/world_state.gd")
const AUTH := "0900000001000B0000"
const LIST := "1800000001000C00010B000000000000000004004865726F"
const CHOOSE := "2D00000001000E00000B000000000000000F000000000000000000204000000000000020400100000000000000"
const READY := "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101"
const MAP13_READY := "2D00000001001400020000000000000032000000000000000B000000000000000D000000000020420000A04201"
const SNAPSHOT := "2A00000001003E00010000000000000001000000005800000002E8030000000000000000000000000000"
const DEPOSIT := "330000000100410001000000000000000200000001005800000002900100000000000058020000000000009001000000000000"
const WITHDRAW := "3300000001004100010000000000000003000000020058000000029600000000000000EE02000000000000FA00000000000000"

func fail(message: String) -> void:
	push_error("BANK_WIRE_FAIL "+message)
	quit(1)

func new_world():
	var world = World.new()
	if not world.mark_auth_sent() or not world.apply_frame(AUTH.hex_decode()) or not world.apply_frame(LIST.hex_decode()) or not world.mark_choose_sent(11) or not world.apply_frame(CHOOSE.hex_decode()) or not world.apply_frame(READY.hex_decode()) or not world.mark_world_ready_ack_sent(1,42):
		return null
	return world

func _initialize() -> void:
	var open := Wire.encode_bank_open(1,1,88)
	var dep := Wire.encode_bank_deposit(1,2,88,400)
	var wd := Wire.encode_bank_withdraw(1,3,88,150)
	if open.size()!=24 or dep.size()!=32 or wd.size()!=32:
		fail("bank request size mismatch")
		return
	var snap := Wire.decode_bank_snapshot(SNAPSHOT.hex_decode())
	var result := Wire.decode_bank_transaction_result(DEPOSIT.hex_decode())
	if snap.is_empty() or int(snap.scope)!=2 or int(snap.wallet_money)!=1000 or int(snap.bank_money)!=0 or result.is_empty() or int(result.amount)!=400 or int(result.wallet_money)!=600 or int(result.bank_money)!=400:
		fail("CSharp bank golden mismatch")
		return

	var world = new_world()
	if world == null or not world.mark_bank_sent(1,1,88,0) or not world.apply_frame(SNAPSHOT.hex_decode()):
		fail("bank open baseline")
		return
	var state: Dictionary = world.snapshot()
	if int(state.active_bank_npc)!=88 or int(state.bank_scope)!=2 or int(state.bank_wallet_money)!=1000 or int(state.bank_money)!=0:
		fail("bank open did not establish authoritative balances")
		return

	world.active_shop_npc = 99
	world.shop_money = 1000
	if not world.mark_bank_sent(2,2,88,400) or not world.apply_frame(DEPOSIT.hex_decode()):
		fail("bank deposit settlement")
		return
	state = world.snapshot()
	if int(state.bank_wallet_money)!=600 or int(state.bank_money)!=400 or int(state.shop_money)!=600 or int(state.last_bank_result.status)!=0:
		fail("bank deposit did not converge server balances")
		return
	var stale := int(state.stale_bank_results)
	if not world.apply_frame(DEPOSIT.hex_decode()) or int(world.snapshot().stale_bank_results)!=stale+1 or int(world.snapshot().bank_money)!=400:
		fail("duplicate bank result mutated twice")
		return

	if not world.mark_bank_sent(3,3,88,150) or not world.apply_frame(WITHDRAW.hex_decode()):
		fail("bank withdraw settlement")
		return
	state = world.snapshot()
	if int(state.bank_wallet_money)!=750 or int(state.bank_money)!=250 or int(state.shop_money)!=750:
		fail("bank withdraw authoritative balances mismatch")
		return

	# AlreadyApplied is a durable replay-recovery result: a fresh pending request
	# must converge directly to the committed balances without client arithmetic.
	world = new_world()
	if world == null or not world.mark_bank_sent(1,1,88,0) or not world.apply_frame(SNAPSHOT.hex_decode()) or not world.mark_bank_sent(2,2,88,400):
		fail("bank replay recovery fixture")
		return
	var replay := DEPOSIT.hex_decode()
	replay[21] = 1
	if not world.apply_frame(replay) or int(world.snapshot().bank_wallet_money)!=600 or int(world.snapshot().bank_money)!=400 or int(world.snapshot().last_bank_result.status)!=1:
		fail("AlreadyApplied did not recover bank balances")
		return

	# Access failure carries no client-usable scope/balances and closes the session.
	if not world.mark_bank_sent(3,2,88,1):
		fail("bank access failure request fixture")
		return
	var denied := DEPOSIT.hex_decode()
	denied.encode_u32(16,3)
	denied[21] = 3
	denied[26] = 0
	denied.encode_s64(27,1)
	denied.encode_s64(35,0)
	denied.encode_s64(43,0)
	if not world.apply_frame(denied):
		fail("bank access failure receipt")
		return
	state = world.snapshot()
	if int(state.active_bank_npc)!=0 or int(state.bank_scope)!=0 or int(state.bank_money)!=0:
		fail("bank access failure left stale session open")
		return

	world = new_world()
	if world == null or not world.mark_bank_sent(1,1,88,0) or not world.apply_frame(SNAPSHOT.hex_decode()):
		fail("map transfer bank fixture")
		return
	if not world.apply_frame(MAP13_READY.hex_decode()) or not world.mark_world_ready_ack_sent(2,50):
		fail("map transfer/ack")
		return
	state = world.snapshot()
	if int(state.active_bank_npc)!=0 or int(state.bank_scope)!=0 or int(state.bank_sequence)!=0 or int(state.pending_bank_sequence)!=0:
		fail("map transfer retained stale bank session")
		return

	world = new_world()
	if world == null or not world.mark_bank_sent(1,1,88,0) or not world.apply_frame(SNAPSHOT.hex_decode()) or not world.mark_bank_sent(2,2,88,400):
		fail("wrong bank receipt fixture")
		return
	var wrong := DEPOSIT.hex_decode()
	wrong.encode_s64(27,401)
	if world.apply_frame(wrong) or int(world.state)!=World.STATE_FAILED:
		fail("wrong bank amount receipt accepted")
		return

	print("BANK_WIRE_PASS open=24 snapshot=42 move=32 result=51 scope=SERVER_RULE balances=SERVER_AUTH replay=STALE replay_recovery=ALREADY_APPLIED_CONVERGES shop_wallet=SYNC transfer=CLOSES spoof=FAIL_CLOSED")
	quit(0)
