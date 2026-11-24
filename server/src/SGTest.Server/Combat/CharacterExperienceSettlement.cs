using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
using System.Numerics;
using System.Text.Json;
using System.Threading.Channels;

namespace SGTest.Server.Combat;

public enum CharacterExperienceRewardTrust : byte
{
    Verified=1,
    SyntheticTest=2
}

public sealed record CharacterExperienceRewardRule(
    int MapId,int EnemyId,int EnemyLevel,long BaseExperience,
    CharacterExperienceRewardTrust Trust,string SourceReference,
    int EnemyDodge=0);

/// <summary>
/// Loads only explicitly source-audited monster EXP/hit inputs. Old AddAttr
/// maps EN_Att column 7 to internal WORD property 12 (DR), which
/// GetEnemyDefendAttrib supplies to IsHit_PerHit, while column 8 maps to
/// internal WORD property 16 (RExp) consumed by CheckExpArrange. This manifest
/// is intentionally separate from visual population evidence.
/// </summary>
public static class CharacterExperienceRewardCatalog
{
    public static IReadOnlyList<CharacterExperienceRewardRule> LoadVerified(
        string path,IReadOnlyList<MonsterRuntime> runtimes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(runtimes);
        var file=new FileInfo(path);
        if(!file.Exists)
            throw new FileNotFoundException("Character EXP reward manifest missing",path);
        if(file.Length is <2 or >1_048_576)
            throw new InvalidDataException(
                "Character EXP reward manifest size out of bound");

        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth=16,
            AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           Text(root,"kind")!="verified_character_experience_reward_rules"||
           Number(root,"version")!=2||
           !Bool(root,"rules_verified")||!Bool(root,"gameplay_ready"))
            throw new InvalidDataException(
                "Character EXP reward manifest is not verified gameplay content");

        string source=Text(root,"source_reference").Trim();
        if(source.Length is <1 or >256)
            throw new InvalidDataException(
                "Character EXP reward source reference required");

        var known=runtimes.SelectMany(x=>x.SnapshotAll())
            .Select(x=>(x.MapId,x.EnemyId)).ToHashSet();
        var rows=Read(root,"rules",JsonValueKind.Array);
        if(rows.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException(
                "Character EXP reward rule count out of bounds");

        var seen=new HashSet<(int,int)>();
        var rules=new List<CharacterExperienceRewardRule>();
        foreach(var row in rows.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException(
                    "Invalid character EXP reward rule");
            int map=Number(row,"map_id");
            int enemy=Number(row,"enemy_id");
            int level=Number(row,"enemy_level");
            int dodge=Number(row,"enemy_dr");
            long rExp=LongNumber(row,"r_exp");
            if(!known.Contains((map,enemy))||
               level is <1 or >ushort.MaxValue||
               dodge is <0 or >ushort.MaxValue||
               rExp is <0 or >SqliteIdentityStore.CharacterMaximumExperience||
               !seen.Add((map,enemy)))
                throw new InvalidDataException(
                    "Unknown, duplicate or invalid character EXP reward rule");
            rules.Add(new(map,enemy,level,rExp,
                CharacterExperienceRewardTrust.Verified,source,dodge));
        }
        return rules;
    }

    private static JsonElement Read(
        JsonElement parent,string name,JsonValueKind kind)
    {
        if(!parent.TryGetProperty(name,out var value)||value.ValueKind!=kind)
            throw new InvalidDataException(
                "Character EXP reward property invalid: "+name);
        return value;
    }

    private static int Number(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.Number).TryGetInt32(out int value)
            ?value:throw new InvalidDataException(
                "Character EXP reward integer invalid: "+name);

    private static long LongNumber(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.Number).TryGetInt64(out long value)
            ?value:throw new InvalidDataException(
                "Character EXP reward integer invalid: "+name);

    private static string Text(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.String).GetString()??"";

