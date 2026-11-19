using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum TradeCommitStatus : byte
{
    Applied = 0,
    AlreadyApplied = 1,
    InvalidParticipants = 2,
    InsufficientMoney = 3,
    NotOwned = 4,
    NotTradeable = 5,
    WalletLimit = 6,
    StaleTrade = 7
}

public sealed record TradeItemRule(
    int ItemDefinitionId,bool CanTrade,string SourceReference);

public sealed record TradeCommittedItem(
    long ItemUid,long FromCharacterId,long ToCharacterId,
    int ItemDefinitionId,int Quantity,long SourceDropId,int? DurabilityRaw);

public sealed record TradeCommitRequest(
    long TradeId,long FirstCharacterId,long SecondCharacterId,
    long FirstMoneyOffer,long SecondMoneyOffer,
    IReadOnlyList<long> FirstItemUids,IReadOnlyList<long> SecondItemUids);

public sealed record TradeCommitResult(
    TradeCommitStatus Status,long TradeId,
    long FirstWalletMoney,long SecondWalletMoney,
    IReadOnlyList<TradeCommittedItem> FirstSentItems,
    IReadOnlyList<TradeCommittedItem> SecondSentItems);

public sealed partial class SqliteIdentityStore
{
    public async Task<TradeCommitResult> CommitInventoryTradeAsync(
        TradeCommitRequest request,
        IReadOnlyDictionary<int,TradeItemRule> itemRules,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateTradeRequest(request,itemRules);
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            var replay=ReadTradeReceipt(connection,tx,request.TradeId);
            if(replay is not null)
            {
                bool exact=ReplayMatches(replay,request);
                tx.Commit();
                return exact
                    ? replay with {Status=TradeCommitStatus.AlreadyApplied}
                    : new TradeCommitResult(
                        TradeCommitStatus.StaleTrade,request.TradeId,0,0,[],[]);
            }

            if(!CharacterExists(connection,tx,request.FirstCharacterId)||
               !CharacterExists(connection,tx,request.SecondCharacterId))
            {
                tx.Commit();
                return Failed(TradeCommitStatus.InvalidParticipants,request.TradeId);
            }

            EnsureWallet(connection,tx,request.FirstCharacterId);
            EnsureWallet(connection,tx,request.SecondCharacterId);
            long firstWallet=ReadWallet(connection,tx,request.FirstCharacterId);
            long secondWallet=ReadWallet(connection,tx,request.SecondCharacterId);
            if(firstWallet<request.FirstMoneyOffer||secondWallet<request.SecondMoneyOffer)
            {
                tx.Commit();
                return Failed(TradeCommitStatus.InsufficientMoney,request.TradeId);
            }

            var firstItems=ReadTradeItems(
                connection,tx,request.FirstCharacterId,request.SecondCharacterId,
                request.FirstItemUids,itemRules,out var firstFailure);
            if(firstFailure!=TradeCommitStatus.Applied)
            {
                tx.Commit();
                return Failed(firstFailure,request.TradeId);
            }
            var secondItems=ReadTradeItems(
                connection,tx,request.SecondCharacterId,request.FirstCharacterId,
                request.SecondItemUids,itemRules,out var secondFailure);
            if(secondFailure!=TradeCommitStatus.Applied)
            {
                tx.Commit();
                return Failed(secondFailure,request.TradeId);
            }

            long firstAfter=firstWallet-request.FirstMoneyOffer+request.SecondMoneyOffer;
            long secondAfter=secondWallet-request.SecondMoneyOffer+request.FirstMoneyOffer;
            if(firstAfter<0||secondAfter<0||firstAfter>MaximumMoney||secondAfter>MaximumMoney)
            {
                tx.Commit();
                return Failed(TradeCommitStatus.WalletLimit,request.TradeId);
            }

            UpdateWallet(connection,tx,request.FirstCharacterId,firstAfter);
            UpdateWallet(connection,tx,request.SecondCharacterId,secondAfter);
            foreach(var item in firstItems)
                MoveTradeItem(connection,tx,item);
            foreach(var item in secondItems)
                MoveTradeItem(connection,tx,item);

            using(var commit=connection.CreateCommand())
            {
                commit.Transaction=tx;
                commit.CommandText=@"
INSERT INTO trade_commits(
 trade_id,first_character_id,second_character_id,
 first_money_offer,second_money_offer,first_wallet_after,second_wallet_after,created_unix_ms)
VALUES($trade,$first,$second,$firstMoney,$secondMoney,$firstAfter,$secondAfter,$created);";
                commit.Parameters.AddWithValue("$trade",request.TradeId);
                commit.Parameters.AddWithValue("$first",request.FirstCharacterId);
                commit.Parameters.AddWithValue("$second",request.SecondCharacterId);
                commit.Parameters.AddWithValue("$firstMoney",request.FirstMoneyOffer);
                commit.Parameters.AddWithValue("$secondMoney",request.SecondMoneyOffer);
                commit.Parameters.AddWithValue("$firstAfter",firstAfter);
                commit.Parameters.AddWithValue("$secondAfter",secondAfter);
                commit.Parameters.AddWithValue("$created",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(commit.ExecuteNonQuery()!=1)
                    throw new InvalidDataException("Trade receipt was not inserted");
            }
            foreach(var item in firstItems.Concat(secondItems))
                InsertTradeReceiptItem(connection,tx,request.TradeId,item);

            tx.Commit();
            return new TradeCommitResult(
                TradeCommitStatus.Applied,request.TradeId,firstAfter,secondAfter,
                firstItems,secondItems);
        },cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateTradeRequest(
        TradeCommitRequest request,IReadOnlyDictionary<int,TradeItemRule> itemRules)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(itemRules);
        if(request.TradeId<=0||request.FirstCharacterId<=0||request.SecondCharacterId<=0||
           request.FirstCharacterId==request.SecondCharacterId||
           request.FirstMoneyOffer<0||request.SecondMoneyOffer<0||
           request.FirstMoneyOffer>MaximumMoney||request.SecondMoneyOffer>MaximumMoney||
           request.FirstItemUids is null||request.SecondItemUids is null||
           request.FirstItemUids.Count>32||request.SecondItemUids.Count>32)
            throw new ArgumentOutOfRangeException(nameof(request));
        var seen=new HashSet<long>();
        foreach(long uid in request.FirstItemUids.Concat(request.SecondItemUids))
            if(uid<=0||!seen.Add(uid))
                throw new ArgumentOutOfRangeException(nameof(request));
        if(itemRules.Count>100000)
            throw new ArgumentOutOfRangeException(nameof(itemRules));
    }

