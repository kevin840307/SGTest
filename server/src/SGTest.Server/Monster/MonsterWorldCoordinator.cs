using SGTest.Server.Skill;
using SGTest.Server.Pet;
using SGTest.Server.Pvp;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.World;

namespace SGTest.Server.Monster;

public sealed record MonsterStateBatch(
    long Tick,
    IReadOnlyList<MonsterSnapshot> Changed,
    IReadOnlyList<MonsterWorldEvent>? Events = null);

public sealed record MonsterWorldCoordinatorStatistics(
    long ProcessedWorldBatches,
    long RuntimeTicks,
    long ChangedSnapshots,
    int TrackedActors,
    int Maps,
    int Monsters);

/// <summary>
/// Exact Old SGOBattleSys::IsHit_PerHit gate (0x1000BA26..0x1000BA82).
/// Player GetPlayerAttackAttrib maps sAttacker+0x10/+0x18 to Level/Hit;
/// enemy GetEnemyDefendAttrib maps sTarget+0x16/+0x1C to enemy Level/DR.
/// CDEnemyDistSys LoadEnAttData/AddAttr proves EN_Att col[7] -> +0x22 ->
/// internal WORD property 12 (DR); RExp is instead col[8] -> property 16.
/// </summary>
public static class NormalAttackHitRules
{
    public static int HitThreshold(
        int attackerLevel,int attackerHit,int enemyLevel,int enemyAttribute12)
    {
        if(attackerLevel is <1 or >ushort.MaxValue||
           attackerHit is <0 or >ushort.MaxValue||
           enemyLevel is <1 or >ushort.MaxValue||
           enemyAttribute12 is <0 or >ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(attackerLevel));
        return Math.Max(30,checked(
            attackerHit-enemyAttribute12+2*(attackerLevel-enemyLevel)+60));
    }

    public static bool IsHit(
        int attackerLevel,int attackerHit,int enemyLevel,int enemyAttribute12,int roll)
    {
        if(roll is <0 or >99)
            throw new ArgumentOutOfRangeException(nameof(roll));
        return HitThreshold(attackerLevel,attackerHit,enemyLevel,enemyAttribute12)>=roll;
    }
}

/// <summary>
/// Old SGOBattleSys physical normal-hit reduction. The recovered battle.cfg used by
/// this source tree sets DamageDelta=10, MinHit=3 and MinHitDelta=4. CalHit applies
/// the first +/- delta around attacker AP, subtracts target DP, then applies the
/// 30-level falloff; CalHit_PerHit applies the second delta and the low-hit floor.
/// One normal attack is one hit, so there is no multi-hit divisor here.
/// </summary>
public static class NormalAttackDamageRules
{
    public const int DamageDeltaPercent=10;
    public const int MinHit=3;
    public const int MinHitDelta=4;
    public const int MaximumDamage=0xFFFD;

    public static int CalculatePhysicalHit(
        int attackerAttack,int attackerLevel,int enemyDefense,int enemyLevel,
        Func<int,int> nextRoll)
    {
        ArgumentNullException.ThrowIfNull(nextRoll);
        if(attackerAttack<0||attackerAttack>100_000_000||
           attackerLevel is <1 or >ushort.MaxValue||
           enemyDefense is <0 or >ushort.MaxValue||
           enemyLevel is <1 or >ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(attackerAttack));

        long damage=ApplyVariance(attackerAttack,Next(nextRoll,DamageDeltaPercent*2));
        damage=Math.Max(0,damage-enemyDefense);

        int levelGap=enemyLevel-attackerLevel;
        if(levelGap>=30)
            damage=0;
        else if(levelGap>0)
            damage=damage*(30-levelGap)/30;

        damage=ApplyVariance(damage,Next(nextRoll,DamageDeltaPercent*2));
        if(damage<MinHit)
            damage=Next(nextRoll,MinHitDelta)+1;

        return (int)Math.Min(MaximumDamage,damage);
    }

    private static long ApplyVariance(long value,int roll) =>
        checked(value+value*(DamageDeltaPercent-roll)/100);

    private static int Next(Func<int,int> nextRoll,int exclusiveMax)
    {
        int value=nextRoll(exclusiveMax);
        if(value<0||value>=exclusiveMax)
            throw new InvalidOperationException(
                $"Normal attack damage roll must be 0..{exclusiveMax-1}");
        return value;
    }
}

/// <summary>
/// Single-consumer bridge from authoritative World batches to map-local MonsterRuntime.
/// It keeps only player position/generation snapshots, performs no DB/socket work,
/// and coalesces multiple same-tick AI events for one monster to one final snapshot.
/// Player HP may be supplied by the separate combat-settlement owner; without one,
/// synthetic/unit callers retain the historical targetable=1 fallback. This coordinator
/// owns movement/AI event publication only and never performs SQLite settlement.
/// </summary>
public sealed class MonsterWorldCoordinator
{
    private const int MaxAreaSkillTargets=1024;
    private readonly SortedDictionary<int, MonsterRuntime> _runtimes;
    private sealed class AttackCursor
    {
        public long ConnectionEpoch;
        public long MapEpoch;
        public uint LastSequence;
        public long LastAppliedTick = -1;
    }

