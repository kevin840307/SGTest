using SGTest.Server.Identity;

internal static class GroundLootChecks
{
    public static void Verify(string root)
    {
        Checks.Run("loot trusted death retries produce exactly one durable ground UID",
            () => IdempotentSpawn(root).GetAwaiter().GetResult());
        Checks.Run("loot authoritative map/range/generation and retry guards",
            () => PickupGuards(root).GetAwaiter().GetResult());
        Checks.Run("loot simultaneous claimers atomically grant only one item UID across reopen",
            () => ConcurrentClaims(root).GetAwaiter().GetResult());
    }

    private static async Task<(SqliteIdentityStore Store,string Database,long A,long B)> Setup(string root, string suffix)
    {
        string db=Path.Combine(root,"loot-"+suffix+"-"+Guid.NewGuid().ToString("N")+".db");
        var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "loot_"+suffix,"Loot-Testing-Password-123!")).AccountId;
        long a=(await store.CreateCharacterAsync(account,0,"Loot"+suffix+"A",
            new CharacterSpawn(13,24,24))).Character!.CharacterId;
        long b=(await store.CreateCharacterAsync(account,1,"Loot"+suffix+"B",
            new CharacterSpawn(13,24,24))).Character!.CharacterId;
        return (store,db,a,b);
    }

    private static TrustedDropSpawn Drop(long generation, long death=9, int slot=0) =>
        new(13,24,24,1001300001,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,death),
            slot,2701,2,
            LootSourceTrust.SyntheticTest,"ci:synthetic-death");

    private static TrustedPickupContext Actor(long id,uint seq=1) =>
        new(id,19,7,seq,13,24,24);

    private static async Task IdempotentSpawn(string root)
    {
        var (store,_,a,_)=await Setup(root,"spawn");
        using(store)
        {
            long generation=await store.AllocateLootWorldGenerationAsync();
            var first=await store.CreateTrustedGroundDropAsync(Drop(generation));
            var repeated=await store.CreateTrustedGroundDropAsync(Drop(generation));
            Checks.Require(first.DropId>0 && first==repeated,
                "identical trusted death retry minted another drop");
            var second=await store.CreateTrustedGroundDropAsync(Drop(generation,slot:1));
            Checks.Require(second.DropId!=first.DropId,
                "different verified slot reused the UID");
            var ground=await store.ListActiveGroundDropsAsync(13);
            Checks.Require(ground.Count==2,"duplicate or missing ground entities");
            bool conflict=false;
            try { await store.CreateTrustedGroundDropAsync(Drop(generation) with { Quantity=3 }); }
            catch(InvalidOperationException) { conflict=true; }
            Checks.Require(conflict,"conflicting trusted retry silently changed item amount");
            bool invalid=false;
            try { await store.CreateTrustedGroundDropAsync(Drop(generation) with { SourceReference="" }); }
            catch(ArgumentOutOfRangeException) { invalid=true; }
            Checks.Require(invalid,"unverified drop was accepted");
            Checks.Require((await store.ListInventoryItemsAsync(a)).Count==0,
                "ground spawn invented an inventory item");
            long nextGeneration=await store.AllocateLootWorldGenerationAsync();
            Checks.Require(nextGeneration>generation,
                "new World generation reused an old durable boot identity");
            var afterRestart=await store.CreateTrustedGroundDropAsync(Drop(nextGeneration));
            Checks.Require(afterRestart.DropId!=first.DropId &&
                (await store.ListActiveGroundDropsAsync(13)).Count==3,
                "post-restart monster local revision collided with an earlier life");
            invalid=false;
            try { await store.CreateTrustedGroundDropAsync(Drop(987654321)); }
            catch(InvalidOperationException) { invalid=true; }
            Checks.Require(invalid,"unallocated World generation minted a drop");
            // A single monster death with two slots commits as ONE transaction.
            // On conflict with a previously committed slot, even a new slot
            // early in that batch must be rolled back.
            var multi=new[]{Drop(generation,death:40,slot:0),
                            Drop(generation,death:40,slot:1)};
            var committed=await store.CreateTrustedGroundDropsAsync(multi);
            var replay=await store.CreateTrustedGroundDropsAsync(multi);
            Checks.Require(committed.Count==2 &&
                committed[0].DropId!=committed[1].DropId &&
                replay[0].DropId==committed[0].DropId &&
                replay[1].DropId==committed[1].DropId,
                "multi-slot trusted death replay duplicated ground UIDs");
            var priorCount=(await store.ListActiveGroundDropsAsync(13)).Count;
            bool conflictBatch=false;
            try
            {
                await store.CreateTrustedGroundDropsAsync([
                    Drop(generation,death:40,slot:2),
                    Drop(generation,death:40,slot:0) with {Quantity=3}
                ]);
            }
            catch(InvalidOperationException) {conflictBatch=true;}
            Checks.Require(conflictBatch &&
                (await store.ListActiveGroundDropsAsync(13)).Count==priorCount,
                "multi-slot conflict leaked a partially committed drop");
        }
    }

    private static async Task PickupGuards(string root)
    {
        var (store,_,a,b)=await Setup(root,"guards");
        using(store)
        {
            long generation=await store.AllocateLootWorldGenerationAsync();
            var d=await store.CreateTrustedGroundDropAsync(Drop(generation));
            var c=Actor(a);
            Checks.Require((await store.ClaimGroundDropAsync(c with { MapId=15 },d.DropId)).Status==PickupStatus.WrongMap,
                "cross-map pickup succeeded");
            Checks.Require((await store.ClaimGroundDropAsync(c with { X=500 },d.DropId)).Status==PickupStatus.TooFar,
                "remote pickup succeeded");
            Checks.Require((await store.ClaimGroundDropAsync(c,999999)).Status==PickupStatus.Missing,
                "unknown drop not rejected");
            Checks.Require((await store.ClaimGroundDropAsync(c with { CharacterId=999999 },d.DropId)).Status==PickupStatus.UnknownCharacter,
                "missing character granted item");
            var won=await store.ClaimGroundDropAsync(c,d.DropId);
            var retry=await store.ClaimGroundDropAsync(c,d.DropId);
            Checks.Require(won.Status==PickupStatus.Granted && won.ItemUid>0 &&
                retry.Status==PickupStatus.AlreadyGranted && retry.ItemUid==won.ItemUid,
                "same authenticated action was not idempotent");
            var other=await store.CreateTrustedGroundDropAsync(Drop(generation,slot:1));
            Checks.Require((await store.ClaimGroundDropAsync(c,other.DropId)).Status==PickupStatus.StaleSequence,
                "same sequence replayed to different UID");
            Checks.Require((await store.ClaimGroundDropAsync(Actor(b),d.DropId)).Status==PickupStatus.AlreadyTaken,
                "second character claimed spent UID");
            var fresh=await store.ClaimGroundDropAsync(c with { ActionSequence=2 },other.DropId);
            Checks.Require(fresh.Status==PickupStatus.Granted && fresh.ItemUid!=won.ItemUid,
                "fresh authorized sequence was not granted");
            var rollback=await store.ClaimGroundDropAsync(c,other.DropId);
            Checks.Require(rollback.Status==PickupStatus.StaleSequence,
                "old sequence was allowed to reorder grants");
            var items=await store.ListInventoryItemsAsync(a);
            Checks.Require(items.Count==2 && items[0].ItemUid!=items[1].ItemUid &&
                (await store.ListInventoryItemsAsync(b)).Count==0 &&
                (await store.ListActiveGroundDropsAsync(13)).Count==0,
                "grant duplicated inventory or left picked loot on ground");
        }
    }

    private static async Task ConcurrentClaims(string root)
    {
        var (store,db,a,b)=await Setup(root,"race");
        using(store)
        {
            long generation=await store.AllocateLootWorldGenerationAsync();
            var d=await store.CreateTrustedGroundDropAsync(Drop(generation));
            var actions=new[] {
                store.ClaimGroundDropAsync(Actor(a),d.DropId),
                store.ClaimGroundDropAsync(Actor(b),d.DropId)
            };
            var results=await Task.WhenAll(actions);
            Checks.Require(results.Count(x=>x.Status==PickupStatus.Granted)==1 &&
                           results.Count(x=>x.Status==PickupStatus.AlreadyTaken)==1,
                "simultaneous claimers minted more than one item");
            long winner=results[0].Status==PickupStatus.Granted?a:b;
            long loser=winner==a?b:a;
            long uid=results.Single(x=>x.Status==PickupStatus.Granted).ItemUid;
            Checks.Require((await store.ListInventoryItemsAsync(winner)).Single().ItemUid==uid &&
                (await store.ListInventoryItemsAsync(loser)).Count==0,
                "race awarded inventory to incorrect character");
        }

        using var reopened = new SqliteIdentityStore(db);
        await reopened.InitializeAsync();
        var winnerAfterRestart = (await reopened.ListInventoryItemsAsync(a))
            .Concat(await reopened.ListInventoryItemsAsync(b)).ToArray();
        Checks.Require(winnerAfterRestart.Length==1 &&
            winnerAfterRestart[0].SourceDropId>0 &&
            (await reopened.ListActiveGroundDropsAsync(13)).Count==0,
            "restart lost claim or duplicated inventory");
        long persistedOwner=winnerAfterRestart[0].CharacterId;
        var replay = await reopened.ClaimGroundDropAsync(Actor(persistedOwner),
            winnerAfterRestart[0].SourceDropId);
        Checks.Require(replay.Status==PickupStatus.AlreadyGranted &&
            replay.ItemUid==winnerAfterRestart[0].ItemUid,
            "restart failed to return the same durable inventory UID on replay");
    }
}
