using SGTest.Server.Npc;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum RepairAccessStatus : byte
{
    Allowed=0, StaleGeneration=1, UnknownRepair=2, TooFar=3, PlayerDead=4
}

public enum RepairItemLocation : byte { None=0, Inventory=1, Equipped=2 }

public enum RepairStatus : byte
{
    Applied=0,
    AlreadyApplied=1,
    UnknownRepair=2,
    TooFar=3,
    NotOwned=4,
    UnknownItem=5,
    AlreadyFull=6,
    InsufficientMoney=7,
    StaleSequence=8,
    StaleGeneration=9,
    PlayerDead=10,
    InvalidDurability=11
}

public readonly record struct TrustedRepairContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,uint ActionSequence);

public readonly record struct TrustedRepairNpcRule(
    int NpcSpawnId,int RepairRate,string SourceReference);

public sealed record RepairPreviewItem(
    long ItemUid,int ItemDefinitionId,RepairItemLocation Location,
    int CurrentDurability,int MaxDurability,long Cost);

public sealed record RepairPreview(
    long WalletMoney,IReadOnlyList<RepairPreviewItem> Items);

public sealed record RepairResult(
    RepairStatus Status,int NpcSpawnId,long ItemUid,int ItemDefinitionId,
    RepairItemLocation Location,int CurrentDurability,int MaxDurability,
    long Cost,long WalletMoney);

public sealed record DurabilityWearResult(
    bool Applied,long ItemUid,int ItemDefinitionId,int SlotId,
    int PreviousRaw,int CurrentRaw,bool Broken);