    private sealed class SkillCursor
    {
        public long ConnectionEpoch;
        public long MapEpoch;
        public uint LastSequence;
        public uint PendingSequence;
        public readonly Dictionary<int,long> LastAppliedTick = [];
    }

    public readonly record struct PreparedSkillUse(
        TrustedSkillUse Request,VerifiedSkillRule Rule,long PreparedTick);

    public readonly record struct SkillPrepareResult(
        PreparedSkillUse? Prepared,SkillUseOutcome? Rejected)
    {
        public bool Ready => Prepared.HasValue && !Rejected.HasValue;
    }

    private readonly IReadOnlyDictionary<int,VerifiedSkillRule> _skillRules;
    private readonly IReadOnlyDictionary<PetCombatRuleKey,VerifiedPetCombatRule> _petRules;
    private readonly IReadOnlyDictionary<PetSkillRuleKey,VerifiedPetSkillRule> _petSkillRules;
    private readonly IReadOnlyDictionary<(int MapId,int EnemyId),CharacterExperienceRewardRule> _normalAttackHitRules;
    private readonly Func<int> _normalAttackRoll;
    private readonly Func<int,int> _normalAttackDamageRoll;
    private readonly Dictionary<long, ActorSnapshot> _actors = [];
    private readonly Dictionary<long, AttackCursor> _attack = [];
    private readonly Dictionary<long, AttackCursor> _petAttack = [];
    private readonly Dictionary<long, SkillCursor> _petSkill = [];
    private readonly Dictionary<long, SkillCursor> _skill = [];
    private readonly HashSet<int> _seededMaps = [];
    private long _lastTick;
    private long _batches, _runtimeTicks, _changed;

    public MonsterWorldCoordinator(
        IEnumerable<MonsterRuntime> runtimes,
        IReadOnlyDictionary<int,VerifiedSkillRule>? skillRules = null,
        IReadOnlyDictionary<PetCombatRuleKey,VerifiedPetCombatRule>? petRules = null,
        IReadOnlyDictionary<PetSkillRuleKey,VerifiedPetSkillRule>? petSkillRules = null,
        IEnumerable<CharacterExperienceRewardRule>? normalAttackHitRules = null,
        Func<int>? normalAttackRoll = null,
        Func<int,int>? normalAttackDamageRoll = null)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        _skillRules=skillRules is null
            ? new Dictionary<int,VerifiedSkillRule>()
            : new Dictionary<int,VerifiedSkillRule>(skillRules);
        _petRules=petRules is null
            ? new Dictionary<PetCombatRuleKey,VerifiedPetCombatRule>()
            : new Dictionary<PetCombatRuleKey,VerifiedPetCombatRule>(petRules);
        _petSkillRules=petSkillRules is null
            ? new Dictionary<PetSkillRuleKey,VerifiedPetSkillRule>()
            : new Dictionary<PetSkillRuleKey,VerifiedPetSkillRule>(petSkillRules);
        if(_petRules.Count>PetCombatRuleCatalog.MaximumRules ||
           _petRules.Any(pair=>pair.Key!=new PetCombatRuleKey(
                    pair.Value.DefinitionId,pair.Value.EvolutionId,pair.Value.Level) ||
                pair.Value.FixedDamage is <1 or >100_000_000 ||
                !double.IsFinite(pair.Value.AttackRange) ||
                pair.Value.AttackRange is <=0 or >500 ||
                pair.Value.CooldownTicks is <0 or >3_600 ||
                string.IsNullOrWhiteSpace(pair.Value.EvidenceSource)))
            throw new ArgumentOutOfRangeException(nameof(petRules));
        if(_petSkillRules.Count>PetSkillRuleCatalog.MaximumRules ||
           _petSkillRules.Any(pair=>pair.Key!=new PetSkillRuleKey(
                    pair.Value.DefinitionId,pair.Value.EvolutionId,
                    pair.Value.Level,pair.Value.SkillId) ||
                pair.Value.FixedDamage is <1 or >100_000_000 ||
                !double.IsFinite(pair.Value.Range) ||
                pair.Value.Range is <=0 or >500 ||
                pair.Value.CooldownTicks is <0 or >3_600 ||
                string.IsNullOrWhiteSpace(pair.Value.EvidenceSource)))
            throw new ArgumentOutOfRangeException(nameof(petSkillRules));
        if(_skillRules.Count>64 ||
            _skillRules.Any(pair=>pair.Key!=pair.Value.SkillId ||
                pair.Key is <1 or >100_000_000 ||
                pair.Value.FixedDamage is <1 or >100_000_000 ||
                pair.Value.CooldownTicks is <0 or >3600 ||
                pair.Value.MpCost is <0 or >100_000_000 ||
                !ValidRuleGeometry(pair.Value)))
            throw new ArgumentOutOfRangeException(nameof(skillRules));
        _runtimes = new SortedDictionary<int, MonsterRuntime>();
        foreach (var runtime in runtimes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (!_runtimes.TryAdd(runtime.MapId, runtime))
                throw new ArgumentException(
                    $"Duplicate MonsterRuntime for map {runtime.MapId}", nameof(runtimes));
        }
        if (_runtimes.Count == 0)
            throw new ArgumentException("At least one MonsterRuntime is required", nameof(runtimes));

