using SGTest.Server.Monster;
using SGTest.Server.Combat;
using System.Text.Json;
using SGTest.Server.Identity;
using SGTest.Server.Gateway;
using SGTest.Server.World;
using SGTest.Server.Wire;

internal static class RecoveryItemChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified recovery source cannot admit unverified or duplicate effects",
            ()=>Manifest(root));
        Checks.Run("recovery consumes item quantity with HP in one SQLite commit, replaying UID once",
            ()=>DurableUse(root).GetAwaiter().GetResult());
        Checks.Run("recovery rejecting full HP, wrong owner, unknown item and fake rule never consumes",
            ()=>Rejections(root).GetAwaiter().GetResult());
        Checks.Run("recovery queues damage before item heal, then resumes one consistent combat timeline",
            ()=>DamageDrainRecovery(root).GetAwaiter().GetResult());
    }

    private static void Manifest(string root)
    {
        string path=Path.Combine(root,"verified-recovery.json");
        var valid=new {
            kind="verified_recovery_item_rules",version=1,
            rules_verified=true,gameplay_ready=true,
            source_reference="ci:explicitly-audited-synthetic-tres",
            rules=new [] {new{item_definition_id=1001,restore_hp=140,required_level=1}}
        };
        File.WriteAllText(path,JsonSerializer.Serialize(valid));
        var rules=RecoveryItemCatalog.LoadVerified(path);
        Checks.Require(rules.Count==1 &&
            rules[1001]==new RecoveryItemRule(1001,140,1),
            "synthetic operator-verified source rejected");
        var unverified=new{
            kind=valid.kind,version=1,rules_verified=false,
            gameplay_ready=true,source_reference=valid.source_reference,rules=valid.rules
        };
        File.WriteAllText(path,JsonSerializer.Serialize(unverified));
        bool rejected=false;
        try {RecoveryItemCatalog.LoadVerified(path);}catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,"unverified client potion was admitted as server effect");
        var duplicate=new{
            kind=valid.kind,version=1,rules_verified=true,
            gameplay_ready=true,source_reference=valid.source_reference,
            rules=new[]{valid.rules[0],valid.rules[0]}
        };
        File.WriteAllText(path,JsonSerializer.Serialize(duplicate));
        rejected=false;
        try {RecoveryItemCatalog.LoadVerified(path);}catch(InvalidDataException){rejected=true;}
        Checks.Require(rejected,"duplicate recovery rule admitted");
        File.WriteAllText(path,JsonSerializer.Serialize(valid));
    }

    private static async Task<(SqliteIdentityStore Store,string Db,long Character,long Other,
        long ItemUid,long Generation)> Setup(string root,string suffix,int currentHp=60,int maxHp=100)
    {
        string db=Path.Combine(root,"recovery-"+suffix+"-"+Guid.NewGuid().ToString("N")+".db");
        var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync("reco_"+suffix,
            "Verified-Recovery-Password-123!")).AccountId;
        long character=(await store.CreateCharacterAsync(account,0,
            "Recovery"+suffix+"A",new CharacterSpawn(13,24,24))).Character!.CharacterId;
        long other=(await store.CreateCharacterAsync(account,1,
            "Recovery"+suffix+"B",new CharacterSpawn(13,24,24))).Character!.CharacterId;
        var profile=new CharacterCombatProfileData(
            CombatProfileTrust.SyntheticTest,"ci:recovery-rule-fixture",
            Race:0,Career:0,Strength:5,Dexterity:5,Constitution:5,Wisdom:5,
            Level:1,CurrentHp:currentHp,MaxHp:maxHp,Attack:20,Defense:5,
            Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15);
        await store.SetCharacterCombatProfileAsync(character,profile);
        long generation=await store.AllocateLootWorldGenerationAsync();
        var drop=await store.CreateTrustedGroundDropAsync(new TrustedDropSpawn(
            13,24,24,1001300001,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,123),
            0,1001,2,LootSourceTrust.SyntheticTest,"ci:recovery-drop"));
        var claim=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(character,19,7,1,13,24,24),drop.DropId);
        Checks.Require(claim.Status==PickupStatus.Granted && claim.ItemUid>0,
            "source item stack was not granted through SQLite");
        return (store,db,character,other,claim.ItemUid,generation);
    }

    private static readonly IReadOnlyDictionary<int,RecoveryItemRule> Rule=
        new Dictionary<int,RecoveryItemRule>{{1001,new(1001,30,1)}};
    private static TrustedRecoveryUseContext Context(long actor,long generation,
        uint sequence,int expectedHp=60) =>
        new(actor,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
            7,sequence,expectedHp);

    private static async Task DurableUse(string root)
    {
        var (store,db,character,other,uid,generation)=await Setup(root,"grant");
        using(store)
        {
            var context=Context(character,generation,1);
            var one=await store.UseVerifiedRecoveryItemAsync(context,uid,Rule);
            Checks.Require(one.Status==RecoveryUseStatus.Granted &&
                one.CurrentHp==90 && one.PreviousHp==60 &&
                one.RemainingQuantity==1,
                "first recovery did not atomically heal and subtract one");
            var replay=await store.UseVerifiedRecoveryItemAsync(context,uid,Rule);
            Checks.Require(replay.Status==RecoveryUseStatus.AlreadyGranted &&
                replay.CurrentHp==one.CurrentHp &&
                replay.RemainingQuantity==1,
                "replay consumed recovery item twice");
            var wrong=await store.UseVerifiedRecoveryItemAsync(context,uid+100,Rule);
            Checks.Require(wrong.Status==RecoveryUseStatus.StaleSequence,
                "same use sequence allowed a different UID");
            var next=await store.UseVerifiedRecoveryItemAsync(
                Context(character,generation,2,90),uid,Rule);
            Checks.Require(next.Status==RecoveryUseStatus.Granted &&
                next.CurrentHp==100 && next.RemainingQuantity==0 &&
                (await store.ListInventoryItemsAsync(character)).Count==0,
                "final unit did not delete stack and cap persistent HP");
            var gone=await store.UseVerifiedRecoveryItemAsync(
                Context(character,generation,3,100),uid,Rule);
            Checks.Require(gone.Status==RecoveryUseStatus.Missing,
                "consumed stack could be used again");
            using var reopened=new SqliteIdentityStore(db);
            await reopened.InitializeAsync();
            var returned=await reopened.UseVerifiedRecoveryItemAsync(context,uid,Rule);
            Checks.Require(returned.Status==RecoveryUseStatus.AlreadyGranted &&
                returned.CurrentHp==90 && returned.RemainingQuantity==1 &&
                (await reopened.GetCharacterCombatProfileAsync(character))?.Data.CurrentHp==100,
                "SQLite reopen lost one-time consumption replay and durable HP");
            Checks.Require(other>0,"second character fixture vanished");
        }
    }

    private static async Task DamageDrainRecovery(string root)
    {
        var (store,_,character,_,uid,generation)=await Setup(root,"drain");
        using(store)
        {
            var actor=new ActorSnapshot(character,13,1,1,0,0,24,24,1,false);
            var profile=(await store.GetCharacterCombatProfileAsync(character))!.Data;
            var gateway=new GatewayConnection(actor);
            var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
            Checks.Require(settlement.TryAttach(actor,profile) &&
                settlement.TryResume(actor),
                "combat HP owner did not attach to trusted recovery actor");
            MonsterWorldEvent Hit(int revision)=>new(
                MonsterWorldEventKind.Attack,10,
                new MonsterSnapshot(1001300001,1,0,13,40,24,40,24,
                    100,100,MonsterAiState.Attack,actor.ActorId,revision,2),
                actor.ActorId,20);
            Checks.Require(settlement.TryPostMonsterHit(actor,Hit(2))==
                CombatPostStatus.Accepted,
                "first monster hit reservation rejected");
            var drain=settlement.SuspendAndDrainAsync(
                actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
            Checks.Require(!drain.IsCompleted,
                "outstanding damage unexpectedly skipped drain gate");
            using var stop=new CancellationTokenSource();
            var worker=settlement.RunAsync(stop.Token);
            await drain.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(settlement.TryGetSuspendedHp(actor,out int hp) &&
                hp==41 && settlement.CurrentHp(actor)==0,
                "recovery observed reserved rather than committed combat HP");
            var result=await store.UseVerifiedRecoveryItemAsync(
                Context(character,generation,1,hp),uid,Rule);
            Checks.Require(result.Status==RecoveryUseStatus.Granted &&
                result.PreviousHp==41 && result.CurrentHp==71 &&
                settlement.TryApplyCommittedRecovery(actor,result),
                "HP recovery did not update projected and committed combat state");
            var request=new TrustedRecoveryUse(
                actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,1,uid);
            Checks.Require(gateway.QueueRecoveryUseResult(request,result)==
                GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out var frame) &&
                WireProtocol.TryDecodeRecoveryUseResult(frame,out var message) &&
                message.CurrentHp==71 && message.RemainingQuantity==1,
                "recovery receipt not delivered after DB commit");
            Checks.Require(settlement.TryResume(actor) &&
                settlement.CurrentHp(actor)==71,
                "recovery reactivation still using old projected HP");
            Checks.Require(settlement.TryPostMonsterHit(actor,Hit(3))==
                CombatPostStatus.Accepted,
                "monster damage did not resume after queued recovery receipt");
            using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var oldHit=await settlement.Results.ReadAsync(wait.Token);
            var nextHit=await settlement.Results.ReadAsync(wait.Token);
            await settlement.SuspendAndDrainAsync(
                actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch);
            Checks.Require(oldHit.CurrentHp==41 &&
                nextHit.CurrentHp==52 &&
                (await store.GetCharacterCombatProfileAsync(character))?.Data.CurrentHp==52 &&
                settlement.TryGetSuspendedHp(actor,out var lastHp) && lastHp==52,
                "damage/HP restoration diverged after suspend/resume");
            stop.Cancel();
            await worker.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task Rejections(string root)
    {
        var (store,_,character,other,uid,generation)=await Setup(root,"reject");
        using(store)
        {
            var context=Context(character,generation,1);
            var notOwner=await store.UseVerifiedRecoveryItemAsync(
                Context(other,generation,1),uid,Rule);
            Checks.Require(notOwner.Status==RecoveryUseStatus.NotOwned,
                "another character consumed an owned item");
            var noRule=await store.UseVerifiedRecoveryItemAsync(context,uid,
                new Dictionary<int,RecoveryItemRule>());
            Checks.Require(noRule.Status==RecoveryUseStatus.NoRule,
                "unverified item effect was consumed");
            var required=await store.UseVerifiedRecoveryItemAsync(context,uid,
                new Dictionary<int,RecoveryItemRule>{{1001,new(1001,30,50)}});
            Checks.Require(required.Status==RecoveryUseStatus.RequirementsNotMet,
                "unverified higher-level action bypassed requirement");
            var wrongHp=await store.UseVerifiedRecoveryItemAsync(
                context with {ExpectedCommittedHp=59},uid,Rule);
            Checks.Require(wrongHp.Status==RecoveryUseStatus.CombatDiverged,
                "stale reserved HP enabled item consumption");
            var after=await store.ListInventoryItemsAsync(character);
            Checks.Require(after.Count==1 && after[0].Quantity==2 &&
                (await store.GetCharacterCombatProfileAsync(character))?.Data.CurrentHp==60,
                "rejected recovery mutated HP or inventory");
            var profile=(await store.GetCharacterCombatProfileAsync(character))!.Data;
            await store.SetCharacterCombatProfileAsync(
                character,profile with {CurrentHp=profile.MaxHp});
            var full=await store.UseVerifiedRecoveryItemAsync(
                Context(character,generation,1,profile.MaxHp),uid,Rule);
            Checks.Require(full.Status==RecoveryUseStatus.FullHp &&
                (await store.ListInventoryItemsAsync(character)).Single().Quantity==2,
                "full HP recovery consumed item even though no heal could apply");
        }
    }
}