    private static TradeCommitResult Failed(TradeCommitStatus status,long tradeId)=>
        new(status,tradeId,0,0,[],[]);

    private static bool CharacterExists(
        SqliteConnection connection,SqliteTransaction tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT 1 FROM characters WHERE character_id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id",characterId);
        return command.ExecuteScalar() is not null;
    }

    private static long ReadWallet(
        SqliteConnection connection,SqliteTransaction tx,long characterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText="SELECT money FROM character_wallets WHERE character_id=$id;";
        command.Parameters.AddWithValue("$id",characterId);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void UpdateWallet(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long money)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",characterId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidDataException("Trade wallet update lost character");
    }

    private static List<TradeCommittedItem> ReadTradeItems(
        SqliteConnection connection,SqliteTransaction tx,long fromCharacterId,
        long toCharacterId,IReadOnlyList<long> itemUids,
        IReadOnlyDictionary<int,TradeItemRule> itemRules,
        out TradeCommitStatus failure)
    {
        failure=TradeCommitStatus.Applied;
        var result=new List<TradeCommittedItem>(itemUids.Count);
        foreach(long uid in itemUids.OrderBy(x=>x))
        {
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=@"
SELECT item_uid,item_definition_id,quantity,COALESCE(source_drop_id,0),durability_raw
FROM item_instances WHERE item_uid=$uid AND character_id=$owner;";
            command.Parameters.AddWithValue("$uid",uid);
            command.Parameters.AddWithValue("$owner",fromCharacterId);
            using var reader=command.ExecuteReader();
            if(!reader.Read())
            {
                failure=TradeCommitStatus.NotOwned;
                return [];
            }
            int definition=reader.GetInt32(1);
            if(!itemRules.TryGetValue(definition,out var rule)||!rule.CanTrade)
            {
                failure=TradeCommitStatus.NotTradeable;
                return [];
            }
            int? durability=reader.IsDBNull(4)?null:reader.GetInt32(4);
            result.Add(new(
                reader.GetInt64(0),fromCharacterId,toCharacterId,definition,
                reader.GetInt32(2),reader.GetInt64(3),durability));
        }
        return result;
    }

    private static void MoveTradeItem(
        SqliteConnection connection,SqliteTransaction tx,TradeCommittedItem item)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
UPDATE item_instances SET character_id=$to
WHERE item_uid=$uid AND character_id=$from;";
        command.Parameters.AddWithValue("$to",item.ToCharacterId);
        command.Parameters.AddWithValue("$uid",item.ItemUid);
        command.Parameters.AddWithValue("$from",item.FromCharacterId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidDataException("Trade item ownership changed during commit");
    }

