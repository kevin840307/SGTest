using SGTest.Server.World;
using System.Text.Json;
using SGTest.Server.Identity;

namespace SGTest.Server.Npc;

public enum QuestNpcAccessStatus : byte
{
    Allowed=0,
    StaleGeneration=1,
    UnknownQuest=2,
    TooFar=3,
    PlayerDead=4
}

public sealed class NpcQuestCatalog
{
    public sealed record QuestPoint(
        int MapId,int NpcSpawnId,double NpcX,double NpcY,TrustedQuestRule Rule);

    private readonly Dictionary<(int MapId,int SpawnId),QuestPoint> _points;
    private readonly Dictionary<int,QuestPoint> _quests;
    public int Count=>_points.Count;
    public double MaximumInteractionDistance { get; }

    public NpcQuestCatalog(
        IEnumerable<QuestPoint> points,double maximumInteractionDistance=96)
    {
        ArgumentNullException.ThrowIfNull(points);
        if(!double.IsFinite(maximumInteractionDistance)||
           maximumInteractionDistance is <16 or >256)
            throw new ArgumentOutOfRangeException(nameof(maximumInteractionDistance));
        MaximumInteractionDistance=maximumInteractionDistance;
        _points=new();
        _quests=new();
        foreach(var point in points)
        {
            SqliteIdentityStore.ValidateQuestRule(point.Rule);
            if(point.MapId<=0||point.NpcSpawnId<=0||
               !double.IsFinite(point.NpcX)||!double.IsFinite(point.NpcY)||
               point.NpcX<0||point.NpcY<0||
               !_points.TryAdd((point.MapId,point.NpcSpawnId),point)||
               !_quests.TryAdd(point.Rule.QuestId,point))
                throw new ArgumentException(
                    "Malformed/duplicate quest NPC or quest id",nameof(points));
        }
        if(_points.Count>10000)
            throw new ArgumentException("Quest NPC index is unbounded",nameof(points));
    }

    public bool TryGet(int mapId,int spawnId,out QuestPoint point)=>
        _points.TryGetValue((mapId,spawnId),out point!);

    public bool TryGetQuest(int questId,out QuestPoint point)=>
        _quests.TryGetValue(questId,out point!);

    public QuestNpcAccessStatus Access(ActorSnapshot actor,int npcSpawnId)
    {
        if(!TryGet(actor.MapId,npcSpawnId,out var point))
            return QuestNpcAccessStatus.UnknownQuest;
        double dx=actor.X-point.NpcX,dy=actor.Y-point.NpcY;
        double d=MaximumInteractionDistance;
        return dx*dx+dy*dy<=d*d
            ? QuestNpcAccessStatus.Allowed:QuestNpcAccessStatus.TooFar;
    }

