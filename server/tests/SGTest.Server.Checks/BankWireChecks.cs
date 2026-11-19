using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class BankWireChecks
{
    public static void Verify()
    {
        Checks.Run("bank wire keeps scope and balances server-owned", () =>
        {
            var open=new BankOpenWire(9,7,88);
            var openFrame=WireProtocol.EncodeBankOpen(open);
            Checks.Require(openFrame.Length==WireProtocol.BankOpenFrameBytes&&
                WireProtocol.TryDecodeBankOpen(openFrame,out var openBack)&&openBack==open,
                "bank open roundtrip");

            var snapshot=new BankSnapshotWire(
                9,7,BankOpenStatus.Opened,88,BankBalanceScope.Account,600,400);
            var snapshotFrame=WireProtocol.EncodeBankSnapshot(snapshot);
            Checks.Require(snapshotFrame.Length==WireProtocol.BankSnapshotFrameBytes&&
                WireProtocol.TryDecodeBankSnapshot(snapshotFrame,out var snapshotBack)&&
                snapshotBack==snapshot,
                "bank snapshot roundtrip");

            var deposit=new BankMoveWire(9,8,88,250);
            var depositFrame=WireProtocol.EncodeBankDeposit(deposit);
            var withdrawFrame=WireProtocol.EncodeBankWithdraw(deposit);
            Checks.Require(
                depositFrame.Length==WireProtocol.BankMoveFrameBytes&&
                withdrawFrame.Length==WireProtocol.BankMoveFrameBytes&&
                WireProtocol.TryDecodeBankDeposit(depositFrame,out var depositBack)&&
                depositBack==deposit&&
                WireProtocol.TryDecodeBankWithdraw(withdrawFrame,out var withdrawBack)&&
                withdrawBack==deposit,
                "bank amount-only mutation roundtrip");

            var result=new BankTransactionResultWire(
                9,8,BankTransactionKind.Deposit,BankTransactionStatus.Applied,
                88,BankBalanceScope.Account,250,350,650);
            var resultFrame=WireProtocol.EncodeBankTransactionResult(result);
            Checks.Require(
                resultFrame.Length==WireProtocol.BankTransactionResultFrameBytes&&
                WireProtocol.TryDecodeBankTransactionResult(resultFrame,out var resultBack)&&
                resultBack==result,
                "bank result roundtrip");
        });

        Checks.Run("bank wire rejects client scope and malformed server balances", () =>
        {
            bool badOpen=false,badSnapshot=false,badResult=false,badSettledNoScope=false;
            try{WireProtocol.EncodeBankOpen(new(0,1,88));}
            catch(ArgumentOutOfRangeException){badOpen=true;}
            try
            {
                WireProtocol.EncodeBankSnapshot(new(
                    9,1,BankOpenStatus.Opened,88,BankBalanceScope.None,0,0));
            }
            catch(ArgumentOutOfRangeException){badSnapshot=true;}
            try
            {
                WireProtocol.EncodeBankTransactionResult(new(
                    9,2,BankTransactionKind.Deposit,BankTransactionStatus.Applied,
                    88,BankBalanceScope.None,1,1,1));
            }
            catch(ArgumentOutOfRangeException){badResult=true;}
            try
            {
                WireProtocol.EncodeBankTransactionResult(new(
                    9,2,BankTransactionKind.Deposit,BankTransactionStatus.Applied,
                    88,BankBalanceScope.None,1,0,0));
            }
            catch(ArgumentOutOfRangeException){badSettledNoScope=true;}
            Checks.Require(badOpen&&badSnapshot&&badResult&&badSettledNoScope,
                "malformed bank authority payload was admitted");
        });
    }
}
