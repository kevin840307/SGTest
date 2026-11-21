using SGTest.Server.Visibility;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PlayerShopWireChecks
{
    public static void Verify()
    {
        Checks.Run("player shop wire binds only command choices, never actor identity",Wire);
        Checks.Run("player shop target visibility follows reliable Spawn Despawn generation",Visibility);
    }

    private static void Wire()
    {
        var cases=new[]
        {
            new PlayerShopCommandWire(3,1,PlayerShopCommandAction.Open,0,0,7001,250),
            new PlayerShopCommandWire(3,2,PlayerShopCommandAction.Close,0,91,0,0),
            new PlayerShopCommandWire(3,3,PlayerShopCommandAction.Inspect,77,0,0,0),
            new PlayerShopCommandWire(3,4,PlayerShopCommandAction.Buy,77,91,0,0)
        };
        foreach(var value in cases)
        {
            byte[] frame=WireProtocol.EncodePlayerShopCommand(value);
            Checks.Require(frame.Length==WireProtocol.PlayerShopCommandFrameBytes&&
                WireProtocol.TryDecodePlayerShopCommand(frame,out var decoded)&&
                decoded==value,"player-shop command wire mismatch");
        }

        Checks.Require(!WireProtocol.TryDecodePlayerShopCommand(
                Mutate(cases[0],seller:77),out _)&&
            !WireProtocol.TryDecodePlayerShopCommand(
                Mutate(cases[2],listing:91),out _)&&
            !WireProtocol.TryDecodePlayerShopCommand(
                Mutate(cases[3],item:7001),out _),
            "player-shop command admitted authority fields for wrong action");

        var result=new PlayerShopResultWire(
            3,4,PlayerShopCommandAction.Buy,PlayerShopResultStatus.Applied,
            77,91,7001,5101,2,501,80,250,1250,750,6);
        byte[] resultFrame=WireProtocol.EncodePlayerShopResult(result);
        Checks.Require(resultFrame.Length==WireProtocol.PlayerShopResultFrameBytes&&
            WireProtocol.TryDecodePlayerShopResult(resultFrame,out var decodedResult)&&
            decodedResult==result,"player-shop result wire mismatch");

        var sold=new PlayerShopSoldWire(3,91,77,7001,1250);
        var soldFrame=WireProtocol.EncodePlayerShopSold(sold);
        Checks.Require(soldFrame.Length==WireProtocol.PlayerShopSoldFrameBytes&&
            WireProtocol.TryDecodePlayerShopSold(soldFrame,out var decodedSold)&&
            decodedSold==sold,"player-shop seller notice wire mismatch");

        var actor=new ActorSnapshot(11,15,5,3,0,0,8,8,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedPlayerShopCommand trusted=default;
        var raw=WireProtocol.EncodePlayerShopCommand(
            new(3,9,PlayerShopCommandAction.Buy,77,91,0,0));
        Checks.Require(gateway.FeedClientBytes(
                raw,_=>true,out int accepted,
                postPlayerShop:value=>{trusted=value;return true;})==
                WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==5&&
            trusted.RequestMapEpoch==3&&trusted.ShopSequence==9&&
            trusted.SellerActorId==77&&trusted.ListingId==91,
            "player-shop gateway did not inject authenticated binding");

        var receipt=new PlayerShopResultWire(
            3,9,PlayerShopCommandAction.Buy,PlayerShopResultStatus.Applied,
            77,91,7001,5101,2,501,80,250,1250,750,6);
        Checks.Require(gateway.QueuePlayerShopResult(trusted,receipt)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var queued)&&
            WireProtocol.TryDecodePlayerShopResult(queued,out var queuedResult)&&
            queuedResult==receipt,
            "player-shop reliable receipt mismatch");
    }

    private static void Visibility()
    {
        var self=new ActorSnapshot(11,15,5,3,0,0,8,8,1,false);
        var peer=new ActorSnapshot(77,15,9,3,0,0,16,8,1,false);
        var gateway=new GatewayConnection(self);
        Checks.Require(!gateway.IsVisibleActor(peer.ActorId),
            "player-shop visibility existed before AOI Spawn");
        Checks.Require(gateway.QueueAoi(new(
                AoiEventKind.Spawn,10,self.MapEpoch,peer))==
                GatewaySendStatus.Enqueued&&
            gateway.IsVisibleActor(peer.ActorId),
            "reliable AOI Spawn did not admit visible seller");
        Checks.Require(gateway.IsVisibleActor(
                ConnectionBinding.FromTrustedSnapshot(peer)),
            "AOI Spawn did not retain exact seller generation");
        Checks.Require(gateway.QueueAoi(new(
                AoiEventKind.Despawn,11,self.MapEpoch,null,
                peer.ActorId,peer.ConnectionEpoch,
                EntityMapEpoch:peer.MapEpoch))==
                GatewaySendStatus.Enqueued&&
            !gateway.IsVisibleActor(peer.ActorId),
            "AOI Despawn did not revoke visible seller");

        var peerNext=peer with {MapEpoch=4,EntityRevision=2};
        Checks.Require(gateway.QueueAoi(new(
                AoiEventKind.Spawn,12,self.MapEpoch,peerNext))==
                GatewaySendStatus.Enqueued&&
            gateway.IsVisibleActor(ConnectionBinding.FromTrustedSnapshot(peerNext))&&
            !gateway.IsVisibleActor(ConnectionBinding.FromTrustedSnapshot(peer)),
            "AOI Spawn did not replace seller with exact new MapEpoch");
        Checks.Require(gateway.QueueAoi(new(
                AoiEventKind.Despawn,13,self.MapEpoch,null,
                peer.ActorId,peer.ConnectionEpoch,
                EntityMapEpoch:peer.MapEpoch))==
                GatewaySendStatus.Enqueued&&
            gateway.IsVisibleActor(ConnectionBinding.FromTrustedSnapshot(peerNext)),
            "stale old-generation Despawn removed current seller visibility");

        var constrained=new GatewayConnection(
            self,new GatewayConnectionOptions(MaxReliableFrames:1));
        Checks.Require(constrained.QueueAoi(new(
                AoiEventKind.WorldReady,12,self.MapEpoch,self))==
                GatewaySendStatus.Enqueued,
            "player-shop visibility fixture WorldReady failed");
        Checks.Require(constrained.QueueAoi(new(
                AoiEventKind.Spawn,13,self.MapEpoch,peer))==
                GatewaySendStatus.Backpressured&&
            !constrained.IsVisibleActor(peer.ActorId),
            "backpressured Spawn leaked seller visibility");

        var transfer=self with{MapId=13,MapEpoch=4,EntityRevision=2};
        Checks.Require(gateway.QueueAoi(new(
                AoiEventKind.Spawn,14,self.MapEpoch,peer))==
                GatewaySendStatus.Enqueued&&gateway.IsVisibleActor(peer.ActorId)&&
            gateway.TryUpdateTrustedBinding(transfer)&&
            !gateway.IsVisibleActor(peer.ActorId),
            "trusted map transfer retained old-map seller visibility");

        var router=new WorldResultRouter();
        var routed=new GatewayConnection(self);
        var routedPeer=new GatewayConnection(peer);
        Checks.Require(router.TryAttach(routed)&&router.TryAttach(routedPeer)&&
            !router.IsVisibleActor(routed.Binding,peer.ActorId),
            "router invented player-shop seller visibility");
        Checks.Require(routed.QueueAoi(new(
                AoiEventKind.Spawn,15,self.MapEpoch,peer))==
                GatewaySendStatus.Enqueued&&
            router.IsVisibleActor(
                routed.Binding,ConnectionBinding.FromTrustedSnapshot(peer)),
            "router did not expose exact current AOI visibility");
        routedPeer.Close();
        Checks.Require(!router.IsVisibleActor(
                routed.Binding,ConnectionBinding.FromTrustedSnapshot(peer)),
            "closed target binding remained exactly visible");
        routed.Close();
        Checks.Require(!router.IsVisibleActor(routed.Binding,peer.ActorId),
            "closed gateway retained player-shop visibility");
    }

    private static byte[] Mutate(
        PlayerShopCommandWire value,long? seller=null,long? listing=null,long? item=null)
    {
        var changed=value with{
            SellerActorId=seller??value.SellerActorId,
            ListingId=listing??value.ListingId,
            ItemUid=item??value.ItemUid};
        // Build raw bytes by starting from a valid action-specific frame then
        // changing fields, because Encode correctly rejects the invalid shape.
        var valid=WireProtocol.EncodePlayerShopCommand(value);
        if(seller.HasValue)
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                valid.AsSpan(21,8),seller.Value);
        if(listing.HasValue)
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                valid.AsSpan(29,8),listing.Value);
        if(item.HasValue)
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                valid.AsSpan(37,8),item.Value);
        _=changed;
        return valid;
    }
}
