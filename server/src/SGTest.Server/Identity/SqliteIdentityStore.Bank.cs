using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum BankBalanceScope : byte { Character = 1, Account = 2 }
public enum BankAccessStatus : byte
{
    Allowed = 0, StaleGeneration = 1, UnknownBank = 2, TooFar = 3, PlayerDead = 4
}
public enum BankTransactionKind : byte { Deposit = 1, Withdraw = 2 }
public enum BankTransactionStatus : byte
{
    Applied = 0,
    AlreadyApplied = 1,
    UnknownBank = 2,
    TooFar = 3,
    InsufficientWallet = 4,
    InsufficientBank = 5,
    WalletLimit = 6,
    BankLimit = 7,
    StaleSequence = 8,
    StaleGeneration = 9,
    PlayerDead = 10
}

public readonly record struct TrustedBankContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,uint ActionSequence);
public readonly record struct TrustedBankRule(
    int NpcSpawnId,BankBalanceScope Scope,string SourceReference);
public sealed record BankBalanceSnapshot(
    BankBalanceScope Scope,long WalletMoney,long BankMoney);
public sealed record BankTransactionResult(
    BankTransactionStatus Status,BankTransactionKind Kind,int NpcSpawnId,
    BankBalanceScope Scope,long Amount,long WalletMoney,long BankMoney);

