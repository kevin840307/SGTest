using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Monster;
using System.Text.Json;
using System.Threading.Channels;

namespace SGTest.Server.Pet;

public enum PetExperienceRewardTrust : byte
{
    Verified=1,
    SyntheticTest=2
}

public sealed record PetExperienceRewardRule(
    int MapId,int EnemyId,long Experience,
    PetExperienceRewardTrust Trust,string SourceReference);

/// <summary>
/// Production Pet EXP is disabled unless an explicit source-audited reward manifest
/// names the exact map/enemy reward. The growth curve and the reward amount are
/// separate evidence: knowing level thresholds never authorizes inventing kill EXP.
/// </summary>
public static class PetExperienceRewardCatalog
{
    public static IReadOnlyList<PetExperienceRewardRule> LoadVerified(
        string path,IReadOnlyList<MonsterRuntime> runtimes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(runtimes);
        var file=new FileInfo(path);
        if(!file.Exists)
            throw new FileNotFoundException("Pet EXP reward manifest missing",path);
        if(file.Length is <2 or >1_048_576)
            throw new InvalidDataException("Pet EXP reward manifest size out of bound");

        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth=16,
            AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           Text(root,"kind")!="verified_pet_experience_reward_rules"||
           Number(root,"version")!=1||
           !Bool(root,"rules_verified")||!Bool(root,"gameplay_ready"))
            throw new InvalidDataException(
                "Pet EXP reward manifest is not verified gameplay content");

        string source=Text(root,"source_reference").Trim();
        if(source.Length is <1 or >256)
            throw new InvalidDataException("Pet EXP reward source reference required");

        var known=runtimes.SelectMany(x=>x.SnapshotAll())
            .Select(x=>(x.MapId,x.EnemyId)).ToHashSet();
        var rows=Read(root,"rules",JsonValueKind.Array);
        if(rows.GetArrayLength() is <1 or >4096)
            throw new InvalidDataException("Pet EXP reward rule count out of bounds");

        var seen=new HashSet<(int,int)>();
        var rules=new List<PetExperienceRewardRule>();
        foreach(var row in rows.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("Invalid Pet EXP reward rule");
            int map=Number(row,"map_id");
            int enemy=Number(row,"enemy_id");
            long experience=LongNumber(row,"experience");
            if(!known.Contains((map,enemy))||
               experience is <1 or >9_000_000_000_000_000L||
               !seen.Add((map,enemy)))
                throw new InvalidDataException(
                    "Unknown, duplicate or invalid Pet EXP reward rule");
            rules.Add(new(map,enemy,experience,
                PetExperienceRewardTrust.Verified,source));
        }
        return rules;
    }

    private static JsonElement Read(
        JsonElement parent,string name,JsonValueKind kind)
    {
        if(!parent.TryGetProperty(name,out var value)||value.ValueKind!=kind)
            throw new InvalidDataException(
                "Pet EXP reward property invalid: "+name);
        return value;
    }

    private static int Number(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.Number).TryGetInt32(out int value)
            ?value:throw new InvalidDataException(
                "Pet EXP reward integer invalid: "+name);

    private static long LongNumber(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.Number).TryGetInt64(out long value)
            ?value:throw new InvalidDataException(
                "Pet EXP reward integer invalid: "+name);

    private static string Text(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.String).GetString()??"";

    private static bool Bool(JsonElement parent,string name)=>
        Read(parent,name,JsonValueKind.True).GetBoolean();
}

public enum PetExperienceDeathPostStatus : byte
{
    Enqueued,
    NoRule,
    Backpressured
}

public sealed record PetExperienceDeathStatistics(
    long DeathsQueued,long GrantsApplied,long Replays,
    long Overflows,int Pending);

public sealed class PetExperienceDeathSettlementException : Exception
{
    public PetExperienceDeathSettlementException(
        string message,Exception? inner=null):base(message,inner){}
}

/// <summary>
/// Bounded non-World settlement for a Pet-authored lethal monster hit. The AOI
/// owner captures the already-trusted Pet generation and authoritative dead
/// MonsterSnapshot; this worker performs SQLite growth settlement off the World
/// tick. A per-host generation is part of the replay key so monster revisions
/// reused after restart cannot suppress a later legitimate reward.
/// </summary>
public sealed class PetExperienceDeathSettlement
{
    private readonly PetCoordinator _pets;
    private readonly WorldResultRouter _router;
    private readonly long _generation;
    private readonly Dictionary<(int,int),PetExperienceRewardRule> _rules;
    private readonly Channel<(PetWorldPresence Pet,MonsterSnapshot Death,
        PetExperienceRewardRule Rule)> _queue;
    private long _queued,_applied,_replays,_overflows;
    private int _pending,_started;
    private Exception? _fault;

