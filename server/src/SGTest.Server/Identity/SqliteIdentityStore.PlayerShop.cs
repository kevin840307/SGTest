using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum PlayerShopListingState : byte
{
    Active=0,
    Sold=1,
    Closed=2,
    Stale=3
}

public enum PlayerShopCreateStatus : byte
{
    Created=0,
    AlreadyOpen=1,
    NotOwned=2,
    NotTradeable=3,
    CapacityReached=4,
    InvalidQuantity=5
}

public enum PlayerShopCloseStatus : byte
{
    Closed=0,
    AlreadyClosed=1,
    NotFound=2
}

public enum PlayerShopBuyStatus : byte
{
    Applied=0,
    AlreadyApplied=1,
    Sold=2,
    Closed=3,
    StaleListing=4,
    SelfPurchase=5,
    InsufficientMoney=6,
    WalletLimit=7,
    NotTradeable=8,
    NotFound=9
}

public sealed record PlayerShopListing(
    long ListingId,long SellerCharacterId,long ItemUid,int ItemDefinitionId,
    int Quantity,long SourceDropId,int? DurabilityRaw,long Price,
    PlayerShopListingState State,long BuyerCharacterId=0,
    long SellerWalletAfter=0,long BuyerWalletAfter=0,
    int EnhancementLevel=0,int SourceKind=2,string SourceReference="legacy-player-shop");

public sealed record PlayerShopCreateResult(
    PlayerShopCreateStatus Status,PlayerShopListing? Listing=null);

public sealed record PlayerShopCloseResult(
    PlayerShopCloseStatus Status,long ListingId);

public sealed record PlayerShopBuyResult(
    PlayerShopBuyStatus Status,PlayerShopListing? Listing);

/// <summary>
/// Durable item/wallet settlement for the player-shop slice.
/// Active listings escrow the listed UID outside item_instances, so the seller
/// cannot equip/discard/trade the same item while it is for sale. Partial-stack
/// listings retain the original UID/provenance in escrow and create a new
/// seller-owned remainder UID inside the same SQLite transaction.
/// </summary>
public sealed partial class SqliteIdentityStore
{
    // SGTest safety bound only. HK's exact stall slot count remains NOT VERIFIED.
    public const int PlayerShopMaximumActiveListings=16;