        var known=_runtimes.Values.SelectMany(runtime=>runtime.SnapshotAll())
            .Select(monster=>(monster.MapId,monster.EnemyId)).ToHashSet();
        var hitRules=new Dictionary<(int MapId,int EnemyId),CharacterExperienceRewardRule>();
        foreach(var rule in normalAttackHitRules ?? [])
        {
            if(rule.Trust is not (CharacterExperienceRewardTrust.Verified or
                    CharacterExperienceRewardTrust.SyntheticTest)||
               rule.MapId<=0||rule.EnemyId<=0||
               rule.EnemyLevel is <1 or >ushort.MaxValue||
               rule.EnemyDodge is <0 or >ushort.MaxValue||
               rule.BaseExperience is <0 or >ushort.MaxValue||
               string.IsNullOrWhiteSpace(rule.SourceReference)||
               !known.Contains((rule.MapId,rule.EnemyId))||
               !hitRules.TryAdd((rule.MapId,rule.EnemyId),rule))
                throw new ArgumentOutOfRangeException(nameof(normalAttackHitRules));
        }
        _normalAttackHitRules=hitRules;
        _normalAttackRoll=normalAttackRoll ?? (()=>Random.Shared.Next(100));
        _normalAttackDamageRoll=normalAttackDamageRoll ??
            (exclusiveMax=>Random.Shared.Next(exclusiveMax));
    }

    public long LastTick => _lastTick;

    public MonsterWorldCoordinatorStatistics Statistics =>
        new(Interlocked.Read(ref _batches), Interlocked.Read(ref _runtimeTicks),
            Interlocked.Read(ref _changed), _actors.Count, _runtimes.Count,
            _runtimes.Values.Sum(x => x.Count));

    public MonsterStateBatch Process(
        WorldChangedBatch batch,
        Func<ActorSnapshot, int>? currentHp = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Tick <= _lastTick)
            throw new InvalidOperationException(
                "Monster coordinator requires strictly ordered World batches");
        _lastTick = batch.Tick;

        if (batch.Lifecycle is not null)
        {
            foreach (var lifecycle in batch.Lifecycle)
            {
                if (lifecycle.Kind == ActorLifecycleKind.Joined)
                    Join(lifecycle.Actor);
                else if (lifecycle.Kind == ActorLifecycleKind.Left)
                    Leave(lifecycle.Actor);
                else
                    throw new InvalidOperationException("Unknown actor lifecycle");
            }
        }

        foreach (var actor in batch.Changed)
            Change(actor);

        var latest = new Dictionary<long, MonsterSnapshot>();
        var combatEvents = new List<MonsterWorldEvent>();
        foreach (var pair in _runtimes)
        {
            var runtime = pair.Value;

            // First observation publishes the current map baseline even if this
            // map has no player yet. AOI stores it spatially; no wire is sent
            // until an observer is actually in range.
            if (_seededMaps.Add(pair.Key))
                foreach (var monster in runtime.SnapshotAll())
                    latest[monster.EntityId] = monster;

            var targets = _actors.Values
                .Where(x => x.MapId == pair.Key)
                .OrderBy(x => x.ActorId)
                .Select(x => new MonsterCombatTarget(
                    x.ActorId, x.MapId, x.X, x.Y,
                    CurrentHp: Math.Max(0, currentHp?.Invoke(x) ?? 1)))
                .ToArray();
            // Keep death/respawn deadlines moving even when this map is empty.
            // Only AI/pathfinding is suspended until a player returns.
            var events = runtime.Tick(batch.Tick, targets,
                skipAi: targets.Length == 0);
            Interlocked.Increment(ref _runtimeTicks);
            foreach (var evt in events)
            {
                latest[evt.Monster.EntityId] = evt.Monster;
                combatEvents.Add(evt);
            }
        }

        var changed = latest.Values.OrderBy(x => x.EntityId).ToArray();
        Interlocked.Increment(ref _batches);
        Interlocked.Add(ref _changed, changed.Length);
        return new MonsterStateBatch(batch.Tick, changed, combatEvents.ToArray());
    }

    public bool TryGetActor(long actorId, out ActorSnapshot actor) =>
        _actors.TryGetValue(actorId, out actor);

    public IReadOnlyList<MonsterDamageContribution> DamageContributions(
        MonsterSnapshot death)
    {
        if(death.EntityId<=0||death.MapId<=0||death.EntityRevision<=0||
           death.CurrentHp!=0||death.State!=MonsterAiState.Dead||
           !_runtimes.TryGetValue(death.MapId,out var runtime))
            return Array.Empty<MonsterDamageContribution>();
        var current=runtime.Find(death.EntityId);
        if(current is null||
           current.Value.EntityRevision!=death.EntityRevision||
           current.Value.State!=MonsterAiState.Dead)
            return Array.Empty<MonsterDamageContribution>();
        return runtime.DamageContributions(death.EntityId);
    }

    public NormalAttackOutcome ResolveNormalAttack(TrustedNormalAttack request)
    {
        if (_lastTick <= 0 || !_actors.TryGetValue(request.ActorId, out var actor) ||
            !_attack.TryGetValue(request.ActorId, out var cursor) ||
            actor.ConnectionEpoch != request.ConnectionEpoch ||
            cursor.ConnectionEpoch != request.ConnectionEpoch)
            return Empty(request, NormalAttackStatus.OldMapEpoch);

        if (request.RequestMapEpoch != actor.MapEpoch ||
            cursor.MapEpoch != actor.MapEpoch)
            return Empty(request, NormalAttackStatus.OldMapEpoch);

        if (request.AttackSequence <= cursor.LastSequence)
            return Empty(request, NormalAttackStatus.ReplayedSequence);
        cursor.LastSequence = request.AttackSequence;

        if (request.Profile is not { } profile ||
            !_runtimes.TryGetValue(actor.MapId, out var runtime))
            return Empty(request, NormalAttackStatus.CombatUnavailable);

        if (cursor.LastAppliedTick >= 0 &&
            _lastTick - cursor.LastAppliedTick < profile.AttackCooldownTicks)
            return Empty(request, NormalAttackStatus.Cooldown);

        var status = runtime.ValidateNormalAttackTarget(
            request.TargetEntityId, actor.X, actor.Y, profile.AttackRange,
            out var target);
        if (status != NormalAttackStatus.Applied)
            return FromTarget(request, status, target);

        if(profile.Level is <1 or >ushort.MaxValue||
           profile.Hit is <0 or >ushort.MaxValue||
           !runtime.TryGetCombatStats(request.TargetEntityId,out var targetStats))
            return FromTarget(request,NormalAttackStatus.CombatUnavailable,target);

        int enemyLevel;
        int enemyDodge;
        if(targetStats.TargetCombatVerified)
        {
            enemyLevel=targetStats.Level;
            enemyDodge=targetStats.DodgeRate;
        }
        else if(_normalAttackHitRules.TryGetValue(
                    (actor.MapId,target.EnemyId),out var hitRule))
        {
            enemyLevel=hitRule.EnemyLevel;
            enemyDodge=hitRule.EnemyDodge;
        }
        else
        {
            return FromTarget(request,NormalAttackStatus.CombatUnavailable,target);
        }

        int roll=_normalAttackRoll();
        if(roll is <0 or >99)
            throw new InvalidOperationException("Normal attack hit roll must be 0..99");
        cursor.LastAppliedTick = _lastTick;
        if(!NormalAttackHitRules.IsHit(
               profile.Level,profile.Hit,enemyLevel,enemyDodge,roll))
            return FromTarget(request,NormalAttackStatus.Miss,target);

        int damage=targetStats.TargetCombatVerified
            ? NormalAttackDamageRules.CalculatePhysicalHit(
                profile.Attack,profile.Level,targetStats.DefensePower,targetStats.Level,
                _normalAttackDamageRoll)
            : Math.Max(1,profile.Attack);
        var applied = runtime.ApplyTrustedDamage(
            request.TargetEntityId, request.ActorId, damage, _lastTick);
        if (!applied.Applied)
            return FromTarget(request, NormalAttackStatus.TargetDead, target);

        var changed = applied.Monster;
        return new(NormalAttackStatus.Applied, _lastTick, request.TargetEntityId,
            applied.Damage, changed.CurrentHp, changed.MaxHp,
            changed.EntityRevision, changed);
    }

    public NormalAttackOutcome ResolvePetNormalAttack(
        TrustedPetAttack request,PetWorldPresence pet)
    {
        if(_lastTick<=0||!_actors.TryGetValue(request.ActorId,out var actor)||
           !_petAttack.TryGetValue(request.ActorId,out var cursor)||
           actor.ConnectionEpoch!=request.ConnectionEpoch||
           cursor.ConnectionEpoch!=request.ConnectionEpoch)
            return PetEmpty(request,NormalAttackStatus.OldMapEpoch);

        if(request.RequestMapEpoch!=actor.MapEpoch||cursor.MapEpoch!=actor.MapEpoch)
            return PetEmpty(request,NormalAttackStatus.OldMapEpoch);

        if(pet.OwnerActorId!=request.ActorId||
           pet.OwnerConnectionEpoch!=request.ConnectionEpoch||
           pet.OwnerMapEpoch!=request.RequestMapEpoch||pet.PetUid!=request.PetUid)
            return PetEmpty(request,NormalAttackStatus.CombatUnavailable);

        if(request.AttackSequence<=cursor.LastSequence)
            return PetEmpty(request,NormalAttackStatus.ReplayedSequence);
        cursor.LastSequence=request.AttackSequence;

        var key=new PetCombatRuleKey(pet.DefinitionId,pet.EvolutionId,pet.Level);
        if(!_petRules.TryGetValue(key,out var rule)||
           !_runtimes.TryGetValue(actor.MapId,out var runtime))
            return PetEmpty(request,NormalAttackStatus.CombatUnavailable);

        if(cursor.LastAppliedTick>=0&&
           _lastTick-cursor.LastAppliedTick<rule.CooldownTicks)
            return PetEmpty(request,NormalAttackStatus.Cooldown);

        var status=runtime.ValidateNormalAttackTarget(
            request.TargetEntityId,pet.X,pet.Y,rule.AttackRange,out var target);
        if(status!=NormalAttackStatus.Applied)
            return PetFromTarget(request,status,target);

        cursor.LastAppliedTick=_lastTick;
        var applied=runtime.ApplyTrustedDamage(
            request.TargetEntityId,request.ActorId,rule.FixedDamage,_lastTick);
        if(!applied.Applied)
            return PetFromTarget(request,NormalAttackStatus.TargetDead,target);

        var changed=applied.Monster;
        return new(NormalAttackStatus.Applied,_lastTick,request.TargetEntityId,
            applied.Damage,changed.CurrentHp,changed.MaxHp,
            changed.EntityRevision,changed);
    }

    public SkillUseOutcome ResolvePetSkill(
        TrustedPetSkill request,PetWorldPresence pet)
    {
        if(_lastTick<=0||!_actors.TryGetValue(request.ActorId,out var actor)||
           !_petSkill.TryGetValue(request.ActorId,out var cursor)||
           actor.ConnectionEpoch!=request.ConnectionEpoch||
           cursor.ConnectionEpoch!=request.ConnectionEpoch)
            return PetSkillEmpty(request,SkillUseStatus.OldMapEpoch);

        if(request.RequestMapEpoch!=actor.MapEpoch||cursor.MapEpoch!=actor.MapEpoch)
            return PetSkillEmpty(request,SkillUseStatus.OldMapEpoch);

        if(pet.OwnerActorId!=request.ActorId||
           pet.OwnerConnectionEpoch!=request.ConnectionEpoch||
           pet.OwnerMapEpoch!=request.RequestMapEpoch||pet.PetUid!=request.PetUid)
            return PetSkillEmpty(request,SkillUseStatus.CombatUnavailable);

        if(request.SkillSequence<=cursor.LastSequence)
            return PetSkillEmpty(request,SkillUseStatus.ReplayedSequence);
        cursor.LastSequence=request.SkillSequence;

        var key=new PetSkillRuleKey(
            pet.DefinitionId,pet.EvolutionId,pet.Level,request.SkillId);
        if(!_petSkillRules.TryGetValue(key,out var rule))
            return PetSkillEmpty(request,SkillUseStatus.NoRule);
        if(!_runtimes.TryGetValue(actor.MapId,out var runtime))
            return PetSkillEmpty(request,SkillUseStatus.CombatUnavailable);

        if(cursor.LastAppliedTick.TryGetValue(request.SkillId,out long lastApplied)&&
           _lastTick-lastApplied<rule.CooldownTicks)
            return PetSkillEmpty(request,SkillUseStatus.Cooldown);

        var targetStatus=runtime.ValidateNormalAttackTarget(
            request.TargetEntityId,pet.X,pet.Y,rule.Range,out var target);
        if(targetStatus!=NormalAttackStatus.Applied)
            return PetSkillFromTarget(request,MapSkillStatus(targetStatus),target);

        cursor.LastAppliedTick[request.SkillId]=_lastTick;
        var applied=runtime.ApplyTrustedDamage(
            request.TargetEntityId,request.ActorId,rule.FixedDamage,_lastTick);
        if(!applied.Applied)
            return PetSkillFromTarget(request,SkillUseStatus.TargetDead,target);

        var changed=applied.Monster;
        return new(SkillUseStatus.Applied,_lastTick,request.SkillId,
            request.TargetEntityId,applied.Damage,changed.CurrentHp,changed.MaxHp,
            changed.EntityRevision,changed);
    }

    public SkillUseOutcome ResolveSkill(TrustedSkillUse request)
    {
        var prepared=PrepareSkill(request);
        if(!prepared.Ready)
            return prepared.Rejected ??
                throw new InvalidOperationException("Skill prepare result missing rejection");
        return CommitPreparedSkill(prepared.Prepared!.Value);
    }

    public SkillPrepareResult PrepareSkill(TrustedSkillUse request)
    {
        if (_lastTick <= 0 || !_actors.TryGetValue(request.ActorId,out var actor) ||
            !_skill.TryGetValue(request.ActorId,out var cursor) ||
            actor.ConnectionEpoch!=request.ConnectionEpoch ||
            cursor.ConnectionEpoch!=request.ConnectionEpoch)
            return new(null,EmptySkill(request,SkillUseStatus.OldMapEpoch));

        if(request.RequestMapEpoch!=actor.MapEpoch ||
            cursor.MapEpoch!=actor.MapEpoch)
            return new(null,EmptySkill(request,SkillUseStatus.OldMapEpoch));

        if(request.SkillSequence<=cursor.LastSequence)
            return new(null,EmptySkill(request,SkillUseStatus.ReplayedSequence));
        cursor.LastSequence=request.SkillSequence;

        if(cursor.PendingSequence!=0)
            return new(null,EmptySkill(request,SkillUseStatus.CombatUnavailable));

        if(!_skillRules.TryGetValue(request.SkillId,out var rule))
            return new(null,EmptySkill(request,SkillUseStatus.NoRule));
        if(request.ExpectedTargetKind is { } expected&&rule.TargetKind!=expected)
            return new(null,EmptySkill(request,SkillUseStatus.NoRule));
        if(request.Profile is null ||
            !_runtimes.TryGetValue(actor.MapId,out var runtime))
            return new(null,EmptySkill(request,SkillUseStatus.CombatUnavailable));

        if(cursor.LastAppliedTick.TryGetValue(rule.SkillId,out long last) &&
            _lastTick-last<rule.CooldownTicks)
            return new(null,EmptySkill(request,SkillUseStatus.Cooldown));

        if(rule.TargetKind==SkillTargetKind.SingleMonster)
        {
            if(request.TargetEntityId<=0)
                return new(null,EmptySkill(request,SkillUseStatus.TargetMissing));
            var targetStatus=runtime.ValidateNormalAttackTarget(
                request.TargetEntityId,actor.X,actor.Y,rule.Range,out var target);
            if(targetStatus!=NormalAttackStatus.Applied)
                return new(null,FromSkillTarget(
                    request,MapSkillStatus(targetStatus),target));
        }
        else if(rule.TargetKind==SkillTargetKind.SinglePlayer)
        {
            var status=ValidatePlayerSkillTarget(
                actor,request.TargetEntityId,rule,runtime,out _);
            if(status!=SkillUseStatus.Applied)
                return new(null,EmptySkill(request,status));
        }
        else
        {
            if(request.TargetEntityId!=0)
                return new(null,EmptySkill(request,SkillUseStatus.TargetMissing));
            var selection=runtime.SelectAreaSkillTargets(
                actor.X,actor.Y,rule.AreaRadius,rule.RequireLineOfSight,
                MaxAreaSkillTargets);
            if(selection.Overloaded)
                return new(null,EmptySkill(
                    request,SkillUseStatus.TooManyTargets));
            if(selection.Targets.Count==0)
                return new(null,EmptySkill(
                    request,SkillUseStatus.TargetMissing));
        }

        cursor.PendingSequence=request.SkillSequence;
        return new(new PreparedSkillUse(request,rule,_lastTick),null);
    }

    public SkillUseOutcome CancelPreparedSkill(
        PreparedSkillUse prepared,SkillUseStatus status)
    {
        var request=prepared.Request;
        if(status is SkillUseStatus.Applied or SkillUseStatus.Miss)
            throw new ArgumentOutOfRangeException(nameof(status));
        if(!_skill.TryGetValue(request.ActorId,out var cursor) ||
            cursor.ConnectionEpoch!=request.ConnectionEpoch ||
            cursor.MapEpoch!=request.RequestMapEpoch ||
            cursor.PendingSequence!=request.SkillSequence)
            return EmptySkill(request,SkillUseStatus.OldMapEpoch);
        cursor.PendingSequence=0;
        return EmptySkill(request,status);
    }

    public SkillUseOutcome CommitPreparedSkill(PreparedSkillUse prepared)
    {
        var request=prepared.Request;
        if(_lastTick<=0 || !_actors.TryGetValue(request.ActorId,out var actor) ||
            !_skill.TryGetValue(request.ActorId,out var cursor) ||
            actor.ConnectionEpoch!=request.ConnectionEpoch ||
            actor.MapEpoch!=request.RequestMapEpoch ||
            cursor.ConnectionEpoch!=request.ConnectionEpoch ||
            cursor.MapEpoch!=request.RequestMapEpoch ||
            cursor.PendingSequence!=request.SkillSequence ||
            !_runtimes.TryGetValue(actor.MapId,out var runtime))
            return EmptySkill(request,SkillUseStatus.OldMapEpoch);

        if(!_skillRules.TryGetValue(request.SkillId,out var currentRule) ||
            currentRule!=prepared.Rule)
        {
            cursor.PendingSequence=0;
            return EmptySkill(request,SkillUseStatus.NoRule);
        }

        if(currentRule.TargetKind==SkillTargetKind.SinglePlayer)
        {
            var status=ValidatePlayerSkillTarget(
                actor,request.TargetEntityId,currentRule,runtime,out _);
            cursor.PendingSequence=0;
            if(status!=SkillUseStatus.Applied)
                return EmptySkill(request,status);
            cursor.LastAppliedTick[currentRule.SkillId]=_lastTick;
            // Player HP is owned by PlayerCombatSettlement, not MonsterRuntime.
            // This commit only finalizes the shared skill sequence/cooldown token.
            return new(SkillUseStatus.Applied,_lastTick,request.SkillId,
                request.TargetEntityId,0,0,0,0);
        }

        if(currentRule.TargetKind==SkillTargetKind.SingleMonster)
        {
            var targetStatus=runtime.ValidateNormalAttackTarget(
                request.TargetEntityId,actor.X,actor.Y,currentRule.Range,out var target);
            if(targetStatus!=NormalAttackStatus.Applied)
            {
                cursor.PendingSequence=0;
                return FromSkillTarget(request,MapSkillStatus(targetStatus),target);
            }

            cursor.PendingSequence=0;
            cursor.LastAppliedTick[currentRule.SkillId]=_lastTick;
            var applied=runtime.ApplyTrustedDamage(
                request.TargetEntityId,request.ActorId,currentRule.FixedDamage,_lastTick);
            if(!applied.Applied)
                return FromSkillTarget(request,SkillUseStatus.TargetDead,target);

            var changed=applied.Monster;
            return new(SkillUseStatus.Applied,_lastTick,request.SkillId,
                request.TargetEntityId,applied.Damage,changed.CurrentHp,
                changed.MaxHp,changed.EntityRevision,changed);
        }

        var area=runtime.SelectAreaSkillTargets(
            actor.X,actor.Y,currentRule.AreaRadius,currentRule.RequireLineOfSight,
            MaxAreaSkillTargets);
        if(area.Overloaded)
        {
            cursor.PendingSequence=0;
            return EmptySkill(request,SkillUseStatus.TooManyTargets);
        }
        if(area.Targets.Count==0)
        {
            cursor.PendingSequence=0;
            return EmptySkill(request,SkillUseStatus.TargetMissing);
        }

        cursor.PendingSequence=0;
        cursor.LastAppliedTick[currentRule.SkillId]=_lastTick;
        var hits=new List<SkillHitOutcome>(area.Targets.Count);
        foreach(var target in area.Targets)
        {
            var applied=runtime.ApplyTrustedDamage(
                target.EntityId,request.ActorId,currentRule.FixedDamage,_lastTick);
            if(applied.Applied)
                hits.Add(new(applied.Monster,applied.Damage));
        }
        if(hits.Count==0)
            return EmptySkill(request,SkillUseStatus.TargetMissing);
        // Area summary deliberately carries no single-target HP/damage fields.
        // Per-target actual damage/revision lives in Hits and later op46 events.
        return new(SkillUseStatus.Applied,_lastTick,request.SkillId,
            0,0,0,0,0,null,hits);
    }

    private SkillUseStatus ValidatePlayerSkillTarget(
        ActorSnapshot attacker,long targetActorId,VerifiedSkillRule rule,
        MonsterRuntime runtime,out ActorSnapshot target)
    {
        target=default;
        if(targetActorId<=0)return SkillUseStatus.TargetMissing;
        if(targetActorId==attacker.ActorId)return SkillUseStatus.SelfTarget;
        if(!PvpMapPolicy.AllowsPlayerPvp(attacker.MapId))
            return SkillUseStatus.NonPvpMap;
        if(attacker.CombatDead)return SkillUseStatus.CombatUnavailable;
        // MapEpoch belongs to each actor's own session/map lifecycle.
        // Players on the same map can legitimately have different epochs
        // after independent reconnects/transfers. The current target
        // snapshot and its own generation are validated by combat settlement.
        if(!_actors.TryGetValue(targetActorId,out target)||
           target.MapId!=attacker.MapId)
            return SkillUseStatus.TargetMissing;
        if(target.CombatDead)return SkillUseStatus.TargetDead;
        double dx=target.X-attacker.X,dy=target.Y-attacker.Y;
        if(dx*dx+dy*dy>rule.Range*rule.Range)
            return SkillUseStatus.OutOfRange;
        if(rule.RequireLineOfSight&&
           !runtime.IsDirectClear(attacker.X,attacker.Y,target.X,target.Y))
            return SkillUseStatus.Blocked;
        return SkillUseStatus.Applied;
    }

    private SkillUseOutcome EmptySkill(
        TrustedSkillUse request,SkillUseStatus status) =>
        new(status,Math.Max(0,_lastTick),request.SkillId,
            request.TargetEntityId,0,0,0,0);

    private SkillUseOutcome FromSkillTarget(
        TrustedSkillUse request,SkillUseStatus status,MonsterSnapshot target)
    {
        if(target.EntityId<=0)return EmptySkill(request,status);
        return new(status,Math.Max(0,_lastTick),request.SkillId,
            request.TargetEntityId,0,target.CurrentHp,target.MaxHp,
            target.EntityRevision,target);
    }

    private static bool ValidRuleGeometry(VerifiedSkillRule rule) =>
        rule.TargetKind switch
        {
            SkillTargetKind.SingleMonster or SkillTargetKind.SinglePlayer =>
                double.IsFinite(rule.Range) && rule.Range is >=1 and <=500 &&
                Math.Abs(rule.AreaRadius)<=1e-9,
            SkillTargetKind.SelfCenteredArea =>
                double.IsFinite(rule.Range) && Math.Abs(rule.Range)<=1e-9 &&
                double.IsFinite(rule.AreaRadius) &&
                rule.AreaRadius is >=1 and <=4096,
            _=>false
        };

    private static SkillUseStatus MapSkillStatus(NormalAttackStatus status) =>
        status switch
        {
            NormalAttackStatus.Applied=>SkillUseStatus.Applied,
            NormalAttackStatus.Miss=>SkillUseStatus.Miss,
            NormalAttackStatus.TargetMissing=>SkillUseStatus.TargetMissing,
            NormalAttackStatus.TargetDead=>SkillUseStatus.TargetDead,
            NormalAttackStatus.OutOfRange=>SkillUseStatus.OutOfRange,
            NormalAttackStatus.Blocked=>SkillUseStatus.Blocked,
            NormalAttackStatus.Cooldown=>SkillUseStatus.Cooldown,
            NormalAttackStatus.CombatUnavailable=>SkillUseStatus.CombatUnavailable,
            NormalAttackStatus.OldMapEpoch=>SkillUseStatus.OldMapEpoch,
            NormalAttackStatus.ReplayedSequence=>SkillUseStatus.ReplayedSequence,
            _=>throw new InvalidOperationException("Unknown target validation status")
        };

    private NormalAttackOutcome Empty(
        TrustedNormalAttack request, NormalAttackStatus status) =>
        new(status, Math.Max(0, _lastTick), request.TargetEntityId, 0, 0, 0, 0);

    private NormalAttackOutcome FromTarget(
        TrustedNormalAttack request, NormalAttackStatus status, MonsterSnapshot target)
    {
        if (target.EntityId <= 0)
            return Empty(request, status);
        return new(status, Math.Max(0, _lastTick), request.TargetEntityId, 0,
            target.CurrentHp, target.MaxHp, target.EntityRevision, target);
    }

    private NormalAttackOutcome PetEmpty(
        TrustedPetAttack request,NormalAttackStatus status) =>
        new(status,Math.Max(0,_lastTick),request.TargetEntityId,0,0,0,0);

    private NormalAttackOutcome PetFromTarget(
        TrustedPetAttack request,NormalAttackStatus status,MonsterSnapshot target)
    {
        if(target.EntityId<=0)
            return PetEmpty(request,status);
        return new(status,Math.Max(0,_lastTick),request.TargetEntityId,0,
            target.CurrentHp,target.MaxHp,target.EntityRevision,target);
    }

    private SkillUseOutcome PetSkillEmpty(
        TrustedPetSkill request,SkillUseStatus status) =>
        new(status,Math.Max(0,_lastTick),request.SkillId,
            request.TargetEntityId,0,0,0,0);

    private SkillUseOutcome PetSkillFromTarget(
        TrustedPetSkill request,SkillUseStatus status,MonsterSnapshot target)
    {
        if(target.EntityId<=0)
            return PetSkillEmpty(request,status);
        return new(status,Math.Max(0,_lastTick),request.SkillId,
            request.TargetEntityId,0,target.CurrentHp,target.MaxHp,
            target.EntityRevision,target);
    }

    private void Join(ActorSnapshot actor)
    {
        ValidateActor(actor);
        if (_actors.ContainsKey(actor.ActorId))
            throw new InvalidOperationException(
                "Monster coordinator received Join without prior Leave");
        _actors.Add(actor.ActorId, actor);
        _attack.Add(actor.ActorId, new AttackCursor
        {
            ConnectionEpoch = actor.ConnectionEpoch,
            MapEpoch = actor.MapEpoch
        });
        _petAttack.Add(actor.ActorId,new AttackCursor
        {
            ConnectionEpoch=actor.ConnectionEpoch,
            MapEpoch=actor.MapEpoch
        });
        _petSkill.Add(actor.ActorId,new SkillCursor
        {
            ConnectionEpoch=actor.ConnectionEpoch,
            MapEpoch=actor.MapEpoch
        });
        _skill.Add(actor.ActorId,new SkillCursor
        {
            ConnectionEpoch=actor.ConnectionEpoch,
            MapEpoch=actor.MapEpoch
        });
    }

    private void Leave(ActorSnapshot actor)
    {
        if (!_actors.TryGetValue(actor.ActorId, out var current) ||
            current.ConnectionEpoch != actor.ConnectionEpoch ||
            current.MapEpoch != actor.MapEpoch ||
            current.MapId != actor.MapId)
            throw new InvalidOperationException("Monster coordinator received stale Leave");
        _actors.Remove(actor.ActorId);
        _attack.Remove(actor.ActorId);
        _petAttack.Remove(actor.ActorId);
        _petSkill.Remove(actor.ActorId);
        _skill.Remove(actor.ActorId);
    }

    private void Change(ActorSnapshot actor)
    {
        ValidateActor(actor);
        if (!_actors.TryGetValue(actor.ActorId, out var current))
            throw new InvalidOperationException("Monster coordinator delta before Join");
        if (current.ConnectionEpoch != actor.ConnectionEpoch ||
            current.MapEpoch != actor.MapEpoch || current.MapId != actor.MapId ||
            actor.EntityRevision < current.EntityRevision)
            throw new InvalidOperationException("Monster coordinator actor generation regressed");
        if (actor.EntityRevision > current.EntityRevision)
            _actors[actor.ActorId] = actor;
    }

    private static void ValidateActor(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 ||
            actor.MapEpoch <= 0 || actor.MapId <= 0 || actor.EntityRevision <= 0 ||
            !double.IsFinite(actor.X) || !double.IsFinite(actor.Y) ||
            actor.X < 0 || actor.Y < 0)
            throw new InvalidOperationException(
                "Invalid authoritative target snapshot for Monster coordinator");
    }
}