public sealed partial class SqliteIdentityStore
{
    public async Task<BankBalanceSnapshot> GetBankBalanceAsync(
        long characterId,BankBalanceScope scope,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateBankIdentity(characterId,scope);
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            EnsureWallet(connection,tx,characterId);
            long ownerId=ResolveBankOwnerId(connection,tx,characterId,scope);
            EnsureBankBalance(connection,tx,scope,ownerId);
            var result=new BankBalanceSnapshot(scope,
                ReadMoney(connection,tx,characterId),
                ReadBankMoney(connection,tx,scope,ownerId));
            tx.Commit();
            return result;
        },cancellationToken).ConfigureAwait(false);
    }

    public Task<BankTransactionResult> DepositBankAsync(
        TrustedBankContext context,BankAccessStatus access,TrustedBankRule rule,long amount,
        CancellationToken cancellationToken=default) =>
        ExecuteBankAsync(context,access,BankTransactionKind.Deposit,rule,amount,cancellationToken);

    public Task<BankTransactionResult> WithdrawBankAsync(
        TrustedBankContext context,BankAccessStatus access,TrustedBankRule rule,long amount,
        CancellationToken cancellationToken=default) =>
        ExecuteBankAsync(context,access,BankTransactionKind.Withdraw,rule,amount,cancellationToken);

    private async Task<BankTransactionResult> ExecuteBankAsync(
        TrustedBankContext context,BankAccessStatus access,BankTransactionKind kind,
        TrustedBankRule rule,long amount,CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateBankRequest(context,kind,rule,amount);
        return await Task.Run<BankTransactionResult>(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            EnsureWallet(connection,tx,context.CharacterId);
            long ownerId=ResolveBankOwnerId(connection,tx,context.CharacterId,rule.Scope);
            EnsureBankBalance(connection,tx,rule.Scope,ownerId);

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT action_kind,npc_spawn_id,balance_scope,amount,wallet_after,bank_after,source_reference
FROM bank_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor",context.CharacterId);
                replay.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch",context.MapEpoch);
                replay.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    var originalKind=(BankTransactionKind)reader.GetInt32(0);
                    int originalNpc=reader.GetInt32(1);
                    var originalScope=(BankBalanceScope)reader.GetInt32(2);
                    long originalAmount=reader.GetInt64(3);
                    long walletAfter=reader.GetInt64(4);
                    long bankAfter=reader.GetInt64(5);
                    string source=reader.GetString(6);
                    bool exact=originalKind==kind&&originalNpc==rule.NpcSpawnId&&
                        originalScope==rule.Scope&&originalAmount==amount&&
                        source==rule.SourceReference;
                    return exact
                        ? new(BankTransactionStatus.AlreadyApplied,kind,rule.NpcSpawnId,
                            rule.Scope,amount,walletAfter,bankAfter)
                        : RejectBank(BankTransactionStatus.StaleSequence,kind,rule,amount,
                            connection,tx,context.CharacterId,ownerId);
                }
            }

            long last=ScalarLong(connection,@"
SELECT COALESCE(MAX(action_sequence),0) FROM bank_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",tx,
                ("$actor",context.CharacterId),("$conn",context.ConnectionGeneration),
                ("$epoch",context.MapEpoch));
            if(last>=context.ActionSequence)
                return RejectBank(BankTransactionStatus.StaleSequence,kind,rule,amount,
                    connection,tx,context.CharacterId,ownerId);

            var accessFailure=BankAccessFailure(access);
            if(accessFailure is not null)
                return RejectBank(accessFailure.Value,kind,rule,amount,
                    connection,tx,context.CharacterId,ownerId);

            long wallet=ReadMoney(connection,tx,context.CharacterId);
            long bank=ReadBankMoney(connection,tx,rule.Scope,ownerId);
            long walletAfter;
            long bankAfter;
            if(kind==BankTransactionKind.Deposit)
            {
                if(amount>wallet)
                    return RejectBank(BankTransactionStatus.InsufficientWallet,kind,rule,amount,
                        connection,tx,context.CharacterId,ownerId);
                if(bank>MaximumMoney-amount)
                    return RejectBank(BankTransactionStatus.BankLimit,kind,rule,amount,
                        connection,tx,context.CharacterId,ownerId);
                walletAfter=wallet-amount;
                bankAfter=bank+amount;
            }
            else
            {
                if(amount>bank)
                    return RejectBank(BankTransactionStatus.InsufficientBank,kind,rule,amount,
                        connection,tx,context.CharacterId,ownerId);
                if(wallet>MaximumMoney-amount)
                    return RejectBank(BankTransactionStatus.WalletLimit,kind,rule,amount,
                        connection,tx,context.CharacterId,ownerId);
                walletAfter=wallet+amount;
                bankAfter=bank-amount;
            }

            UpdateMoney(connection,tx,context.CharacterId,walletAfter);
            UpdateBankMoney(connection,tx,rule.Scope,ownerId,bankAfter);
            using(var action=connection.CreateCommand())
            {
                action.Transaction=tx;
                action.CommandText=@"
INSERT INTO bank_actions(
    character_id,connection_generation,map_epoch,action_sequence,action_kind,
    npc_spawn_id,balance_scope,amount,wallet_after,bank_after,source_reference,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$kind,$npc,$scope,$amount,$wallet,$bank,$source,$now);";
                action.Parameters.AddWithValue("$actor",context.CharacterId);
                action.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                action.Parameters.AddWithValue("$epoch",context.MapEpoch);
                action.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                action.Parameters.AddWithValue("$kind",(int)kind);
                action.Parameters.AddWithValue("$npc",rule.NpcSpawnId);
                action.Parameters.AddWithValue("$scope",(int)rule.Scope);
                action.Parameters.AddWithValue("$amount",amount);
                action.Parameters.AddWithValue("$wallet",walletAfter);
                action.Parameters.AddWithValue("$bank",bankAfter);
                action.Parameters.AddWithValue("$source",rule.SourceReference);
                action.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(action.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Bank receipt insert failed");
            }
            tx.Commit();
            return new(BankTransactionStatus.Applied,kind,rule.NpcSpawnId,rule.Scope,
                amount,walletAfter,bankAfter);
        },cancellationToken).ConfigureAwait(false);
    }

    private static BankTransactionResult RejectBank(
        BankTransactionStatus status,BankTransactionKind kind,TrustedBankRule rule,long amount,
        SqliteConnection connection,SqliteTransaction tx,long characterId,long ownerId) =>
        new(status,kind,rule.NpcSpawnId,rule.Scope,amount,
            ReadMoney(connection,tx,characterId),ReadBankMoney(connection,tx,rule.Scope,ownerId));

    private static BankTransactionStatus? BankAccessFailure(BankAccessStatus access) => access switch
    {
        BankAccessStatus.Allowed=>null,
        BankAccessStatus.UnknownBank=>BankTransactionStatus.UnknownBank,
        BankAccessStatus.TooFar=>BankTransactionStatus.TooFar,
        BankAccessStatus.PlayerDead=>BankTransactionStatus.PlayerDead,
        _=>BankTransactionStatus.StaleGeneration
    };

    private static long ResolveBankOwnerId(
        SqliteConnection connection,SqliteTransaction tx,long characterId,BankBalanceScope scope)
    {
        if(scope==BankBalanceScope.Character)return characterId;
        long accountId=ScalarLong(connection,
            "SELECT COALESCE(MAX(account_id),0) FROM characters WHERE character_id=$id;",tx,
            ("$id",characterId));
        if(accountId<=0)throw new KeyNotFoundException("Unknown bank character");
        return accountId;
    }

    private static void EnsureBankBalance(
        SqliteConnection connection,SqliteTransaction tx,BankBalanceScope scope,long ownerId)
    {
        string table=scope==BankBalanceScope.Character
            ? "character_bank_balances":"account_bank_balances";
        string column=scope==BankBalanceScope.Character?"character_id":"account_id";
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=$"INSERT OR IGNORE INTO {table}({column},money) VALUES($id,0);";
        command.Parameters.AddWithValue("$id",ownerId);
        if(command.ExecuteNonQuery()>1)
            throw new InvalidOperationException("Bank balance insert count invalid");
    }

    private static long ReadBankMoney(
        SqliteConnection connection,SqliteTransaction tx,BankBalanceScope scope,long ownerId)
    {
        string table=scope==BankBalanceScope.Character
            ? "character_bank_balances":"account_bank_balances";
        string column=scope==BankBalanceScope.Character?"character_id":"account_id";
        return ScalarLong(connection,$"SELECT money FROM {table} WHERE {column}=$id;",tx,
            ("$id",ownerId));
    }

    private static void UpdateBankMoney(
        SqliteConnection connection,SqliteTransaction tx,BankBalanceScope scope,long ownerId,long money)
    {
        if(money<0||money>MaximumMoney)throw new ArgumentOutOfRangeException(nameof(money));
        string table=scope==BankBalanceScope.Character
            ? "character_bank_balances":"account_bank_balances";
        string column=scope==BankBalanceScope.Character?"character_id":"account_id";
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=$"UPDATE {table} SET money=$money WHERE {column}=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",ownerId);
        if(command.ExecuteNonQuery()!=1)throw new InvalidOperationException("Bank balance update failed");
    }

    private static void ValidateBankIdentity(long characterId,BankBalanceScope scope)
    {
        if(characterId<=0||scope is not (BankBalanceScope.Character or BankBalanceScope.Account))
            throw new ArgumentOutOfRangeException(nameof(characterId));
    }

    private static void ValidateBankRequest(
        TrustedBankContext context,BankTransactionKind kind,TrustedBankRule rule,long amount)
    {
        if(context.CharacterId<=0||context.ConnectionGeneration<=0||context.MapEpoch<=0||
           context.ActionSequence==0||kind is not (BankTransactionKind.Deposit or BankTransactionKind.Withdraw)||
           rule.NpcSpawnId<=0||rule.Scope is not (BankBalanceScope.Character or BankBalanceScope.Account)||
           string.IsNullOrWhiteSpace(rule.SourceReference)||rule.SourceReference.Length>512||
           amount<=0||amount>MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(context));
    }
}