    private static bool Bool(JsonElement parent,string name)
    {
        if(!parent.TryGetProperty(name,out var value)||
           value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(
                "Character EXP reward boolean invalid: "+name);
        return value.GetBoolean();
    }
}

/// <summary>
/// CONFIRMED Old SGOBattleSys::CheckExpArrange settlement. Damage is first
/// partitioned by contributor group, then party contributors share their
/// group's pool before the existing player-vs-enemy level penalty is applied.
/// HK exact parity remains an A/B item because Old and HK are not guaranteed
/// to be the same server build.
/// </summary>
public static class CharacterExperienceSettlementPolicy
{
    public static long SettleSolo(
        int playerLevel,int enemyLevel,long baseExperience)
    {
        ValidateBase(playerLevel,enemyLevel,baseExperience);

        int difference=playerLevel-enemyLevel;
        if(difference<=3)return baseExperience;
        if(difference>18)return 0;
        if(difference>15)return baseExperience/10;
        if(difference>12)return baseExperience/5;
        if(difference>9)return MultiplyDivideFloor(baseExperience,4,10);
        if(difference>6)return MultiplyDivideFloor(baseExperience,6,10);
        return MultiplyDivideFloor(baseExperience,8,10);
    }

    public static long ProportionalPool(
        long baseExperience,long groupDamage,long totalDamage)
    {
        if(baseExperience<0||
           baseExperience>SqliteIdentityStore.CharacterMaximumExperience||
           groupDamage<0||totalDamage<=0||groupDamage>totalDamage)
            throw new ArgumentOutOfRangeException();
        return MultiplyDivideFloor(baseExperience,groupDamage,totalDamage);
    }

    public static long SettlePartyMember(
        int playerLevel,int enemyLevel,long groupExperience,
        int participantCount,int maximumParticipantLevel)
    {
        ValidateBase(playerLevel,enemyLevel,groupExperience);
        if(participantCount<1||participantCount>1024)
            throw new ArgumentOutOfRangeException(nameof(participantCount));
        if(participantCount==1)
            return SettleSolo(playerLevel,enemyLevel,groupExperience);
        if(maximumParticipantLevel<playerLevel)
            throw new ArgumentOutOfRangeException(nameof(maximumParticipantLevel));

        int gap=maximumParticipantLevel-playerLevel;
        long shared;
        if(gap>=10)
            shared=0;
        else if(gap>0)
        {
            int multiplier=participantCount switch
            {
                2=>14,
                3=>18,
                4=>22,
                5=>26,
                6=>30,
                _=>10
            };
            BigInteger value=(BigInteger)groupExperience*multiplier;
            value/=10;
            value/=participantCount;
            value*=100-gap*gap;
            value/=100;
            shared=CheckedExperience(value);
        }
        else
            shared=groupExperience/participantCount;

        return SettleSolo(playerLevel,enemyLevel,shared);
    }

    private static void ValidateBase(
        int playerLevel,int enemyLevel,long experience)
    {
        if(playerLevel<=0||enemyLevel<=0||experience<0||
           experience>SqliteIdentityStore.CharacterMaximumExperience)
            throw new ArgumentOutOfRangeException();
    }

    private static long MultiplyDivideFloor(
        long value,long multiplier,long divisor)
    {
        if(value<0||multiplier<0||divisor<=0)
            throw new ArgumentOutOfRangeException();
        return CheckedExperience((BigInteger)value*multiplier/divisor);
    }

