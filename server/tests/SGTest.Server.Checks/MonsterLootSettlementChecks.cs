using SGTest.Server.Monster;
using SGTest.Server.Map;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class MonsterLootSettlementChecks
{
    public static void Verify(string root)
    {
        Checks.Run("verified loot manifest rejects absent evidence, unknown enemy and duplicate slot",
            () => CatalogGate(root));
        Checks.Run("bounded loot worker accepts only matched deaths and never drops overflow silently",
            () => BoundedWorker(root).GetAwaiter().GetResult());
    }

    private static MonsterRuntime FixtureRuntime(string root,int mapId)
    {
        string folder=Checks.Fixture(root,mapId,8,8,(_,_)=>0);
        string path=Path.Combine(folder,"monster-loot-population.json");
        File.WriteAllText(path,JsonSerializer.Serialize(new {
            kind="verified_monster_runtime",version=4,map_id=mapId,
            stats_verified=true,gameplay_ready=true,
            definitions=new[]{new{
                id=1,name="Synthetic",sprite="EN001A",
                atlas="Enemy/EN001A/atlas.json",
                stats=new{
                    level=1,max_hp=10,attack_damage=1,move_step=1.0,
                    aggro_radius=10.0,lose_target_radius=20.0,
                    leash_radius=20.0,attack_range=2.0,
                    attack_cooldown_ticks=1,respawn_ticks=5,
                    wander_radius=0.0,wander_interval_ticks=5
                }
            }},
            spawns=new[]{new {source_index=0,source_flags=0,definition=1,
                p=new[]{40,24}}}
        }));
        var grid=BlockingGrid.Load(folder,mapId);
        return new MonsterRuntime(grid,
            MonsterPopulationCatalog.LoadVerified(path,mapId,grid));
    }

    private static void CatalogGate(string root)
    {
        const int mapId=215;
        var runtime=FixtureRuntime(root,mapId);
        string path=Path.Combine(root,"loot-catalog-"+Guid.NewGuid().ToString("N")+".json");
        string Document(bool verified=true,int enemy=1,int slot=0,
            bool duplicate=false)
        {
            var rule=new {
                map_id=mapId,enemy_id=enemy,slot,
                item_definition_id=2701,quantity=2
            };
            return JsonSerializer.Serialize(new {
                kind="verified_monster_drop_rules",version=1,
                rules_verified=verified,gameplay_ready=true,
                source_reference="ci:explicit-provenance",
                rules=duplicate?new[]{rule,rule}:new[]{rule}
            });
        }
        File.WriteAllText(path,Document());
        var valid=MonsterDropCatalog.LoadVerified(path,[runtime]);
        Checks.Require(valid.Count==1 && valid[0].EnemyId==1 &&
            valid[0].Slot==0 && valid[0].ItemId==2701 &&
            valid[0].Trust==LootSourceTrust.Verified,
            "explicitly verified known monster rule was not accepted");

        foreach(string document in new[] {
            Document(verified:false), Document(enemy:999),
            Document(slot:32),Document(duplicate:true),
            "{", "{\"kind\":\"verified_monster_drop_rules\",\"version\":1}"
        })
        {
            File.WriteAllText(path,document);
            bool rejected=false;
            try { MonsterDropCatalog.LoadVerified(path,[runtime]); }
            catch(InvalidDataException) { rejected=true; }
            catch(JsonException) { rejected=true; }
            Checks.Require(rejected,
                "unverified, malformed or unknown loot rule enabled gameplay");
        }
    }

    private static async Task BoundedWorker(string root)
    {
        const int mapId=216;
        var runtime=FixtureRuntime(root,mapId);
        var monster=runtime.SnapshotAll().Single() with {
            State=MonsterAiState.Dead,CurrentHp=0,EntityRevision=8
        };
        using var store=new SqliteIdentityStore(
            Path.Combine(root,"loot-worker-"+Guid.NewGuid().ToString("N")+".db"));
        await store.InitializeAsync();
        long generation=await store.AllocateLootWorldGenerationAsync();
        var worker=new MonsterLootSettlement(store,generation,[
            new MonsterDropRule(mapId,monster.EnemyId,0,2701,2,
                LootSourceTrust.SyntheticTest,"ci:synthetic")
        ],capacity:1);

        Checks.Require(worker.TryPostDeath(monster with { EnemyId=999 })==
            MonsterLootPostStatus.NoRule,"unmapped enemy generated a drop");
        Checks.Require(worker.TryPostDeath(monster)==MonsterLootPostStatus.Enqueued,
            "trusted lethal monster event was not queued");
        Checks.Require(worker.TryPostDeath(monster with { EntityRevision=9 })==
            MonsterLootPostStatus.Backpressured &&
            worker.Statistics.Overflows==1,
            "bounded full queue silently discarded trusted death");
        var running=worker.RunAsync();
        worker.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(4));
        var drops=await store.ListActiveGroundDropsAsync(mapId);
        Checks.Require(drops.Count==1 &&
            drops[0].DeathRevision==
                SqliteIdentityStore.ComposeLootDeathRevision(generation,8) &&
            drops[0].ItemDefinitionId==2701 &&
            worker.Statistics.DeathsCommitted==1 &&
            worker.Statistics.DropsCommitted==1 &&
            worker.Statistics.Pending==0,
            "bounded drain did not atomically persist exactly one trusted drop");
    }
}
