using SGTest.Server.Wire;

internal static class TradeWireChecks
{
    public static void Verify()
    {
        Checks.Run("trade wire keeps identity ownership and settlement server-side", () =>
        {
            var request=new TradeCommandWire(
                9,1,TradeCommandAction.Request,0,22,0,0);
            var requestFrame=WireProtocol.EncodeTradeCommand(request);
            Checks.Require(requestFrame.Length==WireProtocol.TradeCommandFrameBytes&&
                WireProtocol.TryDecodeTradeCommand(requestFrame,out var requestBack)&&
                requestBack==request,"trade request roundtrip");

            var add=new TradeCommandWire(
                9,2,TradeCommandAction.AddItem,7001,0,8001,0);
            var money=new TradeCommandWire(
                9,3,TradeCommandAction.SetMoney,7001,0,0,500);
            Checks.Require(
                WireProtocol.TryDecodeTradeCommand(
                    WireProtocol.EncodeTradeCommand(add),out var addBack)&&addBack==add&&
                WireProtocol.TryDecodeTradeCommand(
                    WireProtocol.EncodeTradeCommand(money),out var moneyBack)&&moneyBack==money,
                "trade mutation commands roundtrip");

            var snapshot=new TradeSnapshotWire(
                9,7001,4,TradeSessionWireState.AwaitingFinalConfirm,22,500,100,
                true,true,false,false,
                [new TradeOfferItemWire(8001,4103,2,501,5)],
                [new TradeOfferItemWire(9001,4104,1,502,7)]);
            var snapshotFrame=WireProtocol.EncodeTradeSnapshot(snapshot);
            Checks.Require(
                snapshotFrame.Length==WireProtocol.TradeSnapshotHeaderBytes+
                    2*WireProtocol.TradeOfferItemBytes&&
                WireProtocol.TryDecodeTradeSnapshot(snapshotFrame,out var snapshotBack)&&
                snapshotBack is not null&&snapshotBack.MapEpoch==9&&
                snapshotBack.TradeId==7001&&snapshotBack.PeerActorId==22&&
                snapshotBack.MyLocked&&snapshotBack.OtherLocked&&
                snapshotBack.MyItems.SequenceEqual(snapshot.MyItems)&&
                snapshotBack.OtherItems.SequenceEqual(snapshot.OtherItems),
                "trade snapshot roundtrip");

            var terminal=new TradeTerminalWire(
                9,7001,5,TradeTerminalStatus.Success,850);
            var terminalFrame=WireProtocol.EncodeTradeTerminal(terminal);
            Checks.Require(terminalFrame.Length==WireProtocol.TradeTerminalFrameBytes&&
                WireProtocol.TryDecodeTradeTerminal(terminalFrame,out var terminalBack)&&
                terminalBack==terminal,"trade terminal roundtrip");
        });

        Checks.Run("trade wire rejects forged mixed-action payloads and duplicate offers", () =>
        {
            bool badRequest=false,badAdd=false,badSnapshot=false,badTerminal=false;
            try
            {
                WireProtocol.EncodeTradeCommand(new(
                    9,1,TradeCommandAction.Request,123,22,0,0));
            }
            catch(ArgumentOutOfRangeException){badRequest=true;}
            try
            {
                WireProtocol.EncodeTradeCommand(new(
                    9,2,TradeCommandAction.AddItem,7001,22,8001,0));
            }
            catch(ArgumentOutOfRangeException){badAdd=true;}
            try
            {
                WireProtocol.EncodeTradeSnapshot(new(
                    9,7001,1,TradeSessionWireState.Active,22,0,0,false,false,false,false,
                    [new TradeOfferItemWire(8001,4103,1,501,5)],
                    [new TradeOfferItemWire(8001,4103,1,501,5)]));
            }
            catch(ArgumentOutOfRangeException){badSnapshot=true;}
            try
            {
                WireProtocol.EncodeTradeTerminal(new(
                    9,7001,2,TradeTerminalStatus.Cancelled,100));
            }
            catch(ArgumentOutOfRangeException){badTerminal=true;}
            Checks.Require(badRequest&&badAdd&&badSnapshot&&badTerminal,
                "invalid trade authority payload was admitted");
        });
    }
}