    private static long CheckedExperience(BigInteger value)
    {
        if(value<0||value>SqliteIdentityStore.CharacterMaximumExperience)
            throw new OverflowException("Character EXP settlement exceeded trusted bounds");
        return (long)value;
    }
}

public enum CharacterExperienceDeathPostStatus : byte
{
    Enqueued,
    NoRule,
    Backpressured
}

public sealed record CharacterExperienceDeathStatistics(
    long DeathsQueued,long GrantsApplied,long Replays,long ZeroAwards,
    long PetGrantsApplied,long PetReplays,long Overflows,int Pending);

public sealed record CharacterExperienceDeathParticipant(
    ConnectionBinding Binding,long AppliedDamage,long ActivePetUid=0);

public sealed record CharacterExperienceDeathGroup(
    long AppliedDamage,
    IReadOnlyList<CharacterExperienceDeathParticipant> Participants);

public sealed class CharacterExperienceSettlementException : Exception
{
    public CharacterExperienceSettlementException(
        string message,Exception? inner=null):base(message,inner){}
}

/// <summary>
/// Bounded off-World EXP settlement for authoritative monster deaths. The AOI
/// owner captures the authenticated character generation and active Pet identity
/// at the lethal event. SQLite owns exactly-once character EXP; after that
/// settles, the same event grants floor(character EXP / 2) to the captured Pet,
/// matching the legacy settlement order.
/// </summary>
public sealed class CharacterExperienceSettlement
{
    private readonly SqliteIdentityStore _identity;
    private readonly PetCoordinator? _pets;
    private readonly WorldResultRouter _router;
    private readonly CharacterProgressionRules? _progressionRules;
    private readonly long _generation;
    private sealed record DeathWork(
        MonsterSnapshot Death,long TotalDamage,
        CharacterExperienceDeathGroup[] Groups,
        CharacterExperienceRewardRule Rule);

    private readonly Dictionary<(int,int),CharacterExperienceRewardRule> _rules;
    private readonly Channel<DeathWork> _queue;
    private long _queued,_applied,_replays,_zeroAwards,_petApplied,_petReplays,_overflows;
    private int _pending,_started;
    private Exception? _fault;

    public CharacterExperienceSettlement(
        SqliteIdentityStore identity,WorldResultRouter router,long generation,
        IEnumerable<CharacterExperienceRewardRule> rules,PetCoordinator? pets=null,
        CharacterProgressionRules? progressionRules=null,int capacity=256)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
        _router=router??throw new ArgumentNullException(nameof(router));
        ArgumentNullException.ThrowIfNull(rules);
        _=SqliteIdentityStore.ComposeLootDeathRevision(generation,1);
        if(capacity is <1 or >65536)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _generation=generation;
        _pets=pets;
        _progressionRules=progressionRules;

        var unique=new Dictionary<(int,int),CharacterExperienceRewardRule>();
        foreach(var rule in rules)
        {
            if(rule.MapId is <1 or >65535||
               rule.EnemyId is <1 or >10_000_000||
               rule.EnemyLevel is <1 or >1_000_000||
               rule.BaseExperience is <0 or >SqliteIdentityStore.CharacterMaximumExperience||
               rule.Trust is not (CharacterExperienceRewardTrust.Verified or
                   CharacterExperienceRewardTrust.SyntheticTest)||
               string.IsNullOrWhiteSpace(rule.SourceReference)||
               rule.SourceReference.Length>256||
               !unique.TryAdd((rule.MapId,rule.EnemyId),rule))
                throw new ArgumentException(
                    "Invalid trusted character EXP reward rule",nameof(rules));
        }
        if(unique.Count==0)
            throw new ArgumentException(
                "Character EXP reward rules required",nameof(rules));
        _rules=unique;

        _queue=Channel.CreateBounded<DeathWork>(
            new BoundedChannelOptions(capacity)
        {
            SingleReader=true,
            SingleWriter=true,
            FullMode=BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations=false
        });
    }

    public CharacterExperienceDeathStatistics Statistics=>new(
        Interlocked.Read(ref _queued),Interlocked.Read(ref _applied),
        Interlocked.Read(ref _replays),Interlocked.Read(ref _zeroAwards),
        Interlocked.Read(ref _petApplied),Interlocked.Read(ref _petReplays),
        Interlocked.Read(ref _overflows),Volatile.Read(ref _pending));