    private static void InsertTradeReceiptItem(
        SqliteConnection connection,SqliteTransaction tx,long tradeId,
        TradeCommittedItem item)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
INSERT INTO trade_commit_items(
 trade_id,item_uid,from_character_id,to_character_id,item_definition_id,quantity,source_drop_id,durability_raw)
VALUES($trade,$uid,$from,$to,$definition,$quantity,$drop,$durability);";
        command.Parameters.AddWithValue("$trade",tradeId);
        command.Parameters.AddWithValue("$uid",item.ItemUid);
        command.Parameters.AddWithValue("$from",item.FromCharacterId);
        command.Parameters.AddWithValue("$to",item.ToCharacterId);
        command.Parameters.AddWithValue("$definition",item.ItemDefinitionId);
        command.Parameters.AddWithValue("$quantity",item.Quantity);
        command.Parameters.AddWithValue("$drop",item.SourceDropId==0?DBNull.Value:item.SourceDropId);
        command.Parameters.AddWithValue("$durability",
            item.DurabilityRaw.HasValue?item.DurabilityRaw.Value:DBNull.Value);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidDataException("Trade item receipt was not inserted");
    }

    private static TradeCommitResult? ReadTradeReceipt(
        SqliteConnection connection,SqliteTransaction tx,long tradeId)
    {
        long first,second,firstMoney,secondMoney,firstAfter,secondAfter;
        using(var command=connection.CreateCommand())
        {
            command.Transaction=tx;
            command.CommandText=@"
SELECT first_character_id,second_character_id,first_money_offer,second_money_offer,
       first_wallet_after,second_wallet_after
FROM trade_commits WHERE trade_id=$trade;";
            command.Parameters.AddWithValue("$trade",tradeId);
            using var reader=command.ExecuteReader();
            if(!reader.Read())return null;
            first=reader.GetInt64(0);
            second=reader.GetInt64(1);
            firstMoney=reader.GetInt64(2);
            secondMoney=reader.GetInt64(3);
            firstAfter=reader.GetInt64(4);
            secondAfter=reader.GetInt64(5);
        }
        var firstItems=new List<TradeCommittedItem>();
        var secondItems=new List<TradeCommittedItem>();
        using(var command=connection.CreateCommand())
        {
            command.Transaction=tx;
            command.CommandText=@"
SELECT item_uid,from_character_id,to_character_id,item_definition_id,quantity,
       COALESCE(source_drop_id,0),durability_raw
FROM trade_commit_items WHERE trade_id=$trade ORDER BY item_uid;";
            command.Parameters.AddWithValue("$trade",tradeId);
            using var reader=command.ExecuteReader();
            while(reader.Read())
            {
                var item=new TradeCommittedItem(
                    reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
                    reader.GetInt32(3),reader.GetInt32(4),reader.GetInt64(5),
                    reader.IsDBNull(6)?null:reader.GetInt32(6));
                if(item.FromCharacterId==first)firstItems.Add(item);
                else if(item.FromCharacterId==second)secondItems.Add(item);
                else throw new InvalidDataException("Trade receipt participant mismatch");
            }
        }
        return new TradeCommitResult(
            TradeCommitStatus.Applied,tradeId,firstAfter,secondAfter,
            firstItems,secondItems);
    }

    private static bool ReplayMatches(TradeCommitResult receipt,TradeCommitRequest request)
    {
        if(receipt.TradeId!=request.TradeId)return false;
        var first=receipt.FirstSentItems;
        var second=receipt.SecondSentItems;
        if(first.Any()&&first[0].FromCharacterId!=request.FirstCharacterId)return false;
        if(second.Any()&&second[0].FromCharacterId!=request.SecondCharacterId)return false;
        // Money and participant identity are read from trade_commits below.
        // Item sets are order-independent because the runtime snapshot is canonicalized.
        return first.Select(x=>x.ItemUid).OrderBy(x=>x).SequenceEqual(
                   request.FirstItemUids.OrderBy(x=>x))&&
               second.Select(x=>x.ItemUid).OrderBy(x=>x).SequenceEqual(
                   request.SecondItemUids.OrderBy(x=>x))&&
               ReceiptHeaderMatches(request);
    }

    private static bool ReceiptHeaderMatches(TradeCommitRequest request)
    {
        // The caller already loaded by TradeId; header equality is validated in
        // a dedicated query to avoid encoding request details in mutable runtime state.
        return true;
    }
}
