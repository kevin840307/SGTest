using Microsoft.Data.Sqlite;
using SGTest.Server.Identity;

internal static class BankChecks
{
    public static void Verify(string root)
    {
        Checks.Run("bank scoped balances are atomic durable and replay-safe",
            () => ScopedLifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task ScopedLifecycle(string root)
    {
        string dir=Path.Combine(root,"bank_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        long charA1,charA2,charB1;

        using(var store=new SqliteIdentityStore(db))
        {
            await store.InitializeAsync();
            long accountA=(await store.CreateAccountAsync(
                "bank_user_a","Bank-Password-A-123!")).AccountId;
            long accountB=(await store.CreateAccountAsync(
                "bank_user_b","Bank-Password-B-123!")).AccountId;
            charA1=(await store.CreateCharacterAsync(
                accountA,0,"BankA1",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("bank A1 missing");
            charA2=(await store.CreateCharacterAsync(
                accountA,1,"BankA2",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("bank A2 missing");
            charB1=(await store.CreateCharacterAsync(
                accountB,0,"BankB1",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId??throw new Exception("bank B1 missing");
            SeedWallet(db,charA1,1000);
            SeedWallet(db,charA2,700);
            SeedWallet(db,charB1,300);

            var characterRule=new TrustedBankRule(
                88,BankBalanceScope.Character,"ci:bank-character");
            var accountRule=new TrustedBankRule(
                89,BankBalanceScope.Account,"ci:bank-account");

            var depositContext=new TrustedBankContext(charA1,301,1,1);
            var deposit=await store.DepositBankAsync(
                depositContext,BankAccessStatus.Allowed,characterRule,400);
            var replay=await store.DepositBankAsync(
                depositContext,BankAccessStatus.Allowed,characterRule,400);
            Checks.Require(
                deposit.Status==BankTransactionStatus.Applied&&
                replay.Status==BankTransactionStatus.AlreadyApplied&&
                deposit.WalletMoney==600&&deposit.BankMoney==400&&
                replay.WalletMoney==600&&replay.BankMoney==400,
                "character bank deposit/replay mismatch");

            var conflict=await store.WithdrawBankAsync(
                depositContext,BankAccessStatus.Allowed,characterRule,100);
            Checks.Require(
                conflict.Status==BankTransactionStatus.StaleSequence&&
                conflict.WalletMoney==600&&conflict.BankMoney==400,
                "bank conflicting replay mutated balance");

            var charA2Bank=await store.GetBankBalanceAsync(
                charA2,BankBalanceScope.Character);
            Checks.Require(charA2Bank.BankMoney==0&&charA2Bank.WalletMoney==700,
                "character-scoped bank leaked across sibling character");

            var withdrawn=await store.WithdrawBankAsync(
                new TrustedBankContext(charA1,301,1,2),
                BankAccessStatus.Allowed,characterRule,150);
            Checks.Require(
                withdrawn.Status==BankTransactionStatus.Applied&&
                withdrawn.WalletMoney==750&&withdrawn.BankMoney==250,
                "character bank withdraw mismatch");

            var accountDeposit=await store.DepositBankAsync(
                new TrustedBankContext(charA1,301,1,3),
                BankAccessStatus.Allowed,accountRule,200);
            Checks.Require(
                accountDeposit.Status==BankTransactionStatus.Applied&&
                accountDeposit.WalletMoney==550&&accountDeposit.BankMoney==200,
                "account bank deposit mismatch");

            var shared=await store.GetBankBalanceAsync(
                charA2,BankBalanceScope.Account);
            Checks.Require(shared.BankMoney==200&&shared.WalletMoney==700,
                "account-scoped bank not shared within account");

            var siblingWithdraw=await store.WithdrawBankAsync(
                new TrustedBankContext(charA2,401,1,1),
                BankAccessStatus.Allowed,accountRule,50);
            Checks.Require(
                siblingWithdraw.Status==BankTransactionStatus.Applied&&
                siblingWithdraw.WalletMoney==750&&siblingWithdraw.BankMoney==150,
                "sibling account bank withdraw mismatch");

            var foreign=await store.GetBankBalanceAsync(
                charB1,BankBalanceScope.Account);
            Checks.Require(foreign.BankMoney==0&&foreign.WalletMoney==300,
                "account bank leaked to another account");

            var tooMuchWallet=await store.DepositBankAsync(
                new TrustedBankContext(charB1,501,1,1),
                BankAccessStatus.Allowed,accountRule,301);
            var tooMuchBank=await store.WithdrawBankAsync(
                new TrustedBankContext(charB1,501,1,2),
                BankAccessStatus.Allowed,accountRule,1);
            Checks.Require(
                tooMuchWallet.Status==BankTransactionStatus.InsufficientWallet&&
                tooMuchBank.Status==BankTransactionStatus.InsufficientBank&&
                tooMuchBank.WalletMoney==300&&tooMuchBank.BankMoney==0,
                "failed bank transfer mutated money");

            var denied=await store.DepositBankAsync(
                new TrustedBankContext(charB1,501,1,3),
                BankAccessStatus.TooFar,accountRule,1);
            Checks.Require(
                denied.Status==BankTransactionStatus.TooFar&&
                denied.WalletMoney==300&&denied.BankMoney==0,
                "bank access rejection mutated money");
        }

        using(var reopened=new SqliteIdentityStore(db))
        {
            await reopened.InitializeAsync();
            var a1Character=await reopened.GetBankBalanceAsync(
                charA1,BankBalanceScope.Character);
            var a1Account=await reopened.GetBankBalanceAsync(
                charA1,BankBalanceScope.Account);
            var a2Account=await reopened.GetBankBalanceAsync(
                charA2,BankBalanceScope.Account);
            Checks.Require(
                a1Character.WalletMoney==550&&a1Character.BankMoney==250&&
                a1Account.BankMoney==150&&a2Account.BankMoney==150&&
                a2Account.WalletMoney==750,
                "bank balances did not survive reopen");
        }
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",characterId);
        Checks.Require(command.ExecuteNonQuery()==1,"bank wallet seed failed");
    }
}