    public CharacterExperienceDeathPostStatus TryPostSoloDeath(
        ConnectionBinding binding,MonsterSnapshot monster,long activePetUid=0)
    {
        long damage=Math.Max(1,monster.MaxHp);
        return TryPostDeath(monster,damage,
        [
            new CharacterExperienceDeathGroup(damage,
            [
                new CharacterExperienceDeathParticipant(
                    binding,damage,activePetUid)
            ])
        ]);
    }

    public CharacterExperienceDeathPostStatus TryPostDeath(
        MonsterSnapshot monster,long totalDamage,
        IReadOnlyList<CharacterExperienceDeathGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if(monster.EntityId<=0||monster.EnemyId<=0||monster.MapId<=0||
           monster.MaxHp<=0||monster.CurrentHp!=0||
           monster.State!=MonsterAiState.Dead||
           monster.EntityRevision<=0||totalDamage<=0||
           groups.Count is <1 or >1024)
            throw new ArgumentOutOfRangeException(nameof(monster));
        if(_fault is { } failure)
            throw new CharacterExperienceSettlementException(
                "Character EXP death worker failed",failure);
        if(!_rules.TryGetValue((monster.MapId,monster.EnemyId),out var rule))
            return CharacterExperienceDeathPostStatus.NoRule;

        var sealedGroups=new CharacterExperienceDeathGroup[groups.Count];
        var seenActors=new HashSet<long>();
        long representedDamage=0;
        for(int i=0;i<groups.Count;i++)
        {
            var group=groups[i]??throw new ArgumentException(
                "Character EXP death group cannot be null",nameof(groups));
            if(group.AppliedDamage<=0||
               group.Participants is null||
               group.Participants.Count is <1 or >1024)
                throw new ArgumentOutOfRangeException(nameof(groups));

            var participants=group.Participants.ToArray();
            long participantDamage=0;
            foreach(var participant in participants)
            {
                var binding=participant.Binding;
                if(binding.ActorId<=0||binding.ConnectionEpoch<=0||
                   binding.MapEpoch<=0||participant.AppliedDamage<=0||
                   participant.ActivePetUid<0||
                   !seenActors.Add(binding.ActorId))
                    throw new ArgumentOutOfRangeException(nameof(groups));
                participantDamage=checked(
                    participantDamage+participant.AppliedDamage);
            }
            if(participantDamage!=group.AppliedDamage)
                throw new ArgumentException(
                    "Character EXP group damage must equal participant damage",
                    nameof(groups));
            representedDamage=checked(representedDamage+group.AppliedDamage);
            sealedGroups[i]=new CharacterExperienceDeathGroup(
                group.AppliedDamage,participants);
        }
        if(representedDamage>totalDamage)
            throw new ArgumentOutOfRangeException(nameof(totalDamage));

        _=SqliteIdentityStore.ComposeLootDeathRevision(
            _generation,monster.EntityRevision);
        if(!_queue.Writer.TryWrite(
               new DeathWork(monster,totalDamage,sealedGroups,rule)))
        {
            Interlocked.Increment(ref _overflows);
            return CharacterExperienceDeathPostStatus.Backpressured;
        }
        Interlocked.Increment(ref _pending);
        Interlocked.Increment(ref _queued);
        return CharacterExperienceDeathPostStatus.Enqueued;
    }

    public void Complete()=>_queue.Writer.TryComplete();

