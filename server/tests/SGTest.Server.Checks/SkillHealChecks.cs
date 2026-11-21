using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class SkillHealChecks
{
    public static void Verify(string root)
    {
        Checks.Run("skill Heal atomically owns shared skill sequence MP and target HP",
            ()=>Durable(root).GetAwaiter().GetResult());
        Checks.Run("skill Heal rejects unlearned level full/dead/MP/diverged state without mutation",
            ()=>Reject(root).GetAwaiter().GetResult());
        Checks.Run("skill Heal synchronizes durable HP into suspended combat timeline",
            ()=>CombatProjection(root).GetAwaiter().GetResult());
    }

    private static CharacterCombatProfileData Profile(
        int hp,int maxHp,int mp,int maxMp)=>new(
            CombatProfileTrust.SyntheticTest,"ci:skill-heal",
            Race:0,Career:7,Strength:5,Dexterity:5,Constitution:5,Wisdom:5,
            Level:35,CurrentHp:hp,MaxHp:maxHp,Attack:20,Defense:5,
            Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15,
            CurrentMp:mp,MaxMp:maxMp);

    private static async Task<(SqliteIdentityStore Store,string Db,long Caster,long Target)>
        Setup(string root,string name,int casterHp=80,int targetHp=40,int mp=100)
    {
        string db=Path.Combine(root,"skill-heal-"+name+"-"+Guid.NewGuid().ToString("N")+".db");
        var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "heal_"+name,"Heal-Skill-Password-123!")).AccountId;
        long caster=(await store.CreateCharacterAsync(
            account,0,"Heal"+name+"A",new(13,24,24))).Character!.CharacterId;
        long target=(await store.CreateCharacterAsync(
            account,1,"Heal"+name+"B",new(13,32,24))).Character!.CharacterId;
        await store.SetCharacterCombatProfileAsync(caster,Profile(casterHp,100,mp,120));
        await store.SetCharacterCombatProfileAsync(target,Profile(targetHp,100,30,80));
        await store.SetCharacterSkillLevelAsync(caster,28101,3,"old:SKC28J101");
        return(store,db,caster,target);
    }

    private static TrustedSkillHealContext Ctx(
        long caster,long target,uint seq,int casterHp=80,int targetHp=40,
        int level=3,int cost=21,int heal=20)=>new(
            caster,7001,9,seq,28101,level,target,casterHp,targetHp,cost,heal);

    private static async Task Durable(string root)
    {
        var (store,db,caster,target)=await Setup(root,"durable");
        using(store)
        {
            var one=await store.SettleSkillHealAsync(Ctx(caster,target,1));
            Checks.Require(one.Status==SkillHealStatus.Applied&&
                one.SkillLevel==3&&one.PreviousMp==100&&one.CurrentMp==79&&
                one.TargetPreviousHp==40&&one.TargetCurrentHp==60&&
                one.HealApplied==20,
                "first durable Heal did not atomically debit MP and restore HP");
            var replay=await store.SettleSkillHealAsync(Ctx(caster,target,1));
            Checks.Require(replay.Status==SkillHealStatus.AlreadyApplied&&
                replay.CurrentMp==79&&replay.TargetCurrentHp==60,
                "exact Heal replay did not return frozen receipt");
            var conflict=await store.SettleSkillHealAsync(
                Ctx(caster,target,1,80,40,heal:19));
            Checks.Require(conflict.Status==SkillHealStatus.StaleSequence,
                "same skill sequence accepted different Heal payload");
            var next=await store.SettleSkillHealAsync(
                Ctx(caster,target,2,casterHp:80,targetHp:60,cost:21,heal:99));
            Checks.Require(next.Status==SkillHealStatus.Applied&&
                next.CurrentMp==58&&next.TargetCurrentHp==100&&
                next.HealApplied==40,
                "Heal did not cap target HP while charging exact MP once");
            var self=await store.SettleSkillHealAsync(
                Ctx(caster,caster,3,casterHp:80,targetHp:80,cost:21,heal:20));
            Checks.Require(self.Status==SkillHealStatus.Applied&&
                self.PreviousMp==58&&self.CurrentMp==37&&
                self.TargetPreviousHp==80&&self.TargetCurrentHp==100&&
                self.HealApplied==20,
                "target-zero original Cure self-heal transaction was not atomic");
            var casterProfile=(await store.GetCharacterCombatProfileAsync(caster))!.Data;
            var targetProfile=(await store.GetCharacterCombatProfileAsync(target))!.Data;
            Checks.Require(casterProfile.CurrentMp==37&&casterProfile.CurrentHp==100&&
                targetProfile.CurrentHp==100,
                "durable combat profiles diverged from target/self Heal receipts");
        }
        using var reopened=new SqliteIdentityStore(db);
        await reopened.InitializeAsync();
        var again=await reopened.SettleSkillHealAsync(Ctx(caster,target,1));
        Checks.Require(again.Status==SkillHealStatus.AlreadyApplied&&
            again.TargetCurrentHp==60&&
            (await reopened.GetCharacterCombatProfileAsync(target))!.Data.CurrentHp==100,
            "DB reopen lost historical Heal receipt or rewound live HP");
    }

    private static async Task Reject(string root)
    {
        var (store,_,caster,target)=await Setup(root,"reject",mp:20);
        using(store)
        {
            var unlearned=await store.SettleSkillHealAsync(
                Ctx(caster,target,1,level:4,cost:28,heal:28));
            Checks.Require(unlearned.Status==SkillHealStatus.SkillNotLearned,
                "server SkillBook level was not enforced");

            var insufficient=await store.SettleSkillHealAsync(Ctx(caster,target,1));
            Checks.Require(insufficient.Status==SkillHealStatus.InsufficientMp,
                "insufficient MP Heal was accepted");
            var c=(await store.GetCharacterCombatProfileAsync(caster))!.Data;
            await store.SetCharacterCombatProfileAsync(
                caster,c with {CurrentMp=100});
            var diverged=await store.SettleSkillHealAsync(
                Ctx(caster,target,1,targetHp:41));
            Checks.Require(diverged.Status==SkillHealStatus.CombatDiverged,
                "stale target combat HP was accepted");
            var applied=await store.SettleSkillHealAsync(Ctx(caster,target,1));
            Checks.Require(applied.Status==SkillHealStatus.Applied,
                "valid Heal after nonmutating rejections failed");

            var fullTarget=(await store.GetCharacterCombatProfileAsync(target))!.Data;
            await store.SetCharacterCombatProfileAsync(
                target,fullTarget with {CurrentHp=100});
            var full=await store.SettleSkillHealAsync(
                Ctx(caster,target,2,casterHp:80,targetHp:100));
            Checks.Require(full.Status==SkillHealStatus.FullHp,
                "full HP target consumed a Heal");

            var deadTarget=(await store.GetCharacterCombatProfileAsync(target))!.Data;
            await store.SetCharacterCombatProfileAsync(
                target,deadTarget with {CurrentHp=0});
            var dead=await store.SettleSkillHealAsync(
                Ctx(caster,target,2,casterHp:80,targetHp:1));
            Checks.Require(dead.Status==SkillHealStatus.TargetDead,
                "ordinary Cure revived a dead target");

            var casterAfter=(await store.GetCharacterCombatProfileAsync(caster))!.Data;
            Checks.Require(casterAfter.CurrentMp==79,
                "rejected Heal mutated caster MP");
        }
    }

    private static async Task CombatProjection(string root)
    {
        var (store,_,caster,target)=await Setup(root,"projection");
        using(store)
        {
            var casterActor=new ActorSnapshot(
                caster,13,1,1,0,0,24,24,1,false);
            var targetActor=new ActorSnapshot(
                target,13,1,1,0,0,32,24,1,false);
            var settlement=new PlayerCombatSettlement(store,new(Capacity:8));
            Checks.Require(settlement.TryAttach(
                    casterActor,(await store.GetCharacterCombatProfileAsync(caster))!.Data)&&
                settlement.TryAttach(
                    targetActor,(await store.GetCharacterCombatProfileAsync(target))!.Data)&&
                settlement.TryResume(casterActor)&&settlement.TryResume(targetActor),
                "Heal combat owners did not attach");
            await settlement.SuspendAndDrainAsync(
                casterActor.ActorId,casterActor.ConnectionEpoch,casterActor.MapEpoch);
            await settlement.SuspendAndDrainAsync(
                targetActor.ActorId,targetActor.ConnectionEpoch,targetActor.MapEpoch);
            int casterHp=0,targetHp=0;
            Checks.Require(settlement.TryGetSuspendedCombatState(
                    casterActor,out casterHp,out _)&&
                settlement.TryGetSuspendedCombatState(
                    targetActor,out targetHp,out _)&&
                casterHp==80&&targetHp==40,
                "Heal did not observe committed suspended HP");
            var result=await store.SettleSkillHealAsync(
                Ctx(caster,target,1,casterHp,targetHp));
            Checks.Require(result.Status==SkillHealStatus.Applied&&
                settlement.TryApplyCommittedSkillHeal(targetActor,result)&&
                settlement.TryResume(targetActor)&&settlement.CurrentHp(targetActor)==60&&
                settlement.TryResume(casterActor)&&settlement.CurrentHp(casterActor)==80,
                "durable Heal did not synchronize target combat projection");
        }
    }
}