    public static NpcQuestCatalog LoadVerified(
        string manifestPath,IEnumerable<string> npcJsonPaths,
        double maximumInteractionDistance=96)
    {
        if(string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Quest manifest path required",nameof(manifestPath));
        var positions=NpcShopCatalog.LoadNpcPositions(npcJsonPaths);
        var file=new FileInfo(manifestPath);
        if(!file.Exists||file.Length is <40 or >4*1024*1024)
            throw new InvalidDataException("Quest manifest missing or too large");
        using var stream=file.OpenRead();
        using var document=JsonDocument.Parse(stream,new JsonDocumentOptions
        {
            MaxDepth=32,AllowTrailingCommas=false,
            CommentHandling=JsonCommentHandling.Disallow
        });
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||
           ReadString(root,"kind")!="verified_npc_quest_rules"||
           ReadInt(root,"version")!=1||
           !ReadTrue(root,"rules_verified")||
           !ReadTrue(root,"gameplay_ready"))
            throw new InvalidDataException(
                "Quest rules require explicit verification");
        string source=ReadString(root,"source_reference");
        if(string.IsNullOrWhiteSpace(source)||source.Length>384)
            throw new InvalidDataException("Quest source reference missing");
        if(!root.TryGetProperty("quests",out var rows)||
           rows.ValueKind!=JsonValueKind.Array||
           rows.GetArrayLength() is <1 or >10000)
            throw new InvalidDataException("Invalid quest list");

        var result=new List<QuestPoint>();
        var seenNpc=new HashSet<(int,int)>();
        var seenQuest=new HashSet<int>();
        foreach(var row in rows.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)
                throw new InvalidDataException("Quest row must be an object");
            int map=ReadPositive(row,"map_id");
            int spawn=ReadPositive(row,"npc_spawn_id");
            int questId=ReadBoundedPositive(row,"quest_id",100000000);
            int revision=ReadPositive(row,"rule_revision");
            int start=ReadNonNegative(row,"start_stage",1000000);
            string title=ReadRequiredString(row,"title",128);
            if(!seenNpc.Add((map,spawn))||!seenQuest.Add(questId)||
               !positions.TryGetValue((map,spawn),out var pos))
                throw new InvalidDataException(
                    "Quest references missing/duplicate source NPC or quest id");

            if(!row.TryGetProperty("stages",out var stagesElement)||
               stagesElement.ValueKind!=JsonValueKind.Array||
               stagesElement.GetArrayLength() is <1 or >64)
                throw new InvalidDataException("Invalid quest stage list");

            var stageBuilders=new Dictionary<int,
                (string Text,List<(int Id,string Label,int Next,QuestProgressState State)> Choices)>();
            foreach(var stageElement in stagesElement.EnumerateArray())
            {
                if(stageElement.ValueKind!=JsonValueKind.Object)
                    throw new InvalidDataException("Quest stage must be an object");
                int stageId=ReadNonNegative(stageElement,"stage_id",1000000);
                string text=ReadRequiredString(stageElement,"text",2048);
                if(!stageElement.TryGetProperty("choices",out var choicesElement)||
                   choicesElement.ValueKind!=JsonValueKind.Array||
                   choicesElement.GetArrayLength()>16)
                    throw new InvalidDataException("Invalid quest choice list");
                var choices=new List<(int,string,int,QuestProgressState)>();
                var seenChoice=new HashSet<int>();
                foreach(var choiceElement in choicesElement.EnumerateArray())
                {
                    if(choiceElement.ValueKind!=JsonValueKind.Object)
                        throw new InvalidDataException("Quest choice must be an object");
                    int id=ReadBoundedPositive(choiceElement,"choice_id",100000000);
                    string label=ReadRequiredString(choiceElement,"label",256);
                    int next=ReadNonNegative(choiceElement,"next_stage",1000000);
                    var state=ReadState(choiceElement);
                    if(!seenChoice.Add(id))
                        throw new InvalidDataException(
                            "Duplicate quest choice id in stage");
                    choices.Add((id,label,next,state));
                }
                if(!stageBuilders.TryAdd(stageId,(text,choices)))
                    throw new InvalidDataException("Duplicate quest stage id");
            }

            var stages=new Dictionary<int,TrustedQuestStageRule>();
            foreach(var pair in stageBuilders)
            {
                var choices=new Dictionary<int,TrustedQuestChoiceRule>();
                foreach(var raw in pair.Value.Choices)
                {
                    if(!stageBuilders.ContainsKey(raw.Next))
                        throw new InvalidDataException(
                            "Quest choice references missing next stage");
                    choices.Add(raw.Id,new(raw.Id,raw.Label,raw.Next,raw.State));
                }
                stages.Add(pair.Key,new(pair.Key,pair.Value.Text,choices));
            }

            string ruleSource=
                source+$"#map={map};npc={spawn};quest={questId};revision={revision}";
            var rule=new TrustedQuestRule(
                questId,revision,title,start,stages,ruleSource);
            SqliteIdentityStore.ValidateQuestRule(rule);
            ValidateReachability(rule);
            result.Add(new(map,spawn,pos.X,pos.Y,rule));
        }

        return new(result,maximumInteractionDistance);
    }

    private static void ValidateReachability(TrustedQuestRule rule)
    {
        var visited=new HashSet<int>();
        var pending=new Queue<int>();
        pending.Enqueue(rule.StartStage);
        while(pending.TryDequeue(out int stage))
        {
            if(!visited.Add(stage))continue;
            foreach(var choice in rule.Stages[stage].Choices.Values)
                pending.Enqueue(choice.NextStage);
        }
        if(visited.Count!=rule.Stages.Count)
            throw new InvalidDataException("Quest contains unreachable stages");
    }

    private static QuestProgressState ReadState(JsonElement choice)
    {
        string value=ReadString(choice,"next_state");
        return value switch
        {
            "active"=>QuestProgressState.Active,
            "completed"=>QuestProgressState.Completed,
            _=>throw new InvalidDataException(
                "Quest next_state must be active or completed")
        };
    }

    private static string ReadRequiredString(
        JsonElement element,string name,int maxLength)
    {
        string value=ReadString(element,name);
        if(string.IsNullOrWhiteSpace(value)||value.Length>maxLength)
            throw new InvalidDataException("Invalid quest "+name);
        return value;
    }

    private static int ReadPositive(JsonElement e,string name)
    {
        int value=ReadInt(e,name);
        if(value<=0)throw new InvalidDataException("Expected positive "+name);
        return value;
    }

    private static int ReadBoundedPositive(JsonElement e,string name,int max)
    {
        int value=ReadPositive(e,name);
        if(value>max)throw new InvalidDataException("Quest "+name+" exceeds bound");
        return value;
    }

    private static int ReadNonNegative(JsonElement e,string name,int max)
    {
        if(!e.TryGetProperty(name,out var v)||!v.TryGetInt32(out int value)||
           value<0||value>max)
            throw new InvalidDataException("Invalid quest "+name);
        return value;
    }

    private static string ReadString(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String
            ? v.GetString()??string.Empty:string.Empty;

    private static int ReadInt(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.TryGetInt32(out int n)?n:0;

    private static bool ReadTrue(JsonElement e,string name)=>
        e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.True;
}
