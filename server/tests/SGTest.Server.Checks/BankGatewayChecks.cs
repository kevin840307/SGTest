using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class BankGatewayChecks
{
    public static void Verify()
    {
        Checks.Run("bank gateway binds identity and rejects forged balance state", WireAndGateway);
    }

    private static void WireAndGateway()
    {
        var actor=new ActorSnapshot(11,13,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);

        TrustedBankCommand open=default;
        var openFrame=WireProtocol.EncodeBankOpen(new(9,7,88));
        var feed=gateway.FeedClientBytes(openFrame,_=>true,out var accepted,
            postBank:value=>{open=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            open.ActorId==11&&open.ConnectionEpoch==19&&
            open.RequestMapEpoch==9&&open.BankSequence==7&&
            open.NpcSpawnId==88&&open.Kind==TrustedBankCommandKind.Open&&open.Amount==0,
            "bank open did not bind authenticated identity");

        var balance=new BankBalanceSnapshot(BankBalanceScope.Account,600,400);
        Checks.Require(
            gateway.QueueBankSnapshot(open,BankOpenStatus.Opened,balance)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var snapshotFrame)&&
            WireProtocol.TryDecodeBankSnapshot(snapshotFrame,out var snapshot)&&
            snapshot.Scope==BankBalanceScope.Account&&
            snapshot.WalletMoney==600&&snapshot.BankMoney==400,
            "bank snapshot did not enter reliable FIFO");

        TrustedBankCommand deposit=default;
        var depositFrame=WireProtocol.EncodeBankDeposit(new(9,8,88,250));
        feed=gateway.FeedClientBytes(depositFrame,_=>true,out accepted,
            postBank:value=>{deposit=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            deposit.ActorId==11&&deposit.ConnectionEpoch==19&&
            deposit.Kind==TrustedBankCommandKind.Deposit&&deposit.Amount==250,
            "bank deposit trusted context mismatch");

        var result=new BankTransactionResult(
            BankTransactionStatus.Applied,BankTransactionKind.Deposit,88,
            BankBalanceScope.Account,250,350,650);
        Checks.Require(
            gateway.QueueBankTransactionResult(deposit,result)==
                GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var resultFrame)&&
            WireProtocol.TryDecodeBankTransactionResult(resultFrame,out var decoded)&&
            decoded.Scope==BankBalanceScope.Account&&decoded.Amount==250&&
            decoded.WalletMoney==350&&decoded.BankMoney==650,
            "bank result did not enter reliable FIFO");

        var forged=new GatewayConnection(actor);
        Checks.Require(
            forged.FeedClientBytes(
                WireProtocol.EncodeBankSnapshot(new(
                    9,1,BankOpenStatus.Opened,88,
                    BankBalanceScope.Account,999,999)),
                _=>true,out _)==WireFeedStatus.ProtocolViolation&&
            forged.FeedClientBytes(
                WireProtocol.EncodeBankTransactionResult(new(
                    9,1,BankTransactionKind.Deposit,BankTransactionStatus.Applied,
                    88,BankBalanceScope.Account,1,999,999)),
                _=>true,out _)==WireFeedStatus.ProtocolViolation,
            "client forged server-only bank balance/result");

        Checks.Require(
            gateway.QueueBankTransactionResult(
                deposit with{ConnectionEpoch=20},result)==
                GatewaySendStatus.GenerationMismatch,
            "stale bank generation received receipt");
    }
}
