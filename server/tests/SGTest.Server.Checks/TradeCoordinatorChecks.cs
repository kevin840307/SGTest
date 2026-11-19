using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class TradeCoordinatorChecks
{
    public static void Verify(string root)
    {
        Checks.Run("trade coordinator requires accept lock relock and dual final confirm",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string dir=Path.Combine(root,"trade_coord_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        string mapDir=Checks.Fixture(root,97,5,5,(_,_)=>0);
        var grid=BlockingGrid.Load(mapDir,97);
        await using var world=new ServerWorldHost([grid]);
        await world.StartAsync();
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long accountA=(await store.CreateAccountAsync(
            "trade_coord_a","Trade-Coord-A-Password-123!")).AccountId;
        long accountB=(await store.CreateAccountAsync(
            "trade_coord_b","Trade-Coord-B-Password-123!")).AccountId;
        long charA=(await store.CreateCharacterAsync(
            accountA,0,"TradeCoordA",new CharacterSpawn(97,24,24)))
            .Character?.CharacterId??throw new Exception("trade coord A missing");
        long charB=(await store.CreateCharacterAsync(
            accountB,0,"TradeCoordB",new CharacterSpawn(97,28,24)))
            .Character?.CharacterId??throw new Exception("trade coord B missing");
        SeedWallet(db,charA,1000);
        SeedWallet(db,charB,500);
        long generation=await store.AllocateLootWorldGenerationAsync();
        var seededA=await SeedItem(store,generation,charA,4103,2,1,97,24,24);
        var seededB=await SeedItem(store,generation,charB,4104,1,2,97,28,24);

        var actorA=await world.TrustedRegisterAsync(charA,97,24,24)
            ??throw new Exception("trade world A register");
        var actorB=await world.TrustedRegisterAsync(charB,97,28,24)
            ??throw new Exception("trade world B register");
        var gatewayA=new GatewayConnection(actorA);
        var gatewayB=new GatewayConnection(actorB);
        var router=new WorldResultRouter();
        Checks.Require(router.TryAttach(gatewayA)&&router.TryAttach(gatewayB),
            "trade gateways attach");
        var rules=new Dictionary<int,TradeItemRule>
        {
            [4103]=new(4103,true,"synthetic:trade-coord-4103"),
            [4104]=new(4104,true,"synthetic:trade-coord-4104")
        };
        var coordinator=new PlayerTradeCoordinator(store,world,rules);

        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,1,
            TradeCommandAction.Request,0,charB,0,0),router);
        var inviteA=DrainSnapshot(gatewayA);
        var inviteB=DrainSnapshot(gatewayB);
        Checks.Require(inviteA.State==TradeSessionWireState.InviteSent&&
            inviteB.State==TradeSessionWireState.InviteReceived&&
            inviteA.TradeId==inviteB.TradeId&&inviteA.PeerActorId==charB&&
            inviteB.PeerActorId==charA&&coordinator.IsTrading(charA)&&
            coordinator.IsTrading(charB),"trade invite state mismatch");
        long tradeId=inviteA.TradeId;

        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,1,
            TradeCommandAction.Accept,tradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.Active);

        // Both authenticated sockets have independent bounded workers, so
        // legitimate peer offer mutations can overlap while each awaits SQLite.
        // One peer advancing the session revision must not silently discard the
        // other peer's already-admitted mutation.
        await Task.WhenAll(
            coordinator.HandleAsync(new(
                charA,actorA.ConnectionEpoch,actorA.MapEpoch,2,
                TradeCommandAction.AddItem,tradeId,0,seededA.ItemUid,0),router),
            coordinator.HandleAsync(new(
                charB,actorB.ConnectionEpoch,actorB.MapEpoch,2,
                TradeCommandAction.AddItem,tradeId,0,seededB.ItemUid,0),router));
        DrainSnapshot(gatewayA);
        var offerA=DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);
        var offerB=DrainSnapshot(gatewayB);
        Checks.Require(
            offerA.MyItems.Single().ItemUid==seededA.ItemUid&&
            offerA.OtherItems.Single().ItemUid==seededB.ItemUid&&
            offerB.MyItems.Single().ItemUid==seededB.ItemUid&&
            offerB.OtherItems.Single().ItemUid==seededA.ItemUid&&
            offerA.MyItems.Single().SourceDropId==seededA.DropId&&
            offerB.MyItems.Single().SourceDropId==seededB.DropId,
            "concurrent peer item offers were lost or misoriented");
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,3,
            TradeCommandAction.SetMoney,tradeId,0,0,200),router);
        DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);

        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,4,
            TradeCommandAction.Lock,tradeId,0,0,0),router);
        var lockedA=DrainSnapshot(gatewayA);
        var lockedSeenByB=DrainSnapshot(gatewayB);
        Checks.Require(lockedA.MyLocked&&lockedSeenByB.OtherLocked,
            "trade first lock missing");

        // The still-unlocked peer may change its offer. That change MUST clear
        // the already-locked peer, forcing both to review the new terms again.
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,3,
            TradeCommandAction.SetMoney,tradeId,0,0,50),router);
        var relockA=DrainSnapshot(gatewayA);
        var relockB=DrainSnapshot(gatewayB);
        Checks.Require(!relockA.MyLocked&&!relockA.OtherLocked&&
            !relockB.MyLocked&&!relockB.OtherLocked&&
            relockA.OtherMoney==50&&relockB.MyMoney==50,
            "trade offer mutation did not reset both locks");

        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,5,
            TradeCommandAction.Lock,tradeId,0,0,0),router);
        DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,4,
            TradeCommandAction.Lock,tradeId,0,0,0),router);
        var finalA=DrainSnapshot(gatewayA);
        var finalB=DrainSnapshot(gatewayB);
        Checks.Require(finalA.State==TradeSessionWireState.AwaitingFinalConfirm&&
            finalB.State==TradeSessionWireState.AwaitingFinalConfirm&&
            finalA.MyLocked&&finalA.OtherLocked,
            "trade dual lock did not enter final confirm");

        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,6,
            TradeCommandAction.FinalConfirm,tradeId,0,0,0),router);
        var oneConfirmA=DrainSnapshot(gatewayA);
        var oneConfirmB=DrainSnapshot(gatewayB);
        Checks.Require(oneConfirmA.MyConfirmed&&!oneConfirmA.OtherConfirmed&&
            oneConfirmB.OtherConfirmed&&!oneConfirmB.MyConfirmed,
            "trade first final confirmation mismatch");
        Checks.Require(!gatewayA.TryDequeueReliable(out _)&&
            !gatewayB.TryDequeueReliable(out _),
            "trade committed before both final confirmations");

        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,5,
            TradeCommandAction.FinalConfirm,tradeId,0,0,0),router);
        var committingA=DrainSnapshot(gatewayA);
        var committingB=DrainSnapshot(gatewayB);
        Checks.Require(committingA.MyConfirmed&&committingA.OtherConfirmed&&
            committingB.MyConfirmed&&committingB.OtherConfirmed,
            "trade dual confirmation snapshot mismatch");
        var terminalA=DrainTerminal(gatewayA);
        var terminalB=DrainTerminal(gatewayB);
        Checks.Require(terminalA.Status==TradeTerminalStatus.Success&&
            terminalB.Status==TradeTerminalStatus.Success&&
            terminalA.WalletMoney==850&&terminalB.WalletMoney==650&&
            !coordinator.IsTrading(charA)&&!coordinator.IsTrading(charB),
            "trade terminal settlement mismatch");
        await AssertOwnership(store,charA,seededB.ItemUid,4104,1,seededB.DropId);
        await AssertOwnership(store,charB,seededA.ItemUid,4103,2,seededA.DropId);

        // A fresh invitation can be rejected and leaves no session.
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,7,
            TradeCommandAction.Request,0,charB,0,0),router);
        var rejectInvite=DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,6,
            TradeCommandAction.Reject,rejectInvite.TradeId,0,0,0),router);
        Checks.Require(DrainTerminal(gatewayA).Status==TradeTerminalStatus.Rejected&&
            DrainTerminal(gatewayB).Status==TradeTerminalStatus.Rejected&&
            !coordinator.IsTrading(charA)&&!coordinator.IsTrading(charB),
            "trade reject did not terminate both peers");

        // Disconnect before commit has a single peer-disconnect terminal.
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,8,
            TradeCommandAction.Request,0,charB,0,0),router);
        DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);
        coordinator.Disconnect(gatewayB.Binding,router);
        Checks.Require(DrainTerminal(gatewayA).Status==TradeTerminalStatus.PeerDisconnected&&
            DrainTerminal(gatewayB).Status==TradeTerminalStatus.PeerDisconnected&&
            !coordinator.IsTrading(charA)&&!coordinator.IsTrading(charB),
            "trade disconnect did not terminate pending session");


        // A dead peer is not a valid trade target, even though the socket and
        // MapEpoch are still authenticated and connected.
        var dead=await world.TrustedSetCombatDeadAsync(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,true);
        Checks.Require(dead is { CombatDead:true },
            "test failed to mark the trade peer dead in authoritative World");
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,9,
            TradeCommandAction.Request,0,charB,0,0),router);
        Checks.Require(!coordinator.IsTrading(charA)&&
            !gatewayA.TryDequeueReliable(out _)&&!gatewayB.TryDequeueReliable(out _),
            "dead peer received a trade invitation");
        Checks.Require((await world.TrustedSetCombatDeadAsync(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,false)) is { CombatDead:false },
            "test failed to restore live World state");

        // Both parties can lock and one can confirm, but an authoritative
        // combat death BEFORE the second final confirm must never commit
        // money or item ownership. Both peers receive a terminal receipt.
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,10,
            TradeCommandAction.Request,0,charB,0,0),router);
        var deathTrade=DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,7,
            TradeCommandAction.Accept,deathTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.Active);
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,11,
            TradeCommandAction.Lock,deathTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.Active);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,8,
            TradeCommandAction.Lock,deathTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.AwaitingFinalConfirm);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,9,
            TradeCommandAction.FinalConfirm,deathTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.AwaitingFinalConfirm);
        await world.TrustedSetCombatDeadAsync(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,true);
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,12,
            TradeCommandAction.FinalConfirm,deathTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.AwaitingFinalConfirm);
        Checks.Require(DrainTerminal(gatewayA).Status==TradeTerminalStatus.StaleGeneration&&
            DrainTerminal(gatewayB).Status==TradeTerminalStatus.StaleGeneration&&
            !coordinator.IsTrading(charA)&&!coordinator.IsTrading(charB),
            "death between final confirmations did not abort trade");
        Checks.Require(await store.GetCharacterMoneyAsync(charA)==850&&
            await store.GetCharacterMoneyAsync(charB)==650,
            "dead peer still changed durable wallet balance");
        await AssertOwnership(store,charA,seededB.ItemUid,4104,1,seededB.DropId);
        await AssertOwnership(store,charB,seededA.ItemUid,4103,2,seededA.DropId);

        // Same-map trusted teleport increments MapEpoch. A pending trade
        // cannot settle against the stale peer generation at final commit.
        await world.TrustedSetCombatDeadAsync(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,false);
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,13,
            TradeCommandAction.Request,0,charB,0,0),router);
        var transferTrade=DrainSnapshot(gatewayA);
        DrainSnapshot(gatewayB);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,10,
            TradeCommandAction.Accept,transferTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.Active);
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,14,
            TradeCommandAction.Lock,transferTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.Active);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,11,
            TradeCommandAction.Lock,transferTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.AwaitingFinalConfirm);
        await coordinator.HandleAsync(new(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,12,
            TradeCommandAction.FinalConfirm,transferTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.AwaitingFinalConfirm);
        var transferred=await world.TrustedTransferAsync(
            charB,actorB.ConnectionEpoch,actorB.MapEpoch,97,28,24);
        Checks.Require(transferred is { } moved&&moved.MapEpoch>actorB.MapEpoch,
            "test trusted transfer failed to advance MapEpoch");
        await coordinator.HandleAsync(new(
            charA,actorA.ConnectionEpoch,actorA.MapEpoch,15,
            TradeCommandAction.FinalConfirm,transferTrade.TradeId,0,0,0),router);
        RequireState(gatewayA,gatewayB,TradeSessionWireState.AwaitingFinalConfirm);
        Checks.Require(DrainTerminal(gatewayA).Status==TradeTerminalStatus.StaleGeneration&&
            DrainTerminal(gatewayB).Status==TradeTerminalStatus.StaleGeneration&&
            !coordinator.IsTrading(charA)&&!coordinator.IsTrading(charB),
            "peer MapEpoch transfer did not abort stale trade commit");
        Checks.Require(await store.GetCharacterMoneyAsync(charA)==850&&
            await store.GetCharacterMoneyAsync(charB)==650,
            "stale peer generation still changed durable wallet balance");
        await AssertOwnership(store,charA,seededB.ItemUid,4104,1,seededB.DropId);
        await AssertOwnership(store,charB,seededA.ItemUid,4103,2,seededA.DropId);

        router.TryDetach(gatewayA);
        router.TryDetach(gatewayB);
        await world.StopAsync();
    }

    private static void RequireState(
        GatewayConnection a,GatewayConnection b,TradeSessionWireState state)
    {
        Checks.Require(DrainSnapshot(a).State==state&&DrainSnapshot(b).State==state,
            "trade session state mismatch: "+state);
    }

    private static TradeSnapshotWire DrainSnapshot(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodeTradeSnapshot(frame,out var value)||
           value is null)
            throw new Exception("trade snapshot missing");
        return value;
    }

    private static TradeTerminalWire DrainTerminal(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodeTradeTerminal(frame,out var value))
            throw new Exception("trade terminal missing");
        return value;
    }

    private static async Task<(long ItemUid,long DropId)> SeedItem(
        SqliteIdentityStore store,long generation,long characterId,int definition,
        int quantity,int slot,int map,double x,double y)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            map,x,y,990000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,quantity,LootSourceTrust.SyntheticTest,
            "synthetic:trade-coordinator-"+slot));
        var grant=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(characterId,100+slot,1,1,map,x,y),drop.DropId);
        Checks.Require(grant.Status==PickupStatus.Granted&&grant.ItemUid>0,
            "trade coordinator seed failed");
        return(grant.ItemUid,drop.DropId);
    }

    private static async Task AssertOwnership(
        SqliteIdentityStore store,long owner,long uid,int definition,int quantity,long dropId)
    {
        var item=(await store.ListInventoryItemsAsync(owner)).Single(x=>x.ItemUid==uid);
        Checks.Require(item.ItemDefinitionId==definition&&item.Quantity==quantity&&
            item.SourceDropId==dropId,"trade coordinator lost UID/provenance");
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=@"
INSERT INTO character_wallets(character_id,money) VALUES($id,$money)
ON CONFLICT(character_id) DO UPDATE SET money=excluded.money;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$money",money);
        Checks.Require(command.ExecuteNonQuery()==1,"trade coordinator wallet seed failed");
    }
}