    public async Task<PlayerShopCreateResult> CreatePlayerShopListingAsync(
        long sellerCharacterId,long itemUid,int requestedQuantity,long price,
        IReadOnlyDictionary<int,TradeItemRule> itemRules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(itemRules);
        if(sellerCharacterId<=0||itemUid<=0||requestedQuantity<0||
           price<=0||price>MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(sellerCharacterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using(var lockSeller=connection.CreateCommand())
            {
                lockSeller.Transaction=tx;
                lockSeller.CommandText=
                    "UPDATE characters SET updated_unix_ms=updated_unix_ms WHERE character_id=$seller;";
                lockSeller.Parameters.AddWithValue("$seller",sellerCharacterId);
                if(lockSeller.ExecuteNonQuery()!=1)
                    throw new KeyNotFoundException("Unknown player-shop seller");
            }
            var existing=ReadActivePlayerShopListings(connection,tx,sellerCharacterId);
            if(existing.Any(x=>x.ItemUid==itemUid))
            {
                tx.Commit();
                return new PlayerShopCreateResult(PlayerShopCreateStatus.AlreadyOpen);
            }
            if(existing.Count>=PlayerShopMaximumActiveListings)
            {
                tx.Commit();
                return new PlayerShopCreateResult(PlayerShopCreateStatus.CapacityReached);
            }

            var item=ReadPlayerShopOwnedItem(connection,tx,sellerCharacterId,itemUid);
            if(item is null)
            {
                tx.Commit();
                return new PlayerShopCreateResult(PlayerShopCreateStatus.NotOwned);
            }
            int quantity=requestedQuantity==0?item.Value.Quantity:requestedQuantity;
            if(quantity<1||quantity>item.Value.Quantity)
            {
                tx.Commit();
                return new PlayerShopCreateResult(PlayerShopCreateStatus.InvalidQuantity);
            }
            if(!itemRules.TryGetValue(item.Value.ItemDefinitionId,out var rule)||
               !rule.CanTrade)
            {
                tx.Commit();
                return new PlayerShopCreateResult(PlayerShopCreateStatus.NotTradeable);
            }

            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using(var remove=connection.CreateCommand())
            {
                remove.Transaction=tx;
                remove.CommandText=@"
DELETE FROM item_instances
WHERE item_uid=$uid AND character_id=$seller AND quantity=$owned;";
                remove.Parameters.AddWithValue("$uid",item.Value.ItemUid);
                remove.Parameters.AddWithValue("$seller",sellerCharacterId);
                remove.Parameters.AddWithValue("$owned",item.Value.Quantity);
                if(remove.ExecuteNonQuery()!=1)
                    throw new InvalidDataException(
                        "Player shop escrow lost seller inventory UID");
            }

            int remainder=item.Value.Quantity-quantity;
            if(remainder>0)
            {
                using var split=connection.CreateCommand();
                split.Transaction=tx;
                split.CommandText=@"
INSERT INTO item_instances(
 character_id,item_definition_id,quantity,source_drop_id,durability_raw,
 enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($seller,$definition,$quantity,NULL,$durability,$enhancement,2,$reference,$now);";
                split.Parameters.AddWithValue("$seller",sellerCharacterId);
                split.Parameters.AddWithValue("$definition",item.Value.ItemDefinitionId);
                split.Parameters.AddWithValue("$quantity",remainder);
                split.Parameters.AddWithValue("$durability",
                    item.Value.DurabilityRaw.HasValue
                        ?item.Value.DurabilityRaw.Value:DBNull.Value);
                split.Parameters.AddWithValue("$enhancement",item.Value.EnhancementLevel);
                split.Parameters.AddWithValue("$reference",$"player-shop-split:{item.Value.ItemUid}");
                split.Parameters.AddWithValue("$now",now);
                if(split.ExecuteNonQuery()!=1)
                    throw new InvalidDataException(
                        "Player shop escrow could not create remainder stack");
            }

            using var insert=connection.CreateCommand();
            insert.Transaction=tx;
            insert.CommandText=@"
INSERT INTO player_shop_listings(
 seller_character_id,item_uid,item_definition_id,quantity,source_drop_id,
 durability_raw,enhancement_level,source_kind,source_reference,
 price,state,created_unix_ms,updated_unix_ms)
VALUES($seller,$uid,$definition,$quantity,$drop,$durability,$enhancement,
       $sourceKind,$sourceReference,$price,0,$now,$now);
SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$seller",sellerCharacterId);
            insert.Parameters.AddWithValue("$uid",item.Value.ItemUid);
            insert.Parameters.AddWithValue("$definition",item.Value.ItemDefinitionId);
            insert.Parameters.AddWithValue("$quantity",quantity);
            insert.Parameters.AddWithValue("$drop",
                item.Value.SourceDropId==0?DBNull.Value:item.Value.SourceDropId);
            insert.Parameters.AddWithValue("$durability",
                item.Value.DurabilityRaw.HasValue
                    ?item.Value.DurabilityRaw.Value:DBNull.Value);
            insert.Parameters.AddWithValue("$enhancement",item.Value.EnhancementLevel);
            insert.Parameters.AddWithValue("$sourceKind",item.Value.SourceKind);
            insert.Parameters.AddWithValue("$sourceReference",item.Value.SourceReference);
            insert.Parameters.AddWithValue("$price",price);
            insert.Parameters.AddWithValue("$now",now);
            long listingId=Convert.ToInt64(insert.ExecuteScalar());
            var listing=new PlayerShopListing(
                listingId,sellerCharacterId,item.Value.ItemUid,
                item.Value.ItemDefinitionId,quantity,
                item.Value.SourceDropId,item.Value.DurabilityRaw,price,
                PlayerShopListingState.Active,
                EnhancementLevel:item.Value.EnhancementLevel,
                SourceKind:item.Value.SourceKind,
                SourceReference:item.Value.SourceReference);
            tx.Commit();
            return new PlayerShopCreateResult(PlayerShopCreateStatus.Created,listing);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlayerShopListing>> GetActivePlayerShopListingsAsync(
        long sellerCharacterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(sellerCharacterId<=0)
            throw new ArgumentOutOfRangeException(nameof(sellerCharacterId));
        return await Task.Run<IReadOnlyList<PlayerShopListing>>(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            var listings=ReadActivePlayerShopListings(connection,tx,sellerCharacterId);
            tx.Commit();
            return listings;
        },cancellationToken).ConfigureAwait(false);
    }

    // Compatibility helper for older single-listing call sites. New stall code
    // must use GetActivePlayerShopListingsAsync when the complete catalog matters.
    public async Task<PlayerShopListing?> GetActivePlayerShopListingAsync(
        long sellerCharacterId,CancellationToken cancellationToken=default)
    {
        var listings=await GetActivePlayerShopListingsAsync(
            sellerCharacterId,cancellationToken).ConfigureAwait(false);
        return listings.Count==0?null:listings[0];
    }

    public async Task<PlayerShopListing?> GetPlayerShopListingAsync(
        long listingId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(listingId<=0)throw new ArgumentOutOfRangeException(nameof(listingId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            var listing=ReadPlayerShopListing(connection,tx,listingId);
            tx.Commit();
            return listing;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CloseAllPlayerShopListingsAsync(
        long sellerCharacterId,CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(sellerCharacterId<=0)
            throw new ArgumentOutOfRangeException(nameof(sellerCharacterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using(var lockSeller=connection.CreateCommand())
            {
                lockSeller.Transaction=tx;
                lockSeller.CommandText=
                    "UPDATE characters SET updated_unix_ms=updated_unix_ms WHERE character_id=$seller;";
                lockSeller.Parameters.AddWithValue("$seller",sellerCharacterId);
                if(lockSeller.ExecuteNonQuery()!=1)
                    throw new KeyNotFoundException("Unknown player-shop seller");
            }
            var active=ReadActivePlayerShopListings(connection,tx,sellerCharacterId);
            foreach(var listing in active)
                RestorePlayerShopEscrow(connection,tx,listing,sellerCharacterId);
            using var update=connection.CreateCommand();
            update.Transaction=tx;
            update.CommandText=@"UPDATE player_shop_listings
SET state=2,updated_unix_ms=$now
WHERE seller_character_id=$seller AND state=0;";
            update.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            update.Parameters.AddWithValue("$seller",sellerCharacterId);
            int count=update.ExecuteNonQuery();
            if(count!=active.Count)
                throw new InvalidDataException("Player shop close-all lost active listing");
            tx.Commit();
            return count;
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlayerShopCloseResult> ClosePlayerShopListingAsync(
        long sellerCharacterId,long listingId,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(sellerCharacterId<=0||listingId<=0)
            throw new ArgumentOutOfRangeException(nameof(sellerCharacterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            using(var lockRow=connection.CreateCommand())
            {
                lockRow.Transaction=tx;
                lockRow.CommandText=@"
UPDATE player_shop_listings SET state=state
WHERE listing_id=$listing;";
                lockRow.Parameters.AddWithValue("$listing",listingId);
                lockRow.ExecuteNonQuery();
            }
            var listing=ReadPlayerShopListing(connection,tx,listingId);
            if(listing is null||listing.SellerCharacterId!=sellerCharacterId)
            {
                tx.Commit();
                return new PlayerShopCloseResult(PlayerShopCloseStatus.NotFound,listingId);
            }
            if(listing.State!=PlayerShopListingState.Active)
            {
                tx.Commit();
                return new PlayerShopCloseResult(
                    PlayerShopCloseStatus.AlreadyClosed,listingId);
            }
            RestorePlayerShopEscrow(connection,tx,listing,sellerCharacterId);
            using var update=connection.CreateCommand();
            update.Transaction=tx;
            update.CommandText=@"
UPDATE player_shop_listings SET state=2,updated_unix_ms=$now
WHERE listing_id=$listing AND seller_character_id=$seller AND state=0;";
            update.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            update.Parameters.AddWithValue("$listing",listingId);
            update.Parameters.AddWithValue("$seller",sellerCharacterId);
            if(update.ExecuteNonQuery()!=1)
                throw new InvalidDataException("Player shop close lost active listing");
            tx.Commit();
            return new PlayerShopCloseResult(PlayerShopCloseStatus.Closed,listingId);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlayerShopBuyResult> BuyPlayerShopListingAsync(
        long buyerCharacterId,long listingId,
        IReadOnlyDictionary<int,TradeItemRule> itemRules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(itemRules);
        if(buyerCharacterId<=0||listingId<=0)
            throw new ArgumentOutOfRangeException(nameof(buyerCharacterId));
        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            // Acquire SQLite's writer serialization before reading state so
            // concurrent buyers cannot both act on the same active snapshot.
            using(var lockRow=connection.CreateCommand())
            {
                lockRow.Transaction=tx;
                lockRow.CommandText=@"
UPDATE player_shop_listings SET state=state
WHERE listing_id=$listing;";
                lockRow.Parameters.AddWithValue("$listing",listingId);
                lockRow.ExecuteNonQuery();
            }

            var listing=ReadPlayerShopListing(connection,tx,listingId);
            if(listing is null)
            {
                tx.Commit();
                return new PlayerShopBuyResult(PlayerShopBuyStatus.NotFound,null);
            }
            if(listing.State==PlayerShopListingState.Sold)
            {
                tx.Commit();
                return new PlayerShopBuyResult(
                    listing.BuyerCharacterId==buyerCharacterId
                        ?PlayerShopBuyStatus.AlreadyApplied
                        :PlayerShopBuyStatus.Sold,
                    listing);
            }
            if(listing.State==PlayerShopListingState.Closed)
            {
                tx.Commit();
                return new PlayerShopBuyResult(PlayerShopBuyStatus.Closed,listing);
            }
            if(listing.State==PlayerShopListingState.Stale)
            {
                tx.Commit();
                return new PlayerShopBuyResult(PlayerShopBuyStatus.StaleListing,listing);
            }
            if(listing.SellerCharacterId==buyerCharacterId)
            {
                tx.Commit();
                return new PlayerShopBuyResult(PlayerShopBuyStatus.SelfPurchase,listing);
            }

            // Legacy active rows (created before escrow) may still own
            // the UID in the seller inventory. New rows intentionally do not.
            var current=ReadPlayerShopOwnedItem(
                connection,tx,listing.SellerCharacterId,listing.ItemUid);
            if(current is { } legacy&&
               !PlayerShopSnapshotMatches(listing,legacy))
            {
                MarkPlayerShopStale(connection,tx,listing.ListingId);
                tx.Commit();
                return new PlayerShopBuyResult(
                    PlayerShopBuyStatus.StaleListing,
                    listing with{State=PlayerShopListingState.Stale});
            }
            if(!itemRules.TryGetValue(listing.ItemDefinitionId,out var rule)||
               !rule.CanTrade)
            {
                MarkPlayerShopStale(connection,tx,listing.ListingId);
                tx.Commit();
                return new PlayerShopBuyResult(
                    PlayerShopBuyStatus.NotTradeable,
                    listing with{State=PlayerShopListingState.Stale});
            }
            if(!CharacterExists(connection,tx,buyerCharacterId))
                throw new KeyNotFoundException("Unknown player-shop buyer");

            EnsureWallet(connection,tx,listing.SellerCharacterId);
            EnsureWallet(connection,tx,buyerCharacterId);
            long sellerBefore=ReadWallet(connection,tx,listing.SellerCharacterId);
            long buyerBefore=ReadWallet(connection,tx,buyerCharacterId);
            if(buyerBefore<listing.Price)
            {
                tx.Commit();
                return new PlayerShopBuyResult(
                    PlayerShopBuyStatus.InsufficientMoney,listing);
            }
            if(sellerBefore>MaximumMoney-listing.Price)
            {
                tx.Commit();
                return new PlayerShopBuyResult(PlayerShopBuyStatus.WalletLimit,listing);
            }
            long sellerAfter=sellerBefore+listing.Price;
            long buyerAfter=buyerBefore-listing.Price;

            if(current is not null)
            {
                using var move=connection.CreateCommand();
                move.Transaction=tx;
                move.CommandText=@"
UPDATE item_instances SET character_id=$buyer
WHERE item_uid=$uid AND character_id=$seller;";
                move.Parameters.AddWithValue("$buyer",buyerCharacterId);
                move.Parameters.AddWithValue("$uid",listing.ItemUid);
                move.Parameters.AddWithValue("$seller",listing.SellerCharacterId);
                if(move.ExecuteNonQuery()!=1)
                    throw new InvalidDataException(
                        "Legacy player shop item ownership changed during commit");
            }
            else
                InsertPlayerShopEscrow(connection,tx,listing,buyerCharacterId);
            UpdateWallet(connection,tx,listing.SellerCharacterId,sellerAfter);
            UpdateWallet(connection,tx,buyerCharacterId,buyerAfter);
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using(var sold=connection.CreateCommand())
            {
                sold.Transaction=tx;
                sold.CommandText=@"
UPDATE player_shop_listings
SET state=1,buyer_character_id=$buyer,
    seller_wallet_after=$sellerAfter,buyer_wallet_after=$buyerAfter,
    updated_unix_ms=$now
WHERE listing_id=$listing AND state=0;";
                sold.Parameters.AddWithValue("$buyer",buyerCharacterId);
                sold.Parameters.AddWithValue("$sellerAfter",sellerAfter);
                sold.Parameters.AddWithValue("$buyerAfter",buyerAfter);
                sold.Parameters.AddWithValue("$now",now);
                sold.Parameters.AddWithValue("$listing",listing.ListingId);
                if(sold.ExecuteNonQuery()!=1)
                    throw new InvalidDataException(
                        "Player shop sale lost active listing");
            }
            tx.Commit();
            return new PlayerShopBuyResult(
                PlayerShopBuyStatus.Applied,
                listing with{
                    State=PlayerShopListingState.Sold,
                    BuyerCharacterId=buyerCharacterId,
                    SellerWalletAfter=sellerAfter,
                    BuyerWalletAfter=buyerAfter});
        },cancellationToken).ConfigureAwait(false);
    }

    private static bool PlayerShopSnapshotMatches(
        PlayerShopListing listing,PlayerShopOwnedItem item)=>
        listing.ItemUid==item.ItemUid&&
        listing.ItemDefinitionId==item.ItemDefinitionId&&
        listing.Quantity==item.Quantity&&
        listing.SourceDropId==item.SourceDropId&&
        listing.DurabilityRaw==item.DurabilityRaw&&
        listing.EnhancementLevel==item.EnhancementLevel&&
        listing.SourceKind==item.SourceKind&&
        listing.SourceReference==item.SourceReference;

    private static void RestorePlayerShopEscrow(
        SqliteConnection connection,SqliteTransaction tx,
        PlayerShopListing listing,long owner)
    {
        var legacy=ReadPlayerShopOwnedItem(connection,tx,owner,listing.ItemUid);
        if(legacy is { } existing)
        {
            if(!PlayerShopSnapshotMatches(listing,existing))
                throw new InvalidDataException(
                    "Legacy player-shop inventory UID no longer matches listing");
            return;
        }
        InsertPlayerShopEscrow(connection,tx,listing,owner);
    }

    private static void InsertPlayerShopEscrow(
        SqliteConnection connection,SqliteTransaction tx,
        PlayerShopListing listing,long owner)
    {
        using var insert=connection.CreateCommand();
        insert.Transaction=tx;
        insert.CommandText=@"
INSERT INTO item_instances(
 item_uid,character_id,item_definition_id,quantity,source_drop_id,
 durability_raw,enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($uid,$owner,$definition,$quantity,$drop,$durability,$enhancement,
       $sourceKind,$sourceReference,$now);";
        insert.Parameters.AddWithValue("$uid",listing.ItemUid);
        insert.Parameters.AddWithValue("$owner",owner);
        insert.Parameters.AddWithValue("$definition",listing.ItemDefinitionId);
        insert.Parameters.AddWithValue("$quantity",listing.Quantity);
        insert.Parameters.AddWithValue("$drop",
            listing.SourceDropId==0?DBNull.Value:listing.SourceDropId);
        insert.Parameters.AddWithValue("$durability",
            listing.DurabilityRaw.HasValue
                ?listing.DurabilityRaw.Value:DBNull.Value);
        insert.Parameters.AddWithValue("$enhancement",listing.EnhancementLevel);
        insert.Parameters.AddWithValue("$sourceKind",listing.SourceKind);
        insert.Parameters.AddWithValue("$sourceReference",listing.SourceReference);
        insert.Parameters.AddWithValue(
            "$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(insert.ExecuteNonQuery()!=1)
            throw new InvalidDataException("Player shop escrow restore failed");
    }

    private static void MarkPlayerShopStale(
        SqliteConnection connection,SqliteTransaction tx,long listingId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
UPDATE player_shop_listings SET state=3,updated_unix_ms=$now
WHERE listing_id=$listing AND state=0;";
        command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$listing",listingId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidDataException("Player shop stale transition lost listing");
    }

    private static List<PlayerShopListing> ReadActivePlayerShopListings(
        SqliteConnection connection,SqliteTransaction tx,long sellerCharacterId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
SELECT listing_id,seller_character_id,item_uid,item_definition_id,quantity,
       COALESCE(source_drop_id,0),durability_raw,price,state,
       COALESCE(buyer_character_id,0),COALESCE(seller_wallet_after,0),
       COALESCE(buyer_wallet_after,0),enhancement_level,
       source_kind,source_reference
FROM player_shop_listings
WHERE seller_character_id=$seller AND state=0
ORDER BY listing_id ASC
LIMIT $limit;";
        command.Parameters.AddWithValue("$seller",sellerCharacterId);
        command.Parameters.AddWithValue("$limit",PlayerShopMaximumActiveListings);
        using var reader=command.ExecuteReader();
        var result=new List<PlayerShopListing>();
        while(reader.Read())result.Add(ReadPlayerShopListing(reader));
        return result;
    }

    private static PlayerShopListing? ReadPlayerShopListing(
        SqliteConnection connection,SqliteTransaction tx,long listingId)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
SELECT listing_id,seller_character_id,item_uid,item_definition_id,quantity,
       COALESCE(source_drop_id,0),durability_raw,price,state,
       COALESCE(buyer_character_id,0),COALESCE(seller_wallet_after,0),
       COALESCE(buyer_wallet_after,0),enhancement_level,
       source_kind,source_reference
FROM player_shop_listings WHERE listing_id=$listing;";
        command.Parameters.AddWithValue("$listing",listingId);
        using var reader=command.ExecuteReader();
        return reader.Read()?ReadPlayerShopListing(reader):null;
    }

    private static PlayerShopListing ReadPlayerShopListing(SqliteDataReader reader)=>
        new(
            reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),
            reader.GetInt32(3),reader.GetInt32(4),reader.GetInt64(5),
            reader.IsDBNull(6)?null:reader.GetInt32(6),
            reader.GetInt64(7),(PlayerShopListingState)reader.GetInt32(8),
            reader.GetInt64(9),reader.GetInt64(10),reader.GetInt64(11),
            reader.GetInt32(12),reader.GetInt32(13),reader.GetString(14));

    private static PlayerShopOwnedItem? ReadPlayerShopOwnedItem(
        SqliteConnection connection,SqliteTransaction tx,long owner,long itemUid)
    {
        using var command=connection.CreateCommand();
        command.Transaction=tx;
        command.CommandText=@"
SELECT item_uid,item_definition_id,quantity,COALESCE(source_drop_id,0),
       durability_raw,enhancement_level,source_kind,source_reference
FROM item_instances WHERE item_uid=$uid AND character_id=$owner;";
        command.Parameters.AddWithValue("$uid",itemUid);
        command.Parameters.AddWithValue("$owner",owner);
        using var reader=command.ExecuteReader();
        return reader.Read()
            ?new PlayerShopOwnedItem(
                reader.GetInt64(0),reader.GetInt32(1),reader.GetInt32(2),
                reader.GetInt64(3),reader.IsDBNull(4)?null:reader.GetInt32(4),
                reader.GetInt32(5),reader.GetInt32(6),reader.GetString(7))
            :null;
    }

    private readonly record struct PlayerShopOwnedItem(
        long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId,
        int? DurabilityRaw,int EnhancementLevel,int SourceKind,string SourceReference);
}
