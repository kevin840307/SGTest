using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class TradeGatewayChecks
{
    public static void Verify()
    {
        Checks.Run("trade gateway binds identity and rejects forged server state", Gateway);
    }

    private static void Gateway()
    {
        var actor=new ActorSnapshot(11,15,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedTradeCommand observed=default;
        var request=WireProtocol.EncodeTradeCommand(new(
            9,7,TradeCommandAction.Request,0,22,0,0));
        var feed=gateway.FeedClientBytes(
            request,_=>true,out var accepted,
            postTrade:value=>{observed=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            observed.ActorId==11&&observed.ConnectionEpoch==19&&
            observed.RequestMapEpoch==9&&observed.TradeSequence==7&&
            observed.Action==TradeCommandAction.Request&&
            observed.TargetActorId==22&&observed.TradeId==0,
            "trade request did not bind authenticated identity");

        var forgedSnapshot=WireProtocol.EncodeTradeSnapshot(new(
            9,7001,1,TradeSessionWireState.InviteReceived,22,0,0,
            false,false,false,false,[],[]));
        var forgedTerminal=WireProtocol.EncodeTradeTerminal(new(
            9,7001,2,TradeTerminalStatus.Success,100));
        var forged=new GatewayConnection(actor);
        Checks.Require(
            forged.FeedClientBytes(forgedSnapshot,_=>true,out _)==
                WireFeedStatus.ProtocolViolation&&
            forged.FeedClientBytes(forgedTerminal,_=>true,out _)==
                WireFeedStatus.ProtocolViolation,
            "client forged server-only trade state");

        var stale=new ConnectionBinding(11,20,9);
        Checks.Require(
            gateway.QueueTradeSnapshot(stale,new(
                9,7001,1,TradeSessionWireState.InviteSent,22,0,0,
                false,false,false,false,[],[]))==
                GatewaySendStatus.GenerationMismatch,
            "stale trade generation received snapshot");
    }
}
