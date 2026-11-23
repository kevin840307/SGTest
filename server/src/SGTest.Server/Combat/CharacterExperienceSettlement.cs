using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
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
    CharacterExperienceRewardTrust Trust,string SourceReference);

/// <summary>
/// Loads only explicitly source-audited monster EXP inputs. Old
/// CDEnemyWORDStr index 12 is RExp and SGOBattleSys::CheckExpArrange consumes
/// that value before applying player/party/level settlement. This manifest is
/// intentionally separate from visual population evidence.
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
           Number(root,"version")!=1||
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
            long rExp=LongNumber(row,"r_exp");
            if(!known.Contains((map,enemy))||
               level is <1 or >1_000_000||
               rExp is <0 or >SqliteIdentityStore.CharacterMaximumExperience||
               !seen.Add((map,enemy)))
                throw new InvalidDataException(
                    "Unknown, duplicate or invalid character EXP reward rule");
            rules.Add(new(map,enemy,level,rExp,
                CharacterExperienceRewardTrust.Verified,source));
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
/// CONFIRMED solo branch of Old SGOBattleSys::CheckExpArrange. Party-specific
/// distribution is deliberately excluded until its full source behavior is
/// verified and tested.
/// </summary>
public static class CharacterExperienceSettlementPolicy
{
    public static long SettleSolo(
        int playerLevel,int enemyLevel,long baseExperience)
    {
        if(playerLevel<=0||enemyLevel<=0||baseExperience<0||
           baseExperience>SqliteIdentityStore.CharacterMaximumExperience)
            throw new ArgumentOutOfRangeException();

        int difference=playerLevel-enemyLevel;
        if(difference<=3)return baseExperience;
        if(difference>18)return 0;
        if(difference>15)return baseExperience/10;
        if(difference>12)return baseExperience/5;
        if(difference>9)return checked(baseExperience*4)/10;
        if(difference>6)return checked(baseExperience*6)/10;
        return checked(baseExperience*8)/10;
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
    private readonly long _generation;
    private readonly Dictionary<(int,int),CharacterExperienceRewardRule> _rules;
    private readonly Channel<(ConnectionBinding Binding,MonsterSnapshot Death,
        long ActivePetUid,CharacterExperienceRewardRule Rule)> _queue;
    private long _queued,_applied,_replays,_zeroAwards,_petApplied,_petReplays,_overflows;
    private int _pending,_started;
    private Exception? _fault;

    public CharacterExperienceSettlement(
        SqliteIdentityStore identity,WorldResultRouter router,long generation,
        IEnumerable<CharacterExperienceRewardRule> rules,PetCoordinator? pets=null,
        int capacity=256)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
        _router=router??throw new ArgumentNullException(nameof(router));
        ArgumentNullException.ThrowIfNull(rules);
        _=SqliteIdentityStore.ComposeLootDeathRevision(generation,1);
        if(capacity is <1 or >65536)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _generation=generation;
        _pets=pets;

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

        _queue=Channel.CreateBounded<(ConnectionBinding,MonsterSnapshot,long,
            CharacterExperienceRewardRule)>(new BoundedChannelOptions(capacity)
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
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0||
           activePetUid<0||
           monster.EntityId<=0||monster.EnemyId<=0||monster.MapId<=0||
           monster.CurrentHp!=0||monster.State!=MonsterAiState.Dead||
           monster.EntityRevision<=0||monster.MapId<=0)
            throw new ArgumentOutOfRangeException(nameof(monster));
        if(_fault is { } failure)
            throw new CharacterExperienceSettlementException(
                "Character EXP death worker failed",failure);
        if(!_rules.TryGetValue((monster.MapId,monster.EnemyId),out var rule))
            return CharacterExperienceDeathPostStatus.NoRule;

        _=SqliteIdentityStore.ComposeLootDeathRevision(
            _generation,monster.EntityRevision);
        if(!_queue.Writer.TryWrite((binding,monster,activePetUid,rule)))
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
            await foreach(var (binding,death,activePetUid,rule) in
                _queue.Reader.ReadAllAsync())
            {
                try
                {
                    var profile=await _identity.GetCharacterCombatProfileAsync(
                        binding.ActorId).ConfigureAwait(false);
                    if(profile is null)
                        throw new CharacterExperienceSettlementException(
                            "Accepted lethal attack has no trusted combat profile");

                    long settled=CharacterExperienceSettlementPolicy.SettleSolo(
                        profile.Data.Level,rule.EnemyLevel,rule.BaseExperience);
                    long deathId=SqliteIdentityStore.ComposeLootDeathRevision(
                        _generation,death.EntityRevision);
                    string grantKey=$"monsterdeath:{deathId}:{death.EntityId}";
                    var outcome=await _identity.ApplyCharacterExperienceAsync(
                        binding.ActorId,settled,grantKey).ConfigureAwait(false);

                    if(outcome.Status==CharacterExperienceGrantStatus.Applied)
                        Interlocked.Increment(ref _applied);
                    else if(outcome.Status==CharacterExperienceGrantStatus.AlreadyProcessed)
                        Interlocked.Increment(ref _replays);
                    else
                        throw new CharacterExperienceSettlementException(
                            $"Trusted character EXP death grant failed: {outcome.Status}");

                    if(settled==0)
                        Interlocked.Increment(ref _zeroAwards);

                    long petExperience=
                        PetExperienceSettlementPolicy.FromSettledCharacterExperience(
                            settled);
                    if(_pets is not null&&activePetUid>0&&petExperience>0)
                    {
                        var petOutcome=await _pets.GrantCommittedExperienceAsync(
                            binding,activePetUid,petExperience,
                            "pet:"+grantKey,_router).ConfigureAwait(false);
                        if(petOutcome.Status==PetExperienceGrantStatus.Applied)
                            Interlocked.Increment(ref _petApplied);
                        else if(petOutcome.Status==
                                PetExperienceGrantStatus.AlreadyProcessed)
                            Interlocked.Increment(ref _petReplays);
                        else
                            throw new CharacterExperienceSettlementException(
                                $"Trusted Pet EXP follow-up failed: {petOutcome.Status}");
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
