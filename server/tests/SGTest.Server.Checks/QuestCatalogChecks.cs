using SGTest.Server.Npc;
using System.Text.Json;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class QuestCatalogChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified NPC quest catalog binds audited NPC and bounded stage graph",
            () => Catalog(root));
    }

    private static void Catalog(string root)
    {
        string npc=Path.Combine(root,"quest_npcs_76.json");
        File.WriteAllText(npc,JsonSerializer.Serialize(new
        {
            schema="sgtest.npcs/v1",map_id=76,coordinate_space="world_pixels",
            npcs=new[]{new{spawn_id=77,p=new[]{8,8},
                interaction=new{mode="script_required"}}}
        }));

        string valid=WriteManifest(root,true,77,false,false);
        var catalog=NpcQuestCatalog.LoadVerified(valid,[npc]);
        Checks.Require(catalog.Count==1&&
            catalog.TryGet(76,77,out var point)&&
            point.NpcX==8&&point.NpcY==8&&
            point.Rule.QuestId==91001&&point.Rule.RuleRevision==1&&
            point.Rule.Stages[0].Choices[1].NextStage==1,
            "verified quest did not join audited NPC/stage graph");

        var near=new ActorSnapshot(1,76,1,1,0,0,8,8,1,false);
        var far=near with{X=500,Y=500};
        Checks.Require(
            catalog.Access(near,77)==QuestNpcAccessStatus.Allowed&&
            catalog.Access(far,77)==QuestNpcAccessStatus.TooFar&&
            catalog.Access(near,78)==QuestNpcAccessStatus.UnknownQuest,
            "quest NPC proximity/identity policy mismatch");

        bool unverified=false,unknownNpc=false,unreachable=false,
            nonterminalCompletion=false;
        try{NpcQuestCatalog.LoadVerified(
            WriteManifest(root,false,77,false,false),[npc]);}
        catch(InvalidDataException){unverified=true;}
        try{NpcQuestCatalog.LoadVerified(
            WriteManifest(root,true,78,false,false),[npc]);}
        catch(InvalidDataException){unknownNpc=true;}
        try{NpcQuestCatalog.LoadVerified(
            WriteManifest(root,true,77,true,false),[npc]);}
        catch(InvalidDataException){unreachable=true;}
        try{NpcQuestCatalog.LoadVerified(
            WriteManifest(root,true,77,false,true),[npc]);}
        catch(ArgumentException){nonterminalCompletion=true;}
        Checks.Require(unverified&&unknownNpc&&unreachable&&nonterminalCompletion,
            "quest catalog admitted unverified/missing/unreachable/nonterminal rules");
    }

    private static string WriteManifest(
        string root,bool verified,int spawn,bool unreachable,bool nonterminalCompletion)
    {
        string path=Path.Combine(root,
            $"quest_rules_{verified}_{spawn}_{unreachable}_{nonterminalCompletion}.json");
        var stages=new List<object>
        {
            new
            {
                stage_id=0,text="開始",
                choices=new[]{new{choice_id=1,label="接受",next_stage=1,
                    next_state="active"}}
            },
            new
            {
                stage_id=1,text="進行中",
                choices=new[]{new{choice_id=2,label="完成",next_stage=2,
                    next_state="completed"}}
            },
            new
            {
                stage_id=2,text="完成",
                choices=nonterminalCompletion
                    ? new[]{new{choice_id=3,label="不應存在",next_stage=2,
                        next_state="active"}}
                    : Array.Empty<object>()
            }
        };
        if(unreachable)
            stages.Add(new{stage_id=9,text="不可達",choices=Array.Empty<object>()});
        File.WriteAllText(path,JsonSerializer.Serialize(new
        {
            kind="verified_npc_quest_rules",version=1,
            rules_verified=verified,gameplay_ready=true,
            source_reference="synthetic:quest-catalog-check",
            quests=new[]{new
            {
                map_id=76,npc_spawn_id=spawn,quest_id=91001,
                rule_revision=1,title="Verified Quest",start_stage=0,stages
            }}
        }));
        return path;
    }
}