    public async Task RunAsync()
    {
        if(Interlocked.Exchange(ref _started,1)!=0)
            throw new InvalidOperationException(
                "Only one character EXP death worker allowed");
        try
        {
            await foreach(var work in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    long deathId=SqliteIdentityStore.ComposeLootDeathRevision(
                        _generation,work.Death.EntityRevision);
                    foreach(var group in work.Groups)
                    {
                        long groupExperience=
                            CharacterExperienceSettlementPolicy.ProportionalPool(
                                work.Rule.BaseExperience,
                                group.AppliedDamage,work.TotalDamage);
                        var levels=new Dictionary<long,int>(
                            group.Participants.Count);
                        foreach(var participant in group.Participants)
                        {
                            var profile=await _identity.GetCharacterCombatProfileAsync(
                                participant.Binding.ActorId).ConfigureAwait(false);
                            if(profile is null)
                                throw new CharacterExperienceSettlementException(
                                    "Accepted lethal contributor has no trusted combat profile");
                            levels.Add(participant.Binding.ActorId,profile.Data.Level);
                        }

                        int maximumLevel=levels.Values.Max();
                        foreach(var participant in group.Participants)
                        {
                            long actorId=participant.Binding.ActorId;
                            long settled=
                                CharacterExperienceSettlementPolicy.SettlePartyMember(
                                    levels[actorId],work.Rule.EnemyLevel,
                                    groupExperience,group.Participants.Count,
                                    maximumLevel);
                            string grantKey=
                                $"monsterdeath:{deathId}:{work.Death.EntityId}:actor:{actorId}";
                            CharacterExperienceGrantStatus grantStatus;
                            if(_progressionRules is not null)
                            {
                                var outcome=
                                    await _identity.ApplyCharacterProgressionExperienceAsync(
                                        actorId,settled,grantKey,_progressionRules)
                                        .ConfigureAwait(false);
                                grantStatus=outcome.Status;
                                if(outcome.Profile is not null)
                                {
                                    var frame=CharacterProfileProgressionHandler.BuildState(
                                        outcome.Profile.Data,actorId,outcome.TotalExperience,
                                        _progressionRules);
                                    var delivery=_router.QueuePlayerProfileState(
                                        participant.Binding,frame);
                                    if(delivery==GatewaySendStatus.Backpressured)
                                        throw new CharacterExperienceSettlementException(
                                            "Committed character progression profile queue full");
                                    if(outcome.Leveled)
                                    {
                                        var vitals=_router.QueuePlayerCombatState(
                                            participant.Binding,
                                            outcome.Profile.Data.CurrentHp,
                                            outcome.Profile.Data.MaxHp,
                                            outcome.Profile.Data.CurrentMp,
                                            outcome.Profile.Data.MaxMp);
                                        if(vitals==GatewaySendStatus.Backpressured)
                                            throw new CharacterExperienceSettlementException(
                                                "Committed character level-up vitals queue full");
                                    }
                                }
                            }
                            else
                            {
                                var outcome=await _identity.ApplyCharacterExperienceAsync(
                                    actorId,settled,grantKey).ConfigureAwait(false);
                                grantStatus=outcome.Status;
                            }

                            if(grantStatus==CharacterExperienceGrantStatus.Applied)
                                Interlocked.Increment(ref _applied);
                            else if(grantStatus==
                                    CharacterExperienceGrantStatus.AlreadyProcessed)
                                Interlocked.Increment(ref _replays);
                            else
                                throw new CharacterExperienceSettlementException(
                                    $"Trusted character EXP death grant failed: {grantStatus}");

                            if(settled==0)
                                Interlocked.Increment(ref _zeroAwards);

                            long petExperience=
                                PetExperienceSettlementPolicy
                                    .FromSettledCharacterExperience(settled);
                            if(_pets is not null&&
                               participant.ActivePetUid>0&&petExperience>0)
                            {
                                var petOutcome=
                                    await _pets.GrantCommittedExperienceAsync(
                                        participant.Binding,
                                        participant.ActivePetUid,petExperience,
                                        "pet:"+grantKey,_router).ConfigureAwait(false);
                                if(petOutcome.Status==
                                   PetExperienceGrantStatus.Applied)
                                    Interlocked.Increment(ref _petApplied);
                                else if(petOutcome.Status==
                                        PetExperienceGrantStatus.AlreadyProcessed)
                                    Interlocked.Increment(ref _petReplays);
                                else
                                    throw new CharacterExperienceSettlementException(
                                        $"Trusted Pet EXP follow-up failed: {petOutcome.Status}");
                            }
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch(Exception failure)
        {
            _fault=failure;
            throw;
        }
    }
}