public sealed partial class SqliteIdentityStore
{
    public async Task<RepairPreview> ListRepairPreviewAsync(
        long characterId,TrustedRepairNpcRule npcRule,
        IReadOnlyDictionary<int,RepairItemRule> itemRules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateRepairRule(npcRule);
        ArgumentNullException.ThrowIfNull(itemRules);
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            EnsureWallet(connection,tx,characterId);
            long wallet=ReadMoney(connection,tx,characterId);
            var result=new List<RepairPreviewItem>();
            ReadPreviewTable(connection,tx,"item_instances",
                RepairItemLocation.Inventory,characterId,npcRule,itemRules,result);
            ReadPreviewTable(connection,tx,"equipped_items",
                RepairItemLocation.Equipped,characterId,npcRule,itemRules,result);
            result.Sort((a,b)=>a.ItemUid.CompareTo(b.ItemUid));
            tx.Commit();
            return new RepairPreview(wallet,result);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<RepairResult> RepairItemAsync(
        TrustedRepairContext context,RepairAccessStatus access,
        TrustedRepairNpcRule npcRule,
        IReadOnlyDictionary<int,RepairItemRule> itemRules,long itemUid,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ValidateRepairRequest(context,npcRule,itemRules,itemUid);
        return await Task.Run<RepairResult>(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            EnsureWallet(connection,tx,context.CharacterId);

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT npc_spawn_id,item_uid,item_definition_id,repair_rate,
       max_durability,raw_before,raw_after,cost,money_after,source_reference
FROM repair_actions
WHERE character_id=$actor AND connection_generation=$conn
  AND map_epoch=$epoch AND action_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor",context.CharacterId);
                replay.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch",context.MapEpoch);
                replay.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    int originalNpc=reader.GetInt32(0);
                    long originalUid=reader.GetInt64(1);
                    int originalItem=reader.GetInt32(2);
                    int originalRate=reader.GetInt32(3);
                    int max=reader.GetInt32(4);
                    int rawBefore=reader.GetInt32(5);
                    int rawAfter=reader.GetInt32(6);
                    long replayCost=reader.GetInt64(7);
                    long replayMoney=reader.GetInt64(8);
                    string source=reader.GetString(9);
                    bool exact=originalNpc==npcRule.NpcSpawnId&&
                        originalUid==itemUid&&originalRate==npcRule.RepairRate&&
                        source==npcRule.SourceReference;
                    if(!exact)
                        return Reject(RepairStatus.StaleSequence,npcRule.NpcSpawnId,
                            itemUid,0,0,0,0,ReadMoney(connection,tx,context.CharacterId));
                    var location=FindLocation(connection,tx,context.CharacterId,itemUid);
                    return new(RepairStatus.AlreadyApplied,originalNpc,originalUid,
                        originalItem,location,DisplayDurability(rawAfter),max,
                        replayCost,replayMoney);
                }
            }

            long last=ScalarLong(connection,@"
SELECT COALESCE(MAX(action_sequence),0) FROM repair_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",tx,
                ("$actor",context.CharacterId),
                ("$conn",context.ConnectionGeneration),
                ("$epoch",context.MapEpoch));
            if(last>=context.ActionSequence)
                return Reject(RepairStatus.StaleSequence,npcRule.NpcSpawnId,
                    itemUid,0,0,0,0,ReadMoney(connection,tx,context.CharacterId));

            var accessFailure=RepairAccessFailure(access);
            if(accessFailure is not null)
                return Reject(accessFailure.Value,npcRule.NpcSpawnId,
                    itemUid,0,0,0,0,ReadMoney(connection,tx,context.CharacterId));

            var row=ReadRepairItem(connection,tx,context.CharacterId,itemUid);
            if(row is null)
                return Reject(RepairStatus.NotOwned,npcRule.NpcSpawnId,
                    itemUid,0,0,0,0,ReadMoney(connection,tx,context.CharacterId));
            if(!itemRules.TryGetValue(row.ItemDefinitionId,out var itemRule))
                return Reject(RepairStatus.UnknownItem,npcRule.NpcSpawnId,
                    itemUid,row.ItemDefinitionId,row.Location,0,0,
                    ReadMoney(connection,tx,context.CharacterId));

            int fullRaw=itemRule.FullRawDurability;
            int raw=row.DurabilityRaw??fullRaw;
            if(raw<0||raw>fullRaw)
                return Reject(RepairStatus.InvalidDurability,npcRule.NpcSpawnId,
                    itemUid,row.ItemDefinitionId,row.Location,0,itemRule.MaxDurability,
                    ReadMoney(connection,tx,context.CharacterId));
            int current=DisplayDurability(raw);
            long walletBefore=ReadMoney(connection,tx,context.CharacterId);
            if(raw==fullRaw)
                return new(RepairStatus.AlreadyFull,npcRule.NpcSpawnId,itemUid,
                    row.ItemDefinitionId,row.Location,itemRule.MaxDurability,
                    itemRule.MaxDurability,0,walletBefore);

            long cost=RepairCost(itemRule.BasePrice,itemRule.MaxDurability,
                current,npcRule.RepairRate);
            if(cost>walletBefore)
                return new(RepairStatus.InsufficientMoney,npcRule.NpcSpawnId,itemUid,
                    row.ItemDefinitionId,row.Location,current,itemRule.MaxDurability,
                    cost,walletBefore);

            string table=row.Location==RepairItemLocation.Equipped
                ?"equipped_items":"item_instances";
            using(var update=connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=$@"
UPDATE {table} SET durability_raw=$raw
WHERE item_uid=$uid AND character_id=$actor;";
                update.Parameters.AddWithValue("$raw",fullRaw);
                update.Parameters.AddWithValue("$uid",itemUid);
                update.Parameters.AddWithValue("$actor",context.CharacterId);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Repair item ownership changed during transaction");
            }
            long walletAfter=walletBefore-cost;
            UpdateMoney(connection,tx,context.CharacterId,walletAfter);

            using(var receipt=connection.CreateCommand())
            {
                receipt.Transaction=tx;
                receipt.CommandText=@"
INSERT INTO repair_actions(
    character_id,connection_generation,map_epoch,action_sequence,npc_spawn_id,
    item_uid,item_definition_id,repair_rate,base_price,max_durability,
    raw_before,raw_after,cost,money_after,source_reference,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$npc,$uid,$item,$rate,$price,$max,
       $before,$after,$cost,$money,$source,$now);";
                receipt.Parameters.AddWithValue("$actor",context.CharacterId);
                receipt.Parameters.AddWithValue("$conn",context.ConnectionGeneration);
                receipt.Parameters.AddWithValue("$epoch",context.MapEpoch);
                receipt.Parameters.AddWithValue("$seq",(long)context.ActionSequence);
                receipt.Parameters.AddWithValue("$npc",npcRule.NpcSpawnId);
                receipt.Parameters.AddWithValue("$uid",itemUid);
                receipt.Parameters.AddWithValue("$item",row.ItemDefinitionId);
                receipt.Parameters.AddWithValue("$rate",npcRule.RepairRate);
                receipt.Parameters.AddWithValue("$price",itemRule.BasePrice);
                receipt.Parameters.AddWithValue("$max",itemRule.MaxDurability);
                receipt.Parameters.AddWithValue("$before",raw);
                receipt.Parameters.AddWithValue("$after",fullRaw);
                receipt.Parameters.AddWithValue("$cost",cost);
                receipt.Parameters.AddWithValue("$money",walletAfter);
                receipt.Parameters.AddWithValue("$source",npcRule.SourceReference);
                receipt.Parameters.AddWithValue(
                    "$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(receipt.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Repair durable receipt failed");
            }

            tx.Commit();
            return new(RepairStatus.Applied,npcRule.NpcSpawnId,itemUid,
                row.ItemDefinitionId,row.Location,itemRule.MaxDurability,
                itemRule.MaxDurability,cost,walletAfter);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<DurabilityWearResult> ApplyTrustedEquippedWearAsync(
        long characterId,long itemUid,
        IReadOnlyDictionary<int,RepairItemRule> itemRules,int rawTicks=1,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(itemRules);
        if(characterId<=0||itemUid<=0||rawTicks is <1 or >100000)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using var read=connection.CreateCommand();
            read.Transaction=tx;
            read.CommandText=@"
SELECT item_definition_id,slot_id,durability_raw
FROM equipped_items WHERE item_uid=$uid AND character_id=$actor;";
            read.Parameters.AddWithValue("$uid",itemUid);
            read.Parameters.AddWithValue("$actor",characterId);
            using var reader=read.ExecuteReader();
            if(!reader.Read())return new DurabilityWearResult(
                false,itemUid,0,0,0,0,false);
            int definition=reader.GetInt32(0);
            int slot=reader.GetInt32(1);
            int? stored=reader.IsDBNull(2)?null:reader.GetInt32(2);
            reader.Close();
            if(!itemRules.TryGetValue(definition,out var rule))
                return new DurabilityWearResult(false,itemUid,definition,slot,0,0,false);
            int before=stored??rule.FullRawDurability;
            if(before<0||before>rule.FullRawDurability)
                throw new InvalidDataException("Equipped durability outside verified rule");
            int after=Math.Max(0,before-rawTicks);
            using var update=connection.CreateCommand();
            update.Transaction=tx;
            update.CommandText=@"
UPDATE equipped_items SET durability_raw=$raw
WHERE item_uid=$uid AND character_id=$actor;";
            update.Parameters.AddWithValue("$raw",after);
            update.Parameters.AddWithValue("$uid",itemUid);
            update.Parameters.AddWithValue("$actor",characterId);
            if(update.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Durability wear lost equipped item");
            tx.Commit();
            return new DurabilityWearResult(true,itemUid,definition,slot,
                before,after,after==0);
        },cancellationToken).ConfigureAwait(false);
    }