    public PetExperienceDeathSettlement(
        PetCoordinator pets,WorldResultRouter router,long generation,
        IEnumerable<PetExperienceRewardRule> rules,int capacity=256)
    {
        _pets=pets??throw new ArgumentNullException(nameof(pets));
        _router=router??throw new ArgumentNullException(nameof(router));
        ArgumentNullException.ThrowIfNull(rules);
        _=SqliteIdentityStore.ComposeLootDeathRevision(generation,1);
        if(capacity is <1 or >65536)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _generation=generation;

        var unique=new Dictionary<(int,int),PetExperienceRewardRule>();
        foreach(var rule in rules)
        {
            if(rule.MapId is <1 or >65535||
               rule.EnemyId is <1 or >10_000_000||
               rule.Experience is <1 or >9_000_000_000_000_000L||
               rule.Trust is not (PetExperienceRewardTrust.Verified or
                   PetExperienceRewardTrust.SyntheticTest)||
               string.IsNullOrWhiteSpace(rule.SourceReference)||
               rule.SourceReference.Length>256||
               !unique.TryAdd((rule.MapId,rule.EnemyId),rule))
                throw new ArgumentException(
                    "Invalid trusted Pet EXP reward rule",nameof(rules));
        }
        if(unique.Count==0)
            throw new ArgumentException(
                "Pet EXP reward rules required",nameof(rules));
        _rules=unique;
        _queue=Channel.CreateBounded<(PetWorldPresence,MonsterSnapshot,
            PetExperienceRewardRule)>(new BoundedChannelOptions(capacity)
        {
            SingleReader=true,
            SingleWriter=true,
            FullMode=BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations=false
        });
    }

    public PetExperienceDeathStatistics Statistics=>new(
        Interlocked.Read(ref _queued),Interlocked.Read(ref _applied),
        Interlocked.Read(ref _replays),Interlocked.Read(ref _overflows),
        Volatile.Read(ref _pending));

    public PetExperienceDeathPostStatus TryPostDeath(
        PetWorldPresence pet,MonsterSnapshot monster)
    {
        if(pet.OwnerActorId<=0||pet.OwnerConnectionEpoch<=0||
           pet.OwnerMapEpoch<=0||pet.PetUid<=0||
           monster.EntityId<=0||monster.EnemyId<=0||monster.MapId<=0||
           monster.CurrentHp!=0||monster.State!=MonsterAiState.Dead||
           monster.EntityRevision<=0)
            throw new ArgumentOutOfRangeException(nameof(monster));
        if(_fault is { } failure)
            throw new PetExperienceDeathSettlementException(
                "Pet EXP death worker failed",failure);
        if(!_rules.TryGetValue((monster.MapId,monster.EnemyId),out var rule))
            return PetExperienceDeathPostStatus.NoRule;

        _=SqliteIdentityStore.ComposeLootDeathRevision(
            _generation,monster.EntityRevision);
        if(!_queue.Writer.TryWrite((pet,monster,rule)))
        {
            Interlocked.Increment(ref _overflows);
            return PetExperienceDeathPostStatus.Backpressured;
        }
        Interlocked.Increment(ref _pending);
        Interlocked.Increment(ref _queued);
        return PetExperienceDeathPostStatus.Enqueued;
    }

    public void Complete()=>_queue.Writer.TryComplete();

    public async Task RunAsync()
    {
        if(Interlocked.Exchange(ref _started,1)!=0)
            throw new InvalidOperationException(
                "Only one Pet EXP death worker allowed");
        try
        {
            await foreach(var (pet,death,rule) in _queue.Reader.ReadAllAsync())
            {
                long deathId=SqliteIdentityStore.ComposeLootDeathRevision(
                    _generation,death.EntityRevision);
                string grantKey=$"petdeath:{deathId}:{death.EntityId}";
                var binding=new ConnectionBinding(
                    pet.OwnerActorId,pet.OwnerConnectionEpoch,pet.OwnerMapEpoch);
                var outcome=await _pets.GrantCommittedExperienceAsync(
                    binding,pet.PetUid,rule.Experience,grantKey,_router)
                    .ConfigureAwait(false);
                if(outcome.Status==PetExperienceGrantStatus.Applied)
                    Interlocked.Increment(ref _applied);
                else if(outcome.Status==PetExperienceGrantStatus.AlreadyProcessed)
                    Interlocked.Increment(ref _replays);
                else
                    throw new PetExperienceDeathSettlementException(
                        $"Trusted Pet EXP death grant failed: {outcome.Status}");
                Interlocked.Decrement(ref _pending);
            }
        }
        catch(Exception failure)
        {
            _fault=failure;
            throw;
        }
    }
}
