extends SceneTree

const WireCodec=preload("res://network/wire_codec.gd")
const WorldState=preload("res://world/world_state.gd")

func _fail(reason:String)->void:
	push_error("PLAYER_SHOP_WIRE_FAIL "+reason)
	quit(1)

func _result(
		epoch:int,sequence:int,action:int,status:int,seller:int,listing:int,
		uid:int,item_id:int,quantity:int,source_drop:int,durability:int,
		price:int,seller_wallet:int,buyer_wallet:int,enhancement:int=0)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(WireCodec.PLAYER_SHOP_RESULT_FRAME_BYTES)
	writer.put_u16(WireCodec.VERSION)
	writer.put_u16(WireCodec.OPCODE_PLAYER_SHOP_RESULT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_u8(action)
	writer.put_u8(status)
	writer.put_64(seller)
	writer.put_64(listing)
	writer.put_64(uid)
	writer.put_32(item_id)
	writer.put_32(quantity)
	writer.put_64(source_drop)
	writer.put_32(durability)
	writer.put_64(price)
	writer.put_64(seller_wallet)
	writer.put_64(buyer_wallet)
	writer.put_u8(enhancement)
	return writer.data_array

func _snapshot(
		epoch:int,sequence:int,seller:int,wallet:int,
		rows:Array[Dictionary])->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(
		WireCodec.PLAYER_SHOP_SNAPSHOT_HEADER_BYTES+
		rows.size()*WireCodec.PLAYER_SHOP_LISTING_BYTES)
	writer.put_u16(WireCodec.VERSION)
	writer.put_u16(WireCodec.OPCODE_PLAYER_SHOP_SNAPSHOT)
	writer.put_64(epoch)
	writer.put_u32(sequence)
	writer.put_64(seller)
	writer.put_64(wallet)
	writer.put_u8(rows.size())
	for row in rows:
		writer.put_64(int(row.listing_id))
		writer.put_64(int(row.item_uid))
		writer.put_32(int(row.item_definition_id))
		writer.put_32(int(row.quantity))
		writer.put_64(int(row.source_drop_id))
		writer.put_32(int(row.durability_raw))
		writer.put_64(int(row.price))
		writer.put_u8(int(row.enhancement_level))
	return writer.data_array


func _sold(epoch:int,listing:int,buyer:int,uid:int,wallet:int)->PackedByteArray:
	var writer:=StreamPeerBuffer.new()
	writer.big_endian=false
	writer.put_u32(WireCodec.PLAYER_SHOP_SOLD_FRAME_BYTES)
	writer.put_u16(WireCodec.VERSION)
	writer.put_u16(WireCodec.OPCODE_PLAYER_SHOP_SOLD)
	writer.put_64(epoch)
	writer.put_64(listing)
	writer.put_64(buyer)
	writer.put_64(uid)
	writer.put_64(wallet)
	return writer.data_array

func _base_world(actor:int,peer:int)->WorldState:
	var world:=WorldState.new()
	world.state=WorldState.STATE_IN_WORLD
	world.actor_id=actor
	world.map_id=15
	world.map_epoch=3
	world.world_ready=true
	world.inventory_ready=true
	world.last_world_ready_ack_epoch=3
	world.entities[peer]={
		"actor_id":peer,"connection_epoch":8,"entity_map_epoch":3,
		"map_id":15,"revision":1}
	return world

func _initialize()->void:
	var open:=WireCodec.encode_player_shop_command(3,1,1,0,0,7001,300)
	var close:=WireCodec.encode_player_shop_command(3,2,2,0,91,0,0)
	var inspect:=WireCodec.encode_player_shop_command(3,3,3,22,0,0,0)
	var buy:=WireCodec.encode_player_shop_command(3,4,4,22,91,0,0)
	if (
		open.size()!=53 or close.size()!=53 or inspect.size()!=53 or buy.size()!=53 or
		WireCodec.frame_opcode(open)!=WireCodec.OPCODE_PLAYER_SHOP_COMMAND
	):
		_fail("command codec/size")
		return
	if (
		not WireCodec.encode_player_shop_command(3,1,1,22,0,7001,300).is_empty() or
		not WireCodec.encode_player_shop_command(3,1,3,22,91,0,0).is_empty() or
		not WireCodec.encode_player_shop_command(3,1,4,22,91,7001,0).is_empty()
	):
		_fail("action-specific command authority validation")
		return
	var decoded:=WireCodec.decode_player_shop_result(
		_result(3,4,4,0,22,91,7001,6101,2,5001,80,300,400,700,5))
	if (
		decoded.is_empty() or int(decoded.item_uid)!=7001 or
		int(decoded.durability_raw)!=80 or int(decoded.buyer_wallet)!=700 or
		int(decoded.enhancement_level)!=5
	):
		_fail("result codec durability/provenance")
		return
	var decoded_sold:=WireCodec.decode_player_shop_sold(_sold(3,91,11,7001,400))
	if decoded_sold.is_empty() or int(decoded_sold.seller_wallet)!=400:
		_fail("seller notice codec")
		return

	# Seller: opening a listing never removes the UID. Only the committed
	# seller-side Sold push removes it and updates the authoritative wallet.
	var seller:=_base_world(22,11)
	seller.inventory_items[7001]={
		"item_uid":7001,"item_definition_id":6101,
		"quantity":2,"drop_id":5001,"durability_raw":80,
		"enhancement_level":5}
	seller.inventory_items[7002]={
		"item_uid":7002,"item_definition_id":6102,
		"quantity":1,"drop_id":5002,"durability_raw":-1,
		"enhancement_level":0}
	if not seller.mark_player_shop_sent(1,1,0,0,7001,300):
		_fail("seller open admission")
		return
	if seller.inventory_items.has(7001)==false:
		_fail("open optimistically removed seller UID")
		return
	if not seller.apply_frame(
		_result(3,1,1,0,22,91,7001,6101,2,5001,80,300,100,100,5)):
		_fail("seller open receipt "+seller.protocol_error)
		return
	var seller_snap:=seller.snapshot()
	if (
		int(seller_snap.player_shop_own_listing.get("listing_id",0))!=91 or
		not (seller_snap.inventory_items as Dictionary).has(7001) or
		int(seller_snap.player_shop_wallet)!=100
	):
		_fail("seller open state")
		return
	if not seller.mark_player_shop_sent(2,1,0,0,7002,125):
		_fail("seller second open admission")
		return
	if not seller.apply_frame(
		_result(3,2,1,0,22,92,7002,6102,1,5002,-1,125,100,100,0)):
		_fail("seller second open receipt "+seller.protocol_error)
		return
	seller_snap=seller.snapshot()
	if (
		(seller_snap.player_shop_own_listings as Dictionary).size()!=2 or
		not (seller_snap.player_shop_own_listings as Dictionary).has(91) or
		not (seller_snap.player_shop_own_listings as Dictionary).has(92)
	):
		_fail("seller multi-listing state")
		return
	if not seller.apply_frame(_sold(3,91,11,7001,400)):
		_fail("seller sold reducer "+seller.protocol_error)
		return
	seller_snap=seller.snapshot()
	if (
		(seller_snap.inventory_items as Dictionary).has(7001) or
		not (seller_snap.inventory_items as Dictionary).has(7002) or
		int(seller_snap.player_shop_own_listing.get("listing_id",0))!=92 or
		(seller_snap.player_shop_own_listings as Dictionary).size()!=1 or
		not (seller_snap.player_shop_own_listings as Dictionary).has(92) or
		int(seller_snap.player_shop_wallet)!=400 or
		int(seller_snap.last_player_shop_sold.get("buyer_actor_id",0))!=11
	):
		_fail("seller sale did not preserve remaining multi-listing stall")
		return
	if (
		not seller.apply_frame(_sold(3,91,11,7001,400)) or
		int(seller.snapshot().stale_player_shop_frames)!=1
	):
		_fail("duplicate seller sold was not stale")
		return

	# Buyer: inspect is presentation state only. Buy mutates inventory only after
	# an authenticated Applied result containing the exact durable UID snapshot.
	var buyer_world:=_base_world(11,22)
	if not buyer_world.mark_player_shop_sent(1,3,22,0,0,0):
		_fail("buyer inspect admission")
		return
	var inspect_rows:Array[Dictionary]=[
		{
			"listing_id":91,"item_uid":7001,"item_definition_id":6101,
			"quantity":2,"source_drop_id":5001,"durability_raw":80,
			"price":300,"enhancement_level":5
		},
		{
			"listing_id":92,"item_uid":7002,"item_definition_id":6102,
			"quantity":1,"source_drop_id":5002,"durability_raw":-1,
			"price":125,"enhancement_level":0
		}
	]
	var snapshot_frame:=_snapshot(3,1,22,1000,inspect_rows)
	var decoded_snapshot:=WireCodec.decode_player_shop_snapshot(snapshot_frame)
	if (
		decoded_snapshot.is_empty() or
		(decoded_snapshot.listings as Array).size()!=2 or
		int((decoded_snapshot.listings as Array)[1].listing_id)!=92
	):
		_fail("multi-listing snapshot codec")
		return
	if not buyer_world.apply_frame(snapshot_frame):
		_fail("inspect snapshot reducer "+buyer_world.protocol_error)
		return
	if not buyer_world.apply_frame(
		_result(3,1,3,0,22,91,7001,6101,2,5001,80,300,0,1000,5)):
		_fail("inspect reducer "+buyer_world.protocol_error)
		return
	var buyer_snap:=buyer_world.snapshot()
	if (
		int(buyer_snap.player_shop_inspected.get("listing_id",0))!=91 or
		(buyer_snap.player_shop_inspected_listings as Dictionary).size()!=2 or
		not (buyer_snap.player_shop_inspected_listings as Dictionary).has(92) or
		int(buyer_snap.player_shop_wallet)!=1000 or
		int(buyer_snap.player_shop_inspected.get("enhancement_level",-1))!=5 or
		(buyer_snap.inventory_items as Dictionary).has(7001)
	):
		_fail("inspect mutated inventory or lost multi-listing catalog")
		return

	# If another buyer wins one listing after our inspect, only that listing is
	# removed; the rest of the inspected stall remains usable.
	var race_world:=_base_world(33,22)
	if not race_world.mark_player_shop_sent(1,3,22,0,0,0):
		_fail("race inspect admission")
		return
	if not race_world.apply_frame(snapshot_frame) or not race_world.apply_frame(
		_result(3,1,3,0,22,91,7001,6101,2,5001,80,300,0,1000,5)):
		_fail("race inspect setup "+race_world.protocol_error)
		return
	if not race_world.mark_player_shop_sent(2,4,22,91,0,0):
		_fail("race buy admission")
		return
	if not race_world.apply_frame(
		_result(3,2,4,12,22,91,7001,6101,2,5001,80,300,400,1000,5)):
		_fail("race sold reducer "+race_world.protocol_error)
		return
	var race_snap:=race_world.snapshot()
	if (
		int(race_snap.player_shop_inspected.get("listing_id",0))!=92 or
		(race_snap.player_shop_inspected_listings as Dictionary).size()!=1 or
		not (race_snap.player_shop_inspected_listings as Dictionary).has(92)
	):
		_fail("sold competitor listing cleared remaining inspected stall")
		return

	if not buyer_world.mark_player_shop_sent(2,4,22,91,0,0):
		_fail("buyer buy admission")
		return
	if (buyer_world.snapshot().inventory_items as Dictionary).has(7001):
		_fail("buy optimistically created UID")
		return
	if not buyer_world.apply_frame(
		_result(3,2,4,0,22,91,7001,6101,2,5001,80,300,400,700,5)):
		_fail("buy reducer "+buyer_world.protocol_error)
		return
	buyer_snap=buyer_world.snapshot()
	var purchased:Dictionary=(buyer_snap.inventory_items as Dictionary).get(7001,{})
	if (
		purchased.is_empty() or int(purchased.item_definition_id)!=6101 or
		int(purchased.quantity)!=2 or int(purchased.drop_id)!=5001 or
		int(purchased.durability_raw)!=80 or int(purchased.enhancement_level)!=5 or
		int(buyer_snap.player_shop_wallet)!=700 or
		int(buyer_snap.player_shop_inspected.get("listing_id",0))!=92 or
		(buyer_snap.player_shop_inspected_listings as Dictionary).size()!=1 or
		not (buyer_snap.player_shop_inspected_listings as Dictionary).has(92)
	):
		_fail("buy lost UID metadata/wallet or closed remaining inspected product")
		return
	if (
		not buyer_world.apply_frame(
			_result(3,2,4,0,22,91,7001,6101,2,5001,80,300,400,700,5)) or
		int(buyer_world.snapshot().stale_player_shop_frames)!=1
	):
		_fail("duplicate buyer receipt not stale")
		return

	# Old-map results are harmless stale frames. Future-map results fail closed.
	var old:=buyer_world.snapshot()
	if not buyer_world.apply_frame(
		_result(2,9,3,2,22,0,0,0,0,0,-1,0,0,0)):
		_fail("old map result should be stale")
		return
	if int(buyer_world.snapshot().stale_player_shop_frames)!=2:
		_fail("old map result stale counter")
		return
	var future:=_base_world(31,32)
	future.inventory_items[8001]={"item_uid":8001,"item_definition_id":6201,"quantity":1,"drop_id":0}
	if not future.mark_player_shop_sent(1,1,0,0,8001,50):
		_fail("future fixture admission")
		return
	if (
		future.apply_frame(
			_result(4,1,1,0,31,100,8001,6201,1,0,-1,50,50,50)) or
		int(future.state)!=WorldState.STATE_FAILED
	):
		_fail("future map player-shop result did not fail closed")
		return

	print("PLAYER_SHOP_WIRE_PASS command=53 result=91 sold=48 snapshot_opcode=127 multi_listing=true action_shape=true server_auth_uid=true inspect_no_inventory_mutation=true buy_uid_provenance=true durability=80 enhancement=5 seller_sold_push=true preserves_remaining_listing=true duplicate_stale=true future_failclosed=true")
	quit(0)
