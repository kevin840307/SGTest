using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PvpIdentityChecks
{
    public static void Verify(string root)
    {
        Checks.Run("player identity wire carries trusted name and PvP deadline",
            WireGateway);
        Checks.Run("player identity AOI fanout follows exact visible generation",
            AoiFanout);
        Checks.Run("router publishes persisted PvP state without client authority",
            RouterPublication);
    }

    private static void WireGateway()
    {
        long server=2_200_000_000_000L;
        var state=new CharacterPvpState(
            88,PvpMode.EnabledWhite,false,0,server+30_000,server-10);
        var actor=Actor(88,5,3,8,8);
        var gateway=new GatewayConnection(
            actor,displayName:"測試玩家",pvpState:state);
        Checks.Require(gateway.TryGetPlayerIdentity(out var identity)&&
            identity.ActorId==88&&identity.ConnectionEpoch==5&&
            identity.EntityMapEpoch==3&&identity.DisplayName=="測試玩家"&&
            identity.Mode==PvpMode.EnabledWhite&&!identity.RedMan&&
            identity.PinkUntilUnixMilliseconds==server+30_000,
            "Gateway trusted player identity missing");
        var deterministic=identity with { ServerUnixMilliseconds=server };
        Checks.Require(gateway.QueueAoi(new(
            AoiEventKind.PlayerIdentity,10,actor.MapEpoch,
            PlayerIdentity:deterministic))==GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var frame)&&
            frame.Length>WireProtocol.PlayerIdentityHeaderBytes&&
            WireProtocol.TryDecodePlayerIdentity(frame,out var decoded)&&
            decoded.ReceiverMapEpoch==3&&decoded.ActorId==88&&
            decoded.ConnectionEpoch==5&&decoded.EntityMapEpoch==3&&
            decoded.Name=="測試玩家"&&decoded.Mode==PvpMode.EnabledWhite&&
            !decoded.RedMan&&decoded.ServerUnixMilliseconds==server&&
            decoded.PinkUntilUnixMilliseconds==server+30_000&&
            decoded.PvpUpdatedUnixMilliseconds==server-10,
            "PlayerIdentity compact wire mismatch");

        var red=state with
        {
            RedMan=true,
            PinkUntilUnixMilliseconds=0,
            UpdatedUnixMilliseconds=server+1
        };
        Checks.Require(gateway.TryUpdatePlayerPvpState(red)&&
            gateway.TryGetPlayerIdentity(out var redIdentity)&&
            redIdentity.RedMan&&redIdentity.PinkUntilUnixMilliseconds==0,
            "Gateway PvP presentation state did not update");
    }

    private static void AoiFanout()
    {
        var a=Actor(101,1,1,8,8);
        var b=Actor(102,1,1,24,8);
        var far=Actor(103,1,1,4000,4000);
        var aoi=new WorldAoi();
        aoi.Apply(new WorldChangedBatch(1,Array.Empty<ActorSnapshot>(),
            [new(ActorLifecycleKind.Joined,a),
             new(ActorLifecycleKind.Joined,b),
             new(ActorLifecycleKind.Joined,far)]));
        while(aoi.TryDequeue(a.ActorId,a.ConnectionEpoch,a.MapEpoch,out _)){}
        while(aoi.TryDequeue(b.ActorId,b.ConnectionEpoch,b.MapEpoch,out _)){}
        while(aoi.TryDequeue(far.ActorId,far.ConnectionEpoch,far.MapEpoch,out _)){}

        var identity=new PlayerIdentityPresentation(
            b.ActorId,b.ConnectionEpoch,b.MapEpoch,"VisibleB",
            PvpMode.ProtectedBlue,false,2_200_000_000_000L,0,0);
        int queued=aoi.EnqueuePlayerIdentity(identity);
        Checks.Require(queued==2,
            "identity did not fan out to self plus visible peer only");
        Checks.Require(aoi.TryDequeue(
                a.ActorId,a.ConnectionEpoch,a.MapEpoch,out var seen)&&
            seen.Kind==AoiEventKind.PlayerIdentity&&
            seen.PlayerIdentity==identity,
            "visible peer identity not queued");
        Checks.Require(aoi.TryDequeue(
                b.ActorId,b.ConnectionEpoch,b.MapEpoch,out var self)&&
            self.Kind==AoiEventKind.PlayerIdentity&&
            self.PlayerIdentity==identity,
            "self identity not queued");
        Checks.Require(!aoi.TryDequeue(
                far.ActorId,far.ConnectionEpoch,far.MapEpoch,out _),
            "far observer received hidden identity");
    }

    private static void RouterPublication()
    {
        var actor=Actor(201,7,4,8,8);
        var state=new CharacterPvpState(
            actor.ActorId,PvpMode.ProtectedBlue,false,0,0,0);
        var gateway=new GatewayConnection(
            actor,displayName:"RouterHero",pvpState:state);
        var router=new WorldResultRouter(
            new WorldResultRouterOptions(MaxSessions:4));
        Checks.Require(router.TryAttach(gateway)&&
            router.PlayerIdentityUpdates.TryRead(out var attach)&&
            attach.ActorId==actor.ActorId&&
            attach.ConnectionEpoch==actor.ConnectionEpoch,
            "router attach did not publish identity invalidation");
        var red=state with
        {
            Mode=PvpMode.EnabledWhite,RedMan=true,
            UpdatedUnixMilliseconds=2_200_000_000_100L
        };
        Checks.Require(router.PublishPlayerPvpState(
                actor.ActorId,actor.ConnectionEpoch,red)==
                GatewaySendStatus.Enqueued&&
            router.PlayerIdentityUpdates.TryRead(out var update)&&
            update.ActorId==actor.ActorId&&
            router.TryGetPlayerIdentity(
                actor.ActorId,actor.ConnectionEpoch,out var identity)&&
            identity.RedMan&&identity.PvpUpdatedUnixMilliseconds==
                red.UpdatedUnixMilliseconds,
            "router committed PvP identity update missing");
    }

    private static ActorSnapshot Actor(
        long id,long connection,long mapEpoch,double x,double y)=>
        new(id,13,connection,mapEpoch,0,0,x,y,1,false,1,false);
}