    public static int DisplayDurability(int raw)=>
        raw<=0?0:checked((raw+49)/50);

    public static long RepairCost(
        long basePrice,int maxDurability,int currentDurability,int repairRate)
    {
        if(basePrice<=0||basePrice>1_000_000_000_000L||
           maxDurability<=0||currentDurability<0||
           currentDurability>maxDurability||repairRate is <1 or >255)
            throw new ArgumentOutOfRangeException(nameof(basePrice));
        decimal numerator=(decimal)basePrice*
            (maxDurability-currentDurability)*repairRate;
        decimal denominator=(decimal)maxDurability*100m;
        long cost=checked((long)Math.Ceiling(numerator/denominator));
        return Math.Max(1,cost);
    }

    private static void ReadPreviewTable(
        SqliteConnection connection,SqliteTransaction tx,string table,
        RepairItemLocation location,long characterId,TrustedRepairNpcRule npcRule,
        IReadOnlyDictionary<int,RepairItemRule> itemRules,
        List<RepairPreviewItem> output)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=$@"
SELECT item_uid,item_definition_id,quantity,durability_raw
FROM {table} WHERE character_id=$actor ORDER BY item_uid;";
        command.Parameters.AddWithValue("$actor",characterId);
        using var reader=command.ExecuteReader();
        while(reader.Read())
        {
            long uid=reader.GetInt64(0);
            int definition=reader.GetInt32(1);
            int quantity=reader.GetInt32(2);
            int? stored=reader.IsDBNull(3)?null:reader.GetInt32(3);
            if(quantity!=1||!itemRules.TryGetValue(definition,out var rule))
                continue;
            int raw=stored??rule.FullRawDurability;
            if(raw<0||raw>rule.FullRawDurability)
                throw new InvalidDataException("Item durability outside verified rule");
            int current=DisplayDurability(raw);
            long cost=raw==rule.FullRawDurability
                ?0:RepairCost(rule.BasePrice,rule.MaxDurability,current,npcRule.RepairRate);
            output.Add(new(uid,definition,location,current,rule.MaxDurability,cost));
        }
    }

    private static RepairOwnedItem? ReadRepairItem(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long itemUid)
    {
        foreach(var (table,location) in new[]
        {
            ("item_instances",RepairItemLocation.Inventory),
            ("equipped_items",RepairItemLocation.Equipped)
        })
        {
            using var command=connection.CreateCommand();
            command.Transaction=tx;
            command.CommandText=$@"
SELECT item_definition_id,quantity,durability_raw
FROM {table} WHERE item_uid=$uid AND character_id=$actor;";
            command.Parameters.AddWithValue("$uid",itemUid);
            command.Parameters.AddWithValue("$actor",characterId);
            using var reader=command.ExecuteReader();
            if(reader.Read())
                return new(itemUid,reader.GetInt32(0),reader.GetInt32(1),
                    reader.IsDBNull(2)?null:reader.GetInt32(2),location);
        }
        return null;
    }

    private static RepairItemLocation FindLocation(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long itemUid)
    {
        foreach(var (table,location) in new[]
        {
            ("item_instances",RepairItemLocation.Inventory),
            ("equipped_items",RepairItemLocation.Equipped)
        })
            if(ScalarLong(connection,
                $"SELECT COUNT(*) FROM {table} WHERE item_uid=$uid AND character_id=$actor;",
                tx,("$uid",itemUid),("$actor",characterId))==1)
                return location;
        return RepairItemLocation.Inventory;
    }

    private static RepairResult Reject(
        RepairStatus status,int npcSpawnId,long itemUid,int itemDefinitionId,
        RepairItemLocation location,int current,int max,long money)=>
        new(status,npcSpawnId,itemUid,itemDefinitionId,location,current,max,0,money);

    private static RepairStatus? RepairAccessFailure(RepairAccessStatus access)=>
        access switch
        {
            RepairAccessStatus.Allowed=>null,
            RepairAccessStatus.StaleGeneration=>RepairStatus.StaleGeneration,
            RepairAccessStatus.UnknownRepair=>RepairStatus.UnknownRepair,
            RepairAccessStatus.TooFar=>RepairStatus.TooFar,
            RepairAccessStatus.PlayerDead=>RepairStatus.PlayerDead,
            _=>RepairStatus.StaleGeneration
        };

    private static void ValidateRepairRequest(
        TrustedRepairContext context,TrustedRepairNpcRule npcRule,
        IReadOnlyDictionary<int,RepairItemRule> itemRules,long itemUid)
    {
        ValidateRepairRule(npcRule);
        ArgumentNullException.ThrowIfNull(itemRules);
        if(context.CharacterId<=0||context.ConnectionGeneration<=0||
           context.MapEpoch<=0||context.ActionSequence==0||itemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(context));
    }

    private static void ValidateRepairRule(TrustedRepairNpcRule rule)
    {
        if(rule.NpcSpawnId<=0||rule.RepairRate is <1 or >255||
           string.IsNullOrWhiteSpace(rule.SourceReference)||
           rule.SourceReference.Length>512)
            throw new ArgumentOutOfRangeException(nameof(rule));
    }

    private sealed record RepairOwnedItem(
        long ItemUid,int ItemDefinitionId,int Quantity,int? DurabilityRaw,
        RepairItemLocation Location);
}
