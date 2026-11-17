using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class InventoryDiscardChecks
{
    private const string Request =
        "2000000001002700090000000000000007000000591B00000000000002000000";
    private const string Result =
        "290000000100280009000000000000000700000000591B000000000000E90300000200000001000000";

    public static void Verify(string root)
    {
        Checks.Run("inventory discard is atomic durable and replay-safe after last stack removal",
            ()=>Durable(root).GetAwaiter().GetResult());
        Checks.Run("inventory discard 32/41 V1 golden binds authenticated identity and reliable result",
            WireAndGateway);
    }

    private static TrustedDropSpawn Drop(long generation,long death,int quantity) =>
        new(13,24,24,1001301000+death,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,death),
            0,2701,quantity,LootSourceTrust.SyntheticTest,
            "ci:inventory-discard-fixture");

    private static TrustedPickupContext Pickup(long character,uint sequence) =>
        new(character,31,7,sequence,13,24,24);

    private static async Task<(SqliteIdentityStore Store,string Db,long A,long B,
        long ItemUid,long OtherUid)> Setup(string root)
    {
        string db=Path.Combine(root,"discard-"+Guid.NewGuid().ToString("N")+".db");
        var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        string nonce=Guid.NewGuid().ToString("N");
        long account=(await store.CreateAccountAsync(
            "discard_"+nonce[..10],"Inventory-Discard-Password-123!")).AccountId;
        long a=(await store.CreateCharacterAsync(account,0,"DiscardA"+nonce[..6],
            new CharacterSpawn(13,24,24))).Character!.CharacterId;
        long b=(await store.CreateCharacterAsync(account,1,"DiscardB"+nonce[6..12],
            new CharacterSpawn(13,24,24))).Character!.CharacterId;
        long generation=await store.AllocateLootWorldGenerationAsync();
        var first=await store.CreateTrustedGroundDropAsync(Drop(generation,1,3));
        var other=await store.CreateTrustedGroundDropAsync(Drop(generation,2,1));
        var aGrant=await store.ClaimGroundDropAsync(Pickup(a,1),first.DropId);
        var bGrant=await store.ClaimGroundDropAsync(Pickup(b,1),other.DropId);
        Checks.Require(aGrant.Status==PickupStatus.Granted &&
            bGrant.Status==PickupStatus.Granted,"discard fixture inventory grant");
        return(store,db,a,b,aGrant.ItemUid,bGrant.ItemUid);
    }

    private static async Task Durable(string root)
    {
        var setup=await Setup(root);
        var ctx=new TrustedInventoryDiscardContext(setup.A,71,9,1);
        using(setup.Store)
        {
            var applied=await setup.Store.DiscardInventoryItemAsync(ctx,setup.ItemUid,2);
            var replay=await setup.Store.DiscardInventoryItemAsync(ctx,setup.ItemUid,2);
            var one=(await setup.Store.ListInventoryItemsAsync(setup.A)).Single();
            Checks.Require(applied.Status==InventoryDiscardStatus.Applied &&
                applied.ItemDefinitionId==2701 && applied.DiscardedQuantity==2 &&
                applied.RemainingQuantity==1 &&
                replay.Status==InventoryDiscardStatus.AlreadyApplied &&
                replay.DiscardedQuantity==2&&replay.RemainingQuantity==1 &&
                one.ItemUid==setup.ItemUid&&one.Quantity==1,
                "discard replay double-consumed stack");

            var sameSequenceDifferentAmount=await setup.Store.DiscardInventoryItemAsync(
                ctx,setup.ItemUid,1);
            Checks.Require(sameSequenceDifferentAmount.Status==
                InventoryDiscardStatus.StaleSequence,
                "same durable action sequence changed discard payload");

            var next=ctx with {ActionSequence=2};
            var tooMany=await setup.Store.DiscardInventoryItemAsync(next,setup.ItemUid,2);
            var foreign=await setup.Store.DiscardInventoryItemAsync(next,setup.OtherUid,1);
            Checks.Require(tooMany.Status==InventoryDiscardStatus.InvalidQuantity &&
                foreign.Status==InventoryDiscardStatus.NotOwned &&
                (await setup.Store.ListInventoryItemsAsync(setup.A)).Single().Quantity==1,
                "rejected discard mutated inventory or claimed foreign UID");

            var final=await setup.Store.DiscardInventoryItemAsync(next,setup.ItemUid,1);
            Checks.Require(final.Status==InventoryDiscardStatus.Applied &&
                final.RemainingQuantity==0 &&
                (await setup.Store.ListInventoryItemsAsync(setup.A)).Count==0 &&
                (await setup.Store.ListInventoryItemsAsync(setup.B)).Single().ItemUid==
                    setup.OtherUid,
                "last discard did not atomically delete exact owned stack");
        }

        using var reopened=new SqliteIdentityStore(setup.Db);
        await reopened.InitializeAsync();
        var after=await reopened.DiscardInventoryItemAsync(
            ctx with {ActionSequence=2},setup.ItemUid,1);
        var stale=await reopened.DiscardInventoryItemAsync(ctx,setup.OtherUid,1);
        Checks.Require(after.Status==InventoryDiscardStatus.AlreadyApplied &&
            after.RemainingQuantity==0 &&
            stale.Status==InventoryDiscardStatus.StaleSequence &&
            (await reopened.ListInventoryItemsAsync(setup.A)).Count==0 &&
            (await reopened.ListInventoryItemsAsync(setup.B)).Count==1,
            "DB reopen lost discard receipt or resurrected inventory");
    }

    private static void WireAndGateway()
    {
        var request=new InventoryDiscardWire(9,7,7001,2);
        var bytes=WireProtocol.EncodeInventoryDiscard(request);
        Checks.Require(bytes.Length==32&&Convert.ToHexString(bytes)==Request&&
            WireProtocol.TryDecodeInventoryDiscard(bytes,out var decoded)&&decoded==request,
            "discard request golden");
        var receipt=new InventoryDiscardResultWire(
            9,7,InventoryDiscardStatus.Applied,7001,1001,2,1);
        var response=WireProtocol.EncodeInventoryDiscardResult(receipt);
        Checks.Require(response.Length==41&&Convert.ToHexString(response)==Result&&
            WireProtocol.TryDecodeInventoryDiscardResult(response,out var parsed)&&parsed==receipt,
            "discard result golden");
        var hostile=(byte[])response.Clone();
        hostile[20]=255;
        Checks.Require(!WireProtocol.TryDecodeInventoryDiscardResult(hostile,out _),
            "unknown discard status accepted");
        hostile=(byte[])bytes.Clone();
        hostile[28]=0;hostile[29]=0;hostile[30]=0;hostile[31]=0;
        Checks.Require(!WireProtocol.TryDecodeInventoryDiscard(hostile,out _),
            "zero discard quantity accepted");

        var actor=new ActorSnapshot(11,13,19,9,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);
        TrustedInventoryDiscard trusted=default;
        var feed=gateway.FeedClientBytes(bytes,_=>true,out var accepted,
            postDiscard:value=>{trusted=value;return true;});
        Checks.Require(feed==WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==19&&
            trusted.RequestMapEpoch==9&&trusted.DiscardSequence==7&&
            trusted.ItemUid==7001&&trusted.Quantity==2,
            "client forged discard identity or quantity context");
        var result=new InventoryDiscardResult(
            InventoryDiscardStatus.Applied,7001,1001,2,1);
        Checks.Require(gateway.QueueInventoryDiscardResult(trusted,result)==
            GatewaySendStatus.Enqueued&&gateway.TryDequeueReliable(out var frame)&&
            WireProtocol.TryDecodeInventoryDiscardResult(frame,out var queued)&&queued==receipt,
            "committed discard did not enter trusted FIFO");
        Checks.Require(gateway.QueueInventoryDiscardResult(
            trusted with {ConnectionEpoch=20},result)==GatewaySendStatus.GenerationMismatch,
            "stale discard generation received receipt");
        var forged=new GatewayConnection(actor);
        Checks.Require(forged.FeedClientBytes(response,_=>true,out _)==
            WireFeedStatus.ProtocolViolation,
            "client forged server-only discard result");
    }
}
