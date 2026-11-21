using SGTest.Server.Pvp;
using SGTest.Server.Map;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class GmOnlineChecks
{
    public static void Verify(string root)
    {
        Checks.Run("online GM PvP-name wire never carries operator identity",WireAndGateway);
        Checks.Run("online GM PvP-name sequence exact replay cannot change target or state",Sequence);
        Checks.Run("online GM allowlist is authenticated and capability is server-issued",
            ()=>CapabilityTls(root).GetAwaiter().GetResult());
        Checks.Run("online GM durable audit publishes changed PvP identity to active target",
            ()=>AuditAndPublish(root).GetAwaiter().GetResult());
    }

    private static void WireAndGateway()
    {
        var capability=new GmCapabilityWire(true);
        var cap=WireProtocol.EncodeGmCapability(capability);
        Checks.Require(cap.Length==WireProtocol.GmCapabilityFrameBytes&&
            WireProtocol.TryDecodeGmCapability(cap,out var parsedCap)&&
            parsedCap==capability,"GM capability wire mismatch");

        var command=new GmPvpNameCommandWire(
            3,7,99,PvpNameState.PinkFlashing);
        byte[] bytes=WireProtocol.EncodeGmPvpNameCommand(command);
        Checks.Require(bytes.Length==29&&
            WireProtocol.TryDecodeGmPvpNameCommand(bytes,out var parsed)&&
            parsed==command,"GM PvP-name command wire mismatch");
        var result=new GmPvpNameResultWire(
            3,7,GmPvpNameResultStatus.Applied,99,PvpNameState.PinkFlashing);
        Checks.Require(WireProtocol.TryDecodeGmPvpNameResult(
            WireProtocol.EncodeGmPvpNameResult(result),out var decoded)&&
            decoded==result,"GM PvP-name result wire mismatch");

        var actor=new ActorSnapshot(88,13,5,3,0,0,8,8,1,false);
        var nonGm=new GatewayConnection(actor);
        TrustedGmPvpNameCommand captured=default;
        Checks.Require(nonGm.FeedClientBytes(
            bytes,_=>true,out int accepted,
            postGmPvpName:value=>{captured=value;return true;})==
            WireFeedStatus.Accepted&&accepted==1&&
            captured.ActorId==88&&captured.ConnectionEpoch==5&&
            captured.OperatorId is null,
            "non-GM gateway fabricated operator identity");

        var gm=new GatewayConnection(actor,gmOperatorId:"account:123");
        captured=default;
        Checks.Require(gm.GmAuthorized&&gm.FeedClientBytes(
            bytes,_=>true,out accepted,
            postGmPvpName:value=>{captured=value;return true;})==
            WireFeedStatus.Accepted&&accepted==1&&
            captured.OperatorId=="account:123"&&
            captured.TargetActorId==99&&captured.State==PvpNameState.PinkFlashing,
            "GM gateway did not inject server-owned operator identity");
        Checks.Require(gm.QueueGmPvpNameResult(captured,result)==GatewaySendStatus.Enqueued&&
            gm.TryDequeueReliable(out var queued)&&
            WireProtocol.TryDecodeGmPvpNameResult(queued,out var q)&&q==result,
            "GM reliable result missing");
    }

    private static void Sequence()
    {
        var gate=new GmPvpNameSequenceGate();
        var request=new TrustedGmPvpNameCommand(
            88,5,3,1,99,PvpNameState.Red,"account:123");
        Checks.Require(gate.Inspect(request)==GmPvpNameSequenceDecision.New,
            "first GM command rejected");
        var receipt=new GmPvpNameResultWire(
            3,1,GmPvpNameResultStatus.Applied,99,PvpNameState.Red);
        gate.Record(request,receipt);
        Checks.Require(gate.Inspect(request)==GmPvpNameSequenceDecision.ExactReplay&&
            gate.LastReceipt==receipt,
            "exact GM replay not retained");
        Checks.Require(gate.Inspect(request with {TargetActorId=100})==
                GmPvpNameSequenceDecision.Stale&&
            gate.Inspect(request with {State=PvpNameState.EnabledWhite})==
                GmPvpNameSequenceDecision.Stale,
            "same GM sequence changed target/state");
        var nextMap=request with {
            RequestMapEpoch=4,GmSequence=1,TargetActorId=100,
            State=PvpNameState.ProtectedBlue};
        Checks.Require(gate.Inspect(nextMap)==GmPvpNameSequenceDecision.New,
            "trusted new MapEpoch did not reset GM sequence");
    }

    private static async Task CapabilityTls(string root)
    {
        string dir=Path.Combine(root,"gm-cap-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string map=Checks.Fixture(dir,13,4,4,(_,_)=>0);
            await using var world=new ServerWorldHost([BlockingGrid.Load(map,13)]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(Path.Combine(dir,"identity.db"));
            await store.InitializeAsync();
            const string password="Gm-Capability-Password-123!";
            long gmAccount=(await store.CreateAccountAsync("gm_user",password)).AccountId;
            long normalAccount=(await store.CreateAccountAsync("normal_user",password)).AccountId;
            long gmChar=(await store.CreateCharacterAsync(
                gmAccount,0,"GmHero",new(13,8,8))).Character!.CharacterId;
            long normalChar=(await store.CreateCharacterAsync(
                normalAccount,0,"NormalHero",new(13,24,8))).Character!.CharacterId;
            var authorization=new IdentityTlsAuthorization(
                store,world,gmAccounts:new HashSet<string>(
                    ["GM_USER"],StringComparer.OrdinalIgnoreCase));

            await VerifyCapability(
                authorization,world,"gm_user",password,gmChar,true);
            await VerifyCapability(
                authorization,world,"normal_user",password,normalChar,false);
            await world.StopAsync();
        }
        finally{Directory.Delete(dir,true);}
    }

    private static async Task VerifyCapability(
        IdentityTlsAuthorization authorization,ServerWorldHost world,
        string username,string password,long characterId,bool expectCapability)
    {
        using var certificate=TlsSocketChecks.CreateCertificate();
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start(1);
        var accepting=listener.AcceptTcpClientAsync();
        using var client=new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndpoint).Port);
        var serverClient=await accepting;
        var serverTask=TlsWireConnection.RunServerAsync(
            serverClient,certificate,(tls,token) => authorization.AuthorizeAndBindAsync(tls,token),
            world.TryPostMove);

        var tls=TlsSocketChecks.CreateClientTls(client);
        await TlsSocketChecks.AuthenticateClient(tls);
        await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(username,password)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeAuthResult(
            await Read(tls),out var auth)&&auth.Status==AuthWireStatus.Success,
            "GM capability auth failed");
        Checks.Require(WireProtocol.TryDecodeCharacterList(
            await Read(tls),out var list)&&
            list.Characters.Single().CharacterId==characterId,
            "GM capability character list mismatch");
        await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
            await Read(tls),out var choose)&&
            choose.Status==CharacterChooseWireStatus.Success,
            "GM capability choose failed");
        byte[] next=await Read(tls);
        if(expectCapability)
            Checks.Require(WireProtocol.TryDecodeGmCapability(next,out var cap)&&
                cap.Authorized,"allowlisted account did not receive GM capability");
        else
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(next,out var inventory)&&
                inventory is not null&&inventory.Complete,
                "ordinary account received GM capability or wrong next baseline");

        await tls.DisposeAsync();
        client.Dispose();
        var ended=await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        Checks.Require(ended.Reason==TlsWireEndReason.CleanEof,
            "GM capability TLS did not close cleanly");
    }

    private static async Task AuditAndPublish(string root)
    {
        string dir=Path.Combine(root,"gm-online-audit-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var store=new SqliteIdentityStore(Path.Combine(dir,"identity.db"));
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "gm_audit","Gm-Audit-Password-123!")).AccountId;
            long target=(await store.CreateCharacterAsync(
                account,0,"Target",new(13,8,8))).Character!.CharacterId;
            var actor=new ActorSnapshot(target,13,2,1,0,0,8,8,1,false);
            var before=await store.GetCharacterPvpStateAsync(target);
            var gateway=new GatewayConnection(
                actor,displayName:"Target",pvpState:before);
            var router=new WorldResultRouter();
            Checks.Require(router.TryAttach(gateway),
                "target gateway attach failed");

            var changed=await store.ApplyPvpNameAdminAsync(
                target,PvpNameState.Red,"account:999",
                "online authenticated GM PvP-name command",
                "online-ci-event",DateTimeOffset.UtcNow);
            var sent=router.PublishPlayerPvpState(
                target,actor.ConnectionEpoch,changed.State);
            Checks.Require(sent==GatewaySendStatus.Enqueued&&
                gateway.TryGetPlayerIdentity(out var identity)&&
                identity.RedMan&&identity.Mode==PvpMode.EnabledWhite,
                "committed online GM state did not update active identity");
            Checks.Require(router.PlayerIdentityUpdates.TryRead(out var update)&&
                update.ActorId==target&&update.ConnectionEpoch==actor.ConnectionEpoch,
                "committed online GM state did not queue AOI identity refresh");

            var replay=await store.ApplyPvpNameAdminAsync(
                target,PvpNameState.Red,"account:999",
                "online authenticated GM PvP-name command",
                "online-ci-event",DateTimeOffset.UtcNow.AddSeconds(1));
            Checks.Require(replay.Replayed&&replay.State==changed.State,
                "online GM audit exact replay mutated state");
        }
        finally{Directory.Delete(dir,true);}
    }

    private static async Task<byte[]> Read(SslStream tls)
    {
        byte[] header=new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header);
        int length=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        byte[] frame=new byte[length];
        header.CopyTo(frame,0);
        if(length>header.Length)
            await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }
}
