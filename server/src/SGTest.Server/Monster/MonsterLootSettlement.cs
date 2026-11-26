using SGTest.Server.Gateway;
using System.Text.Json;
using System.Threading.Channels;
using SGTest.Server.Identity;
using SGTest.Server.World;

namespace SGTest.Server.Monster;

public sealed record MonsterDropRule(int MapId,int EnemyId,int Slot,
    int ItemId,int Quantity,LootSourceTrust Trust,string SourceReference,
    int EnemyLevel=0,uint RollThreshold=0,int LevelDeltaDivisor=0,
    bool Guaranteed=true);

/// <summary>Only an explicit source-verified manifest enables Host loot.
/// v1 remains the legacy guaranteed-slot test contract. v2 is the production
/// Old-Server probability contract emitted by the audited converter.</summary>
public static class MonsterDropCatalog
{
    public static IReadOnlyList<MonsterDropRule> LoadVerified(
        string path,IReadOnlyList<MonsterRuntime> runtimes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(runtimes);
        var file=new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Loot manifest missing",path);
        if (file.Length is < 2 or > 1_048_576)
            throw new InvalidDataException("Loot manifest size out of bound");
        using var stream=file.OpenRead();
        using var doc=JsonDocument.Parse(stream,new JsonDocumentOptions{
            MaxDepth=16,AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow
        });
        var root=doc.RootElement;
        int version=Number(root,"version");
        if (root.ValueKind!=JsonValueKind.Object ||
            Text(root,"kind")!="verified_monster_drop_rules" ||
            version is not (1 or 2) ||
            !Bool(root,"rules_verified") || !Bool(root,"gameplay_ready"))
            throw new InvalidDataException("Loot manifest is not verified gameplay content");
        string source=Text(root,"source_reference");
        if (string.IsNullOrWhiteSpace(source) || source.Length>256)
            throw new InvalidDataException("Loot source reference required");
        int levelDelta=version==2?Number(root,"level_delta_divisor"):0;
        if(version==2 && levelDelta is < 1 or > 255)
            throw new InvalidDataException("Loot level delta divisor invalid");
        var known=runtimes.SelectMany(x=>x.SnapshotAll())
            .Select(x=>(x.MapId,x.EnemyId)).ToHashSet();
        var records=Read(root,"rules",JsonValueKind.Array);
        if (records.GetArrayLength() is < 1 or > 1024)
            throw new InvalidDataException("Loot rule count out of bounds");
        var seen=new HashSet<(int,int,int)>();
        var rules=new List<MonsterDropRule>();
        foreach(var row in records.EnumerateArray())
        {
            if (row.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("Invalid loot rule");
            int map=Number(row,"map_id"),enemy=Number(row,"enemy_id");
            int slot=Number(row,"slot"),item=Number(row,"item_definition_id");
            int quantity=Number(row,"quantity");
            if (!known.Contains((map,enemy)) || slot is < 0 or > 31 ||
                item is < 1 or > 100_000_000 || quantity is < 1 or > 1_000_000 ||
                !seen.Add((map,enemy,slot)))
                throw new InvalidDataException("Unknown or duplicate loot rule");
            if(version==1)
            {
                rules.Add(new(map,enemy,slot,item,quantity,
                    LootSourceTrust.Verified,source));
                continue;
            }
            int enemyLevel=Number(row,"enemy_level");
            uint threshold=Unsigned(row,"roll_threshold");
            if(enemyLevel is < 1 or > 255 || threshold==0)
                throw new InvalidDataException("Loot probability rule invalid");
            rules.Add(new(map,enemy,slot,item,quantity,
                LootSourceTrust.Verified,source,enemyLevel,threshold,levelDelta,
                Guaranteed:false));
        }
        return rules;
    }
    private static JsonElement Read(JsonElement obj,string key,JsonValueKind kind)
    {
        if (!obj.TryGetProperty(key,out var value)||value.ValueKind!=kind)
            throw new InvalidDataException("Loot property invalid: "+key);
        return value;
    }
    private static int Number(JsonElement obj,string key)=>
        Read(obj,key,JsonValueKind.Number).TryGetInt32(out int n)
            ?n:throw new InvalidDataException("Loot integer invalid: "+key);
    private static uint Unsigned(JsonElement obj,string key)=>
        Read(obj,key,JsonValueKind.Number).TryGetUInt32(out uint n)
            ?n:throw new InvalidDataException("Loot uint invalid: "+key);
    private static string Text(JsonElement obj,string key)=>
        Read(obj,key,JsonValueKind.String).GetString()??"";
    private static bool Bool(JsonElement obj,string key)=>
        Read(obj,key,JsonValueKind.True).GetBoolean();
}

public enum MonsterLootPostStatus:byte { Enqueued, NoRule, Backpressured }
public sealed record MonsterLootStatistics(long DeathsQueued,long DeathsCommitted,
    long DropsCommitted,long Overflows,int Pending);
public sealed class MonsterLootSettlementException:Exception
{
    public MonsterLootSettlementException(string message,Exception? inner=null)
        :base(message,inner){}
}

/// <summary>One bounded, non-World async settlement worker. Only the
/// authoritative combat reducer can enqueue monster deaths. v2 probability
/// rolls reproduce Old CheckDropItem's threshold and level-gap semantics.
/// A queue overflow must stop Host, not drop a committed death.</summary>
public sealed class MonsterLootSettlement
{
    private readonly SqliteIdentityStore _store;
    private readonly long _generation;
    private readonly Dictionary<(int,int),MonsterDropRule[]> _rules;
    private readonly Channel<(MonsterSnapshot Death,MonsterDropRule[] Rules,int AttackerLevel)> _queue;
    private readonly Channel<IReadOnlyList<GroundDropRecord>> _publishedDrops;
    private readonly Func<uint> _nextRoll;
    private long _queued,_committed,_drops,_overflow;
    private int _pending,_started;
    private Exception? _fault;

    public MonsterLootSettlement(SqliteIdentityStore store,long generation,
        IEnumerable<MonsterDropRule> rules,int capacity=256,
        Func<uint>? nextRoll=null)
    {
        _store=store??throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(rules);
        _=SqliteIdentityStore.ComposeLootDeathRevision(generation,1);
        if(capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
        _generation=generation;
        _nextRoll=nextRoll??NextOldDropRoll;
        var groups=new Dictionary<(int,int),List<MonsterDropRule>>();
        var unique=new HashSet<(int,int,int)>();
        foreach(var rule in rules)
        {
            bool probabilityValid=rule.Guaranteed
                ? rule.EnemyLevel==0&&rule.RollThreshold==0&&rule.LevelDeltaDivisor==0
                : rule.EnemyLevel is >=1 and <=255&&rule.RollThreshold>0&&
                  rule.LevelDeltaDivisor is >=1 and <=255;
            if(rule.MapId is < 1 or > 65535 || rule.EnemyId is < 1 or > 10_000_000 ||
                rule.Slot is < 0 or > 31 || rule.ItemId is < 1 or > 100_000_000 ||
                rule.Quantity is < 1 or > 1_000_000 || !probabilityValid ||
                rule.Trust is not (LootSourceTrust.Verified or LootSourceTrust.SyntheticTest) ||
                string.IsNullOrWhiteSpace(rule.SourceReference) ||
                rule.SourceReference.Length>256 ||
                !unique.Add((rule.MapId,rule.EnemyId,rule.Slot)))
                throw new ArgumentException("Invalid trusted drop rule",nameof(rules));
            if(!groups.TryGetValue((rule.MapId,rule.EnemyId),out var list))
                groups.Add((rule.MapId,rule.EnemyId),list=[]);
            list.Add(rule);
        }
        if(unique.Count==0) throw new ArgumentException("Loot rules required",nameof(rules));
        _rules=groups.ToDictionary(x=>x.Key,x=>x.Value.OrderBy(v=>v.Slot).ToArray());
        _queue=Channel.CreateBounded<(MonsterSnapshot,MonsterDropRule[],int)>(
            new BoundedChannelOptions(capacity){
                SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
        _publishedDrops=Channel.CreateBounded<IReadOnlyList<GroundDropRecord>>(
            new BoundedChannelOptions(capacity){
                SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations=false
            });
    }

    public MonsterLootStatistics Statistics=>new(Interlocked.Read(ref _queued),
        Interlocked.Read(ref _committed),Interlocked.Read(ref _drops),
        Interlocked.Read(ref _overflow),Volatile.Read(ref _pending));

    public static uint AdjustThreshold(
        uint threshold,int attackerLevel,int enemyLevel,int levelDeltaDivisor)
    {
        if(threshold==0||attackerLevel<1||enemyLevel<1||
           levelDeltaDivisor<1)
            return 0;
        int delta=attackerLevel-enemyLevel;
        int quotient=delta/levelDeltaDivisor;
        if(quotient<0) quotient=0;
        return threshold/(uint)(quotient+1);
    }

    private static uint NextOldDropRoll()
    {
        uint low=(uint)(Random.Shared.Next(0,32768)+Random.Shared.Next(0,32768));
        uint high=(uint)(Random.Shared.Next(0,32768)+Random.Shared.Next(0,32768));
        return unchecked(low+(high<<16));
    }

    public MonsterLootPostStatus TryPostDeath(MonsterSnapshot monster,int attackerLevel=0)
    {
        if(monster.EntityId<=0||monster.EnemyId<=0||monster.MapId<=0||
           monster.CurrentHp!=0||monster.State!=MonsterAiState.Dead||
           monster.EntityRevision<=0||!double.IsFinite(monster.X)||
           !double.IsFinite(monster.Y)||monster.X<0||monster.Y<0||
           attackerLevel is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(monster));
        if(_fault is { } failure)
            throw new MonsterLootSettlementException("Loot worker failed",failure);
        if(!_rules.TryGetValue((monster.MapId,monster.EnemyId),out var rules))
            return MonsterLootPostStatus.NoRule;
        _=SqliteIdentityStore.ComposeLootDeathRevision(_generation,monster.EntityRevision);
        if(!_queue.Writer.TryWrite((monster,rules,attackerLevel)))
        {
            Interlocked.Increment(ref _overflow);
            return MonsterLootPostStatus.Backpressured;
        }
        Interlocked.Increment(ref _pending);
        Interlocked.Increment(ref _queued);
        return MonsterLootPostStatus.Enqueued;
    }

    public ChannelReader<IReadOnlyList<GroundDropRecord>> CommittedDrops=>_publishedDrops.Reader;

    public void Complete()=>_queue.Writer.TryComplete();

    public async Task RunAsync()
    {
        if(Interlocked.Exchange(ref _started,1)!=0)
            throw new InvalidOperationException("Only one loot worker allowed");
        try
        {
            await foreach(var (monster,rules,attackerLevel) in _queue.Reader.ReadAllAsync())
            {
                var selected=rules.Where(rule=>
                    rule.Guaranteed ||
                    (attackerLevel>0 &&
                     _nextRoll()<AdjustThreshold(
                         rule.RollThreshold,attackerLevel,rule.EnemyLevel,
                         rule.LevelDeltaDivisor))).ToArray();

                if(selected.Length==0)
                {
                    Interlocked.Increment(ref _committed);
                    Interlocked.Decrement(ref _pending);
                    continue;
                }

                long deathId=SqliteIdentityStore.ComposeLootDeathRevision(
                    _generation,monster.EntityRevision);
                var batch=selected.Select(rule=>new TrustedDropSpawn(
                    monster.MapId,monster.X,monster.Y,monster.EntityId,
                    deathId,rule.Slot,rule.ItemId,rule.Quantity,
                    rule.Trust,rule.SourceReference)).ToArray();
                var settled=await _store.CreateTrustedGroundDropsAsync(batch);
                if(!_publishedDrops.Writer.TryWrite(settled))
                    throw new MonsterLootSettlementException(
                        "Ground-drop AOI committed notification queue overflow");
                Interlocked.Add(ref _drops,settled.Count);
                Interlocked.Increment(ref _committed);
                Interlocked.Decrement(ref _pending);
            }
        }
        catch(Exception failure){_fault=failure;throw;}
        finally{_publishedDrops.Writer.TryComplete(_fault);}
    }
}
