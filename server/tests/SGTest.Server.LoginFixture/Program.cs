using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Clan;
using SGTest.Server.Friend;
using SGTest.Server.Party;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class Program
{
    internal const string Username = "fixture_user";
    internal const string Password = "Fixture-Password-123!";

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--multi"])
            return await RunMultiplayerAsync(false).ConfigureAwait(false);
        if (args is ["--multi-reconnect"])
            return await RunMultiplayerAsync(false, reconnect: true).ConfigureAwait(false);
        if (args is ["--crossmap"])
            return await RunMultiplayerAsync(true).ConfigureAwait(false);
        if (args is ["--portal"])
            return await RunMultiplayerAsync(false, true).ConfigureAwait(false);
        if (args is ["--reconnect"])
            return await RunReconnectAsync().ConfigureAwait(false);
        if (args is ["--combat"])
            return await RunCombatAsync().ConfigureAwait(false);
        if (args is ["--target-combat"])
            return await RunTargetCombatAsync().ConfigureAwait(false);
        if (args is ["--target-combat-miss"])
            return await RunTargetCombatAsync(forceMonsterMiss:true).ConfigureAwait(false);
        if (args is ["--combat-observer"])
            return await RunCombatObserverAsync().ConfigureAwait(false);
        if (args is ["--skill-aoe"])
            return await RunSkillAoeAsync().ConfigureAwait(false);
        if (args is ["--skill-aoe-observer"])
            return await RunSkillAoeObserverAsync().ConfigureAwait(false);
        if (args is ["--auto-attack"])
            return await RunAutoApproachAttackAsync().ConfigureAwait(false);
        if (args is ["--discard"])
            return await RunDiscardAsync().ConfigureAwait(false);
        if (args is ["--recovery-hotkey"])
            return await RecoveryHotkeyLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--clan-create"])
            return await ClanCreateLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--storage"])
            return await RunStorageAsync().ConfigureAwait(false);
        if (args is ["--pet"])
            return await PetLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--mecha"])
            return await MechaLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--equipment"])
            return await EquipmentLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--bank"])
            return await BankLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--repair"])
            return await RepairLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--enhancement"])
            return await EnhancementLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--quest"])
            return await QuestLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--trade"])
            return await TradeLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--player-shop"])
            return await PlayerShopLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--pvp"])
            return await PvpLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--register"])
            return await RunRegistrationAsync().ConfigureAwait(false);
        if (args is ["--appearance"])
            return await CharacterAppearanceLoginFixture.RunAsync().ConfigureAwait(false);
        if (args is ["--checkpoint-crash-producer", var producerDb])
            return await RunCheckpointCrashProducerAsync(producerDb).ConfigureAwait(false);
        if (args is ["--checkpoint-crash-verify", var verifyDb])
            return await RunCheckpointCrashVerifyAsync(verifyDb).ConfigureAwait(false);
        if (args.Length != 0)
            return 2;
        string root = Path.Combine(Path.GetTempPath(), "sgtest_login_fixture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string mapDir = CreateMap(root, 15);
            var grid = BlockingGrid.Load(mapDir, 15);
            await using var world = new ServerWorldHost(new[] { grid });
            await world.StartAsync();
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync();
            long accountId = (await store.CreateAccountAsync(Username, Password)).AccountId;
            var created = await store.CreateCharacterAsync(accountId, 0, "FixtureHero", new CharacterSpawn(15, 8, 8));
            long characterId = created.Character?.CharacterId ?? throw new InvalidOperationException("fixture character create failed");
            await store.SetCharacterSkillLevelAsync(
                characterId,28101,3,"ci:tls-skill-book-fixture");
            var authorization = new IdentitySessionAuthorization(store, world);
            var resultRouter = new WorldResultRouter();
            var aoiPump = new WorldAoiPump(resultRouter);

            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine($"LOGIN_FIXTURE_READY port={port} username={Username} character={characterId}");
                Console.Out.Flush();
                using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15));
                using var aoiStop = new CancellationTokenSource();
                var aoiTask = aoiPump.RunAsync(aoiStop.Token);
                try
                {
                var serverTask = authorization.RunAuthenticatedAesSocketAsync(client,
                    settings, resultRouter,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));

                using var receiptCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var receipt = await WaitForMoveAsync(world, resultRouter, characterId, receiptCts.Token);
                if (receipt.Status != MoveReceiptStatus.Accepted)
                    throw new InvalidOperationException("fixture move rejected: " + receipt.Status);
                if (resultRouter.Statistics.ReceiptsQueued != 1)
                    throw new InvalidOperationException("World result router did not route exactly one reliable ACK");
                Console.WriteLine($"LOGIN_FIXTURE_MOVE_PASS actor={receipt.ActorId} map_epoch={receipt.MapEpoch} sequence={receipt.MovementSequence}");
                Console.WriteLine($"LOGIN_FIXTURE_RECEIPT_ROUTED actor={receipt.ActorId} sequence={receipt.MovementSequence} status={receipt.Status}");
                Console.Out.Flush();

                // Continue routing every World tick after the first receipt;
                // owner position/stop updates must reach the real TLS Client too.
                var routingTask = resultRouter.RunAsync(world.Results);
                AesWireConnectionResult ended;
                try { ended = await serverTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally
                {
                    await world.StopAsync();
                    await routingTask.WaitAsync(TimeSpan.FromSeconds(3));
                }
                // WorldReady triggers one PvP mode query in addition to the Move.
                if (ended.CommandsAccepted != 2 || ended.ReliableFramesSent < 2 ||
                    aoiPump.FramesQueued < 1 || resultRouter.Statistics.ActiveSessions != 0)
                    throw new InvalidOperationException($"fixture disconnect or send mismatch commands={ended.CommandsAccepted} reliable_frames={ended.ReliableFramesSent} aoi={aoiPump.FramesQueued} active_routes={resultRouter.Statistics.ActiveSessions}");
                Console.WriteLine($"LOGIN_FIXTURE_WORLD_READY_ROUTED aoi_frames={aoiPump.FramesQueued}");
                Console.WriteLine($"LOGIN_FIXTURE_PASS aes=true commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
                }
                finally
                {
                    aoiStop.Cancel();
                    await aoiTask.ConfigureAwait(false);
                }
            }
            finally
            {
                listener.Stop();
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("LOGIN_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }


    private static async Task<int> RunRegistrationAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_registration_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var map=BlockingGrid.Load(CreateMap(root,15),15);
            await using var world=new ServerWorldHost([map]);
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            var authorization=new IdentitySessionAuthorization(
                store,world,
                registrationRateLimiter:new AccountRegistrationRateLimiter());
            var router=new WorldResultRouter();

            var settings=AesCbcSettings.FromEnvironment();

            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(1);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"REGISTER_FIXTURE_READY port={port} username={Username}");
                Console.Out.Flush();
                using var client=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var serverTask=authorization.RunAuthenticatedAesSocketAsync(
                    client,settings,router,
                    new AesWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));
                var ended=await serverTask.WaitAsync(TimeSpan.FromSeconds(15))
                    .ConfigureAwait(false);
                var auth=await store.AuthenticateAsync(
                    Username,Password).ConfigureAwait(false);
                if(auth.Status!=AuthenticationStatus.Success||
                   auth.Account is null||auth.Account.Characters.Count!=0||
                   world.Statistics.ActiveActors!=0||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        "registration fixture durable/auth/world boundary mismatch");
                await world.StopAsync().ConfigureAwait(false);
                Console.WriteLine(
                    $"REGISTER_FIXTURE_PASS reason={ended.Reason} account={auth.Account.AccountId} characters=0 active=0");
                Console.Out.Flush();
                return 0;
            }
            finally{listener.Stop();}
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("REGISTER_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }


    private static async Task<MoveReceipt> WaitForMoveAsync(ServerWorldHost world, WorldResultRouter router, long actorId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await world.Results.ReadAsync(cancellationToken);
            router.Route(result); // same production routing boundary as live World outputs
            foreach (var receipt in result.Receipts)
                if (receipt.ActorId == actorId && receipt.MovementSequence == 1)
                    return receipt;
        }
    }




    private static async Task<int> RunCombatAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_combat_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string mapDir = CreateMap(root, 15);
            var grid = BlockingGrid.Load(mapDir, 15);
            string progressionPath=Path.Combine(root,"combat-progression.json");
            WriteCombatProgressionManifest(progressionPath);
            var progressionRules=CharacterProgressionRules.LoadVerified(progressionPath);
            var experienceRule=new CharacterExperienceRewardRule(
                15,1,3,1,CharacterExperienceRewardTrust.SyntheticTest,
                "ci:combat-exp-fixture",EnemyDodge:0);
            string populationPath = Path.Combine(root, "combat-population.json");
            File.WriteAllText(populationPath, JsonSerializer.Serialize(new
            {
                version = 4,
                kind = "verified_monster_runtime",
                map_id = 15,
                stats_verified = true,
                gameplay_ready = true,
                definitions = new[]
                {
                    new
                    {
                        id = 1, name = "CombatFixture", sprite = "EN001A",
                        atlas = "Enemy/EN001A/atlas.json",
                        stats = new
                        {
                            level = 3, max_hp = 100, attack_damage = 20, move_step = 8.0,
                            aggro_radius = 1.0, lose_target_radius = 150.0,
                            leash_radius = 150.0, attack_range = 100.0,
                            attack_cooldown_ticks = 1000, respawn_ticks = 5,
                            wander_radius = 0.0, wander_interval_ticks = 100
                        }
                    }
                },
                spawns = new[]
                {
                    new { source_index = 0, source_flags = 7, definition = 1,
                          p = new[] { 40, 24 } }
                }
            }));
            var population = MonsterPopulationCatalog.LoadVerified(
                populationPath, 15, grid);
            var runtime = new MonsterRuntime(grid, population,
                new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                    MaxPathQueriesPerStep: 1, MaxPathExpandedCells: 128));
            var coordinator = new MonsterWorldCoordinator([runtime],
                new Dictionary<int,VerifiedSkillRule>
                {
                    // Synthetic single-target damage/range fixture; MpCost=1
                    // matches the current-client 99001 source resource cost only.
                    [99001]=new(99001,40,48,1,MpCost:1),
                    // Explicit synthetic failure-path rule. It exists only to
                    // prove insufficient MP cannot mutate the target.
                    [99002]=new(99002,10,48,1,MpCost:2)
                },
                normalAttackHitRules:[experienceRule],
                normalAttackRoll: () => 0);

            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long lootGeneration = await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var loot = new MonsterLootSettlement(
                store, lootGeneration,
                [new MonsterDropRule(
                    15, 1, 0, 2701, 2,
                    LootSourceTrust.SyntheticTest, "ci:v2-probability",
                    EnemyLevel: 3, RollThreshold: 100,
                    LevelDeltaDivisor: 15, Guaranteed: false)],
                capacity: 8, nextRoll: () => 0);
            long accountId = (await store.CreateAccountAsync(Username, Password))
                .AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "CombatHero", new CharacterSpawn(15, 24, 24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("combat character create failed");
            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.Verified,
                    "ci:combat-progression-fixture",
                    Race: 0, Career: 0, Strength: 5, Dexterity: 5,
                    Constitution: 5, Wisdom: 5, Level: 1,
                    CurrentHp: 18, MaxHp: 100,
                    Attack: 60, Defense: 10, Hit: 10, Dodge: 3,
                    AttackRange: 48, AttackCooldownTicks: 15,
                    CurrentMp:2, MaxMp:2))
                .ConfigureAwait(false);

            var router = new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity: 8));
            var experience=new CharacterExperienceSettlement(
                store,router,lootGeneration,[experienceRule],
                progressionRules:progressionRules,capacity:8);
            var settlement = new PlayerCombatSettlement(
                store, new(Capacity: 8), router.QueuePlayerDamage,
                committedLifecycle: async result =>
                {
                    if (result.Died)
                    {
                        var stopped = await world.TrustedSetCombatDeadAsync(
                            result.ActorId, result.ConnectionEpoch, result.MapEpoch, true)
                            .ConfigureAwait(false);
                        if (stopped is null)
                            throw new PlayerCombatSettlementException(
                                "combat fixture lethal commit did not stop World actor");
                    }
                    if (!router.TryPostPlayerDamageVisual(result))
                        throw new PlayerCombatSettlementException(
                            "combat fixture damage visual queue full");
                });
            var pump = new WorldAoiPump(
                router, monsters: coordinator, playerCombat: settlement,
                loot: loot,experience:experience);
            var authorization = new IdentitySessionAuthorization(
                store, world, combat: settlement,
                pickupWorldGeneration: lootGeneration,
                progressionRules:progressionRules);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results);
            var pumping = pump.RunAsync(stop.Token);
            var settling = settlement.RunAsync(stop.Token);
            var experiencing = experience.RunAsync();
            var looting = loot.RunAsync();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"COMBAT_FIXTURE_READY port={port} username={Username} character={characterId}");
                Console.Out.Flush();

                using var client = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended = await authorization.RunAuthenticatedAesSocketAsync(
                    client, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000))
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                using var relogClient = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var relogEnded = await authorization.RunAuthenticatedAesSocketAsync(
                    relogClient, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000))
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                var monster = runtime.SnapshotAll().Single();
                var player = await store.GetCharacterCombatProfileAsync(characterId)
                    .ConfigureAwait(false);
                var inventory = await store.ListInventoryItemsAsync(characterId)
                    .ConfigureAwait(false);
                var activeDrops = await store.ListActiveGroundDropsAsync(15)
                    .ConfigureAwait(false);
                var characterExperience=await store.GetCharacterExperienceAsync(characterId)
                    .ConfigureAwait(false);
                if (monster.CurrentHp != 100 || monster.MaxHp != 100 ||
                    player?.Data.CurrentHp != 100 || player.Data.MaxHp != 100 ||
                    player.Data.CurrentMp != 1 || player.Data.MaxMp != 2 ||
                    inventory.Count != 1 ||
                    inventory[0].ItemDefinitionId != 2701 ||
                    inventory[0].Quantity != 2 ||
                    inventory[0].SourceDropId <= 0 ||
                    activeDrops.Count != 0 ||
                    characterExperience?.Experience != 1 ||
                    experience.Statistics.GrantsApplied != 1 ||
                    loot.Statistics.DeathsCommitted != 1 ||
                    loot.Statistics.DropsCommitted != 1 ||
                    ended.CommandsAccepted != 7 ||
                    relogEnded.CommandsAccepted != 1 ||
                    settlement.Statistics.HitsCommitted != 1 ||
                    settlement.Statistics.DamageCommitted != 18 ||
                    settlement.Statistics.DeathsCommitted != 1 ||
                    router.Statistics.ActiveSessions != 0)
                    throw new InvalidOperationException(
                        $"combat E2E mismatch monster={monster.CurrentHp}/{monster.MaxHp} " +
                        $"player={player?.Data.CurrentHp}/{player?.Data.MaxHp} " +
                        $"inventory={inventory.Count} activeDrops={activeDrops.Count} exp={characterExperience?.Experience} " +
                        $"expGrants={experience.Statistics.GrantsApplied} " +
                        $"lootDeaths={loot.Statistics.DeathsCommitted} lootDrops={loot.Statistics.DropsCommitted} " +
                        $"hits={settlement.Statistics.HitsCommitted} deaths={settlement.Statistics.DeathsCommitted} " +
                        $"commands={ended.CommandsAccepted} relogCommands={relogEnded.CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"COMBAT_FIXTURE_PASS aes=true target={monster.EntityId} " +
                    $"monster_hp={monster.CurrentHp}/{monster.MaxHp} player_hp={player.Data.CurrentHp}/{player.Data.MaxHp} " +
                    $"player_mp={player.Data.CurrentMp}/{player.Data.MaxMp} respawn=true skill_99001=true skill_mp_cost=1 " +
                    $"loot_v2=true item=2701 quantity=2 source_drop={inventory[0].SourceDropId} pickup=true " +
                    $"experience={characterExperience!.Experience} experience_percent=10 relog_experience=true " +
                    $"relog_inventory=true ground_drop_absent_after_relog=true " +
                    $"insufficient_skill_99002=true monster_damage_committed={settlement.Statistics.DamageCommitted} " +
                    $"deaths={settlement.Statistics.DeathsCommitted} commands={ended.CommandsAccepted} " +
                    $"relog_commands={relogEnded.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                loot.Complete();
                experience.Complete();
                await Task.WhenAll(looting,experiencing)
                    .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                stop.Cancel();
                await Task.WhenAll(pumping, settling).ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("COMBAT_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }


    private static void WriteCombatProgressionManifest(string path)
    {
        var levels=Enumerable.Range(1,100).Select(level=>new
        {
            level,
            required_experience=level==1?10L:1000L+level,
            attribute_points_awarded=2,
            base_hp=100,
            base_mp=2,
            base_attack=60,
            base_defense=10,
            base_magic_attack=0,
            base_magic_defense=0,
            base_hit=10,
            base_dodge=3,
            base_will=0
        }).ToArray();
        int[] ramp=Enumerable.Repeat(0,200).ToArray();
        int[] hp=Enumerable.Repeat(0,200).ToArray();
        int[] hit=Enumerable.Repeat(0,200).ToArray();
        int[] mp=Enumerable.Repeat(0,200).ToArray();
        var race=new
        {
            strength_attack=ramp,
            constitution_hp=hp,
            dexterity_hit=hit,
            dexterity_dodge=hit,
            wisdom_mp=mp,
            wisdom_magic_attack=hit
        };
        File.WriteAllText(path,JsonSerializer.Serialize(new
        {
            schema="sgtest.character_progression_rules/v1",
            status="pass",
            rules_verified=true,
            gameplay_ready=true,
            source="synthetic combat E2E progression fixture",
            max_level=100,
            max_attribute=200,
            levels,
            races=new {boy=race,girl=race,migu=race,robot=race}
        }));
    }

    private static async Task<int> RunTargetCombatAsync(bool forceMonsterMiss=false)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_target_combat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(CreateMap(root, 15, width: 40, height: 3), 15);
            string populationPath = Path.Combine(root, "target-combat-population.json");
            File.WriteAllText(populationPath, JsonSerializer.Serialize(new
            {
                version = 7,
                kind = "verified_monster_retaliation_chase_runtime",
                map_id = 15,
                stats_verified = true,
                target_combat_ready = true,
                retaliation_ready = true,
                retaliation_chase_ready = true,
                ai_ready = false,
                definitions = new[]
                {
                    new
                    {
                        id = 1,
                        sprite = "EN001A",
                        atlas = "Enemy/EN001A/atlas.json",
                        stats = new
                        {
                            level = 3,
                            max_hp = 100,
                            attack_damage = 17,
                            hit_rate = 30,
                            defense_power = 20,
                            magic_defense_power = 15,
                            dodge_rate = 5,
                            move_step = 385,
                            attack_range = 150,
                            lose_target_radius = 400,
                            attack_cooldown_ticks = 50,
                            respawn_ticks = 300
                        }
                    }
                },
                spawns = new[]
                {
                    new { source_index = 0, source_flags = 7, definition = 1,
                          p = new[] { 40, 24 } }
                }
            }));
            var runtime = new MonsterRuntime(
                grid, MonsterPopulationCatalog.LoadVerified(populationPath, 15, grid));
            var coordinator = new MonsterWorldCoordinator(
                [runtime],
                normalAttackRoll: () => 0,
                normalAttackDamageRoll: exclusiveMax => exclusiveMax == 20 ? 10 : 0);

            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(Username, Password)).AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "TargetCombatHero", new CharacterSpawn(15, 24, 24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("target combat character create failed");
            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:v7-retaliation-chase-combat-aes",
                    Race: 0, Career: 0, Strength: 5, Dexterity: 5,
                    Constitution: 5, Wisdom: 5, Level: 3,
                    CurrentHp: 100, MaxHp: 100,
                    Attack: 60, Defense: 10, Hit: 10, Dodge: 3,
                    AttackRange: 48, AttackCooldownTicks: 15,
                    CurrentMp: 0, MaxMp: 0))
                .ConfigureAwait(false);

            var router = new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity: 8));
            var settlement = new PlayerCombatSettlement(
                store, new(Capacity: 8), router.QueuePlayerDamage,
                committedLifecycle: result =>
                {
                    if (!router.TryPostPlayerDamageVisual(result))
                        throw new PlayerCombatSettlementException(
                            "target combat fixture damage visual queue full");
                    return Task.CompletedTask;
                },
                monsterDamageRoll: _ => 0,
                monsterHitRoll: _ => forceMonsterMiss ? 99 : 0);
            var pump = new WorldAoiPump(
                router, monsters: coordinator, playerCombat: settlement);
            var authorization = new IdentitySessionAuthorization(
                store, world, combat: settlement);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results);
            var pumping = pump.RunAsync(stop.Token);
            var settling = settlement.RunAsync(stop.Token);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"TARGET_COMBAT_FIXTURE_READY port={port} username={Username} " +
                    $"character={characterId}");
                Console.Out.Flush();

                using var client = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended = await authorization.RunAuthenticatedAesSocketAsync(
                    client, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000))
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                var monster = runtime.SnapshotAll().Single();
                var player = await store.GetCharacterCombatProfileAsync(characterId)
                    .ConfigureAwait(false);
                int expectedPlayerHp=forceMonsterMiss?100:92;
                long expectedCommittedHits=forceMonsterMiss?0:1;
                long expectedCommittedDamage=forceMonsterMiss?0:8;
                if (monster.CurrentHp != 60 || monster.MaxHp != 100 ||
                    player?.Data.CurrentHp != expectedPlayerHp || player.Data.MaxHp != 100 ||
                    ended.CommandsAccepted < 1 ||
                    settlement.Statistics.HitsAccepted != 1 ||
                    settlement.Statistics.HitsCommitted != expectedCommittedHits ||
                    settlement.Statistics.DamageCommitted != expectedCommittedDamage)
                    throw new InvalidOperationException(
                        $"target combat E2E mismatch monster={monster.CurrentHp}/{monster.MaxHp} " +
                        $"player={player?.Data.CurrentHp}/{player?.Data.MaxHp} " +
                        $"commands={ended.CommandsAccepted} " +
                        $"player_accepted={settlement.Statistics.HitsAccepted} " +
                        $"player_hits={settlement.Statistics.HitsCommitted} " +
                        $"player_damage={settlement.Statistics.DamageCommitted}");

                Console.WriteLine(
                    $"TARGET_COMBAT_FIXTURE_PASS aes=true target={monster.EntityId} " +
                    $"monster_hp={monster.CurrentHp}/{monster.MaxHp} damage=40 " +
                    $"player_hp={player.Data.CurrentHp}/{player.Data.MaxHp} " +
                    $"retaliation={(forceMonsterMiss?"MISS":"8")} " +
                    $"v7_retaliation_chase=true experience_manifest=false ai=false chase=true monster_x={monster.X:F1} " +
                    $"commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await Task.WhenAll(pumping, settling).ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("TARGET_COMBAT_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }


    private static async Task<int> RunSkillAoeAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_skill_aoe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(CreateMap(root,15),15);
            string populationPath=Path.Combine(root,"skill-aoe-population.json");
            File.WriteAllText(populationPath,JsonSerializer.Serialize(new
            {
                version=4,kind="verified_monster_runtime",map_id=15,
                stats_verified=true,gameplay_ready=true,
                definitions=new[]
                {
                    new
                    {
                        id=1,name="SkillAoeFixture",sprite="EN001A",
                        atlas="Enemy/EN001A/atlas.json",
                        stats=new
                        {
                            level=3,max_hp=100,attack_damage=1,move_step=8.0,
                            aggro_radius=1.0,lose_target_radius=2.0,
                            leash_radius=2.0,attack_range=1.0,
                            attack_cooldown_ticks=1000,respawn_ticks=300,
                            wander_radius=0.0,wander_interval_ticks=100
                        }
                    }
                },
                spawns=new[]
                {
                    new {source_index=0,source_flags=7,definition=1,p=new[]{24,8}},
                    new {source_index=1,source_flags=7,definition=1,p=new[]{40,24}},
                    new {source_index=2,source_flags=7,definition=1,p=new[]{40,8}}
                }
            }));
            var runtime=new MonsterRuntime(
                grid,MonsterPopulationCatalog.LoadVerified(populationPath,15,grid),
                new(AiIntervalTicks:1,MaxAiPerStep:8,
                    MaxPathQueriesPerStep:1,MaxPathExpandedCells:128));
            var coordinator=new MonsterWorldCoordinator([runtime],
                new Dictionary<int,VerifiedSkillRule>
                {
                    [99001]=new(99001,40,0,0,1,
                        SkillTargetKind.SelfCenteredArea,18,false)
                });

            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId=(await store.CreateAccountAsync(Username,Password))
                .AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"SkillAoeHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("skill AoE character create failed");
            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:tls-skill-aoe-fixture",
                    Race:0,Career:0,Strength:5,Dexterity:5,
                    Constitution:5,Wisdom:5,Level:1,
                    CurrentHp:100,MaxHp:100,Attack:1,Defense:10,
                    Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15,
                    CurrentMp:2,MaxMp:2)).ConfigureAwait(false);

            var router=new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity:8));
            var settlement=new PlayerCombatSettlement(
                store,new(Capacity:8),router.QueuePlayerDamage);
            var pump=new WorldAoiPump(
                router,monsters:coordinator,playerCombat:settlement);
            var authorization=new IdentitySessionAuthorization(
                store,world,combat:settlement);
            var settings=AesCbcSettings.FromEnvironment();

            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(1);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            var settling=settlement.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"SKILL_AOE_FIXTURE_READY port={port} " +
                    $"username={Username} character={characterId}");
                Console.Out.Flush();

                using var client=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended=await authorization.RunAuthenticatedAesSocketAsync(
                    client,settings,router,
                    new AesWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000))
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                var monsters=runtime.SnapshotAll()
                    .OrderBy(x=>x.SourceIndex).ToArray();
                var player=await store.GetCharacterCombatProfileAsync(characterId)
                    .ConfigureAwait(false);
                if(monsters.Length!=3 ||
                    monsters[0].CurrentHp!=60 ||
                    monsters[1].CurrentHp!=60 ||
                    monsters[2].CurrentHp!=100 ||
                    player?.Data.CurrentMp!=1 || player.Data.MaxMp!=2 ||
                    ended.CommandsAccepted!=2 ||
                    router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"skill AoE E2E mismatch hp=" +
                        $"{string.Join(',',monsters.Select(x=>x.CurrentHp))} " +
                        $"mp={player?.Data.CurrentMp}/{player?.Data.MaxMp} " +
                        $"commands={ended.CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"SKILL_AOE_FIXTURE_PASS aes=true " +
                    $"monster_hp={monsters[0].CurrentHp},{monsters[1].CurrentHp},{monsters[2].CurrentHp} " +
                    $"player_mp={player!.Data.CurrentMp}/{player.Data.MaxMp} " +
                    $"commands={ended.CommandsAccepted} aoi_frames={pump.FramesQueued}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await Task.WhenAll(pumping,settling).ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("SKILL_AOE_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root,recursive:true); } catch { }
        }
    }


    private static async Task<int> RunSkillAoeObserverAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_skill_aoe_observer_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid=BlockingGrid.Load(CreateMap(root,15),15);
            string populationPath=Path.Combine(root,"skill-aoe-observer-population.json");
            File.WriteAllText(populationPath,JsonSerializer.Serialize(new
            {
                version=4,kind="verified_monster_runtime",map_id=15,
                stats_verified=true,gameplay_ready=true,
                definitions=new[]
                {
                    new
                    {
                        id=1,name="SkillAoeObserverFixture",sprite="EN001A",
                        atlas="Enemy/EN001A/atlas.json",
                        stats=new
                        {
                            level=3,max_hp=100,attack_damage=1,move_step=8.0,
                            aggro_radius=1.0,lose_target_radius=2.0,
                            leash_radius=2.0,attack_range=1.0,
                            attack_cooldown_ticks=1000,respawn_ticks=300,
                            wander_radius=0.0,wander_interval_ticks=100
                        }
                    }
                },
                spawns=new[]
                {
                    new {source_index=0,source_flags=7,definition=1,p=new[]{24,8}},
                    new {source_index=1,source_flags=7,definition=1,p=new[]{40,24}},
                    new {source_index=2,source_flags=7,definition=1,p=new[]{40,8}}
                }
            }));
            var runtime=new MonsterRuntime(
                grid,MonsterPopulationCatalog.LoadVerified(populationPath,15,grid),
                new(AiIntervalTicks:1,MaxAiPerStep:8,
                    MaxPathQueriesPerStep:1,MaxPathExpandedCells:128));
            var coordinator=new MonsterWorldCoordinator([runtime],
                new Dictionary<int,VerifiedSkillRule>
                {
                    [99001]=new(99001,40,0,0,1,
                        SkillTargetKind.SelfCenteredArea,18,false)
                });

            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long observerAccount=(await store.CreateAccountAsync(
                "fixture_one",Password)).AccountId;
            long actorAccount=(await store.CreateAccountAsync(
                "fixture_two",Password)).AccountId;
            long observerId=(await store.CreateCharacterAsync(
                observerAccount,0,"SkillAoeObserver",new CharacterSpawn(15,8,24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("skill AoE observer missing");
            long actorId=(await store.CreateCharacterAsync(
                actorAccount,0,"SkillAoeActor",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("skill AoE actor missing");
            await store.SetCharacterCombatProfileAsync(actorId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:two-client-skill-aoe",
                    Race:0,Career:0,Strength:5,Dexterity:5,
                    Constitution:5,Wisdom:5,Level:1,
                    CurrentHp:100,MaxHp:100,Attack:1,Defense:10,
                    Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15,
                    CurrentMp:2,MaxMp:2)).ConfigureAwait(false);

            var router=new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity:8));
            var settlement=new PlayerCombatSettlement(
                store,new(Capacity:8),router.QueuePlayerDamage);
            var pump=new WorldAoiPump(
                router,monsters:coordinator,playerCombat:settlement);
            var authorization=new IdentitySessionAuthorization(
                store,world,combat:settlement);
            var settings=AesCbcSettings.FromEnvironment();

            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(2);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            var settling=settlement.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"SKILL_AOE_OBSERVER_FIXTURE_READY port={port} " +
                    $"actor_a={observerId} actor_b={actorId}");
                Console.Out.Flush();

                using var first=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var firstTask=authorization.RunAuthenticatedAesSocketAsync(
                    first,settings,router,
                    new AesWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));
                using var second=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var secondTask=authorization.RunAuthenticatedAesSocketAsync(
                    second,settings,router,
                    new AesWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));

                var results=await Task.WhenAll(firstTask,secondTask)
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                var monsters=runtime.SnapshotAll().OrderBy(x=>x.SourceIndex).ToArray();
                var actorProfile=await store.GetCharacterCombatProfileAsync(actorId)
                    .ConfigureAwait(false);
                var deadline=DateTime.UtcNow.AddSeconds(3);
                while(world.Statistics.ActiveActors!=0 ||
                      router.Statistics.ActiveSessions!=0)
                {
                    if(DateTime.UtcNow>=deadline)
                        throw new InvalidOperationException(
                            "skill AoE observer left active actor/session");
                    await Task.Delay(5).ConfigureAwait(false);
                }
                if(monsters.Length!=3 ||
                    monsters[0].CurrentHp!=60 ||
                    monsters[1].CurrentHp!=60 ||
                    monsters[2].CurrentHp!=100 ||
                    actorProfile?.Data.CurrentMp!=1 ||
                    actorProfile.Data.MaxMp!=2 ||
                    results[0].Reason!=AesWireEndReason.CleanEof ||
                    results[1].Reason!=AesWireEndReason.CleanEof ||
                    results[0].CommandsAccepted!=1 ||
                    results[1].CommandsAccepted!=2)
                    throw new InvalidOperationException(
                        $"skill AoE observer mismatch hp=" +
                        $"{string.Join(',',monsters.Select(x=>x.CurrentHp))} " +
                        $"mp={actorProfile?.Data.CurrentMp}/{actorProfile?.Data.MaxMp} " +
                        $"observer_cmd={results[0].CommandsAccepted} " +
                        $"actor_cmd={results[1].CommandsAccepted}");

                Console.WriteLine(
                    $"SKILL_AOE_OBSERVER_FIXTURE_PASS actor_a={observerId} actor_b={actorId} " +
                    $"monster_hp={monsters[0].CurrentHp},{monsters[1].CurrentHp},{monsters[2].CurrentHp} " +
                    $"actor_mp={actorProfile!.Data.CurrentMp}/{actorProfile.Data.MaxMp} " +
                    $"observer_commands={results[0].CommandsAccepted} " +
                    $"actor_commands={results[1].CommandsAccepted} aoi_frames={pump.FramesQueued}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await Task.WhenAll(pumping,settling).ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("SKILL_AOE_OBSERVER_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root,recursive:true); } catch { }
        }
    }

    private static async Task<int> RunCombatObserverAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_combat_observer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(CreateMap(root, 15), 15);
            string populationPath = Path.Combine(root, "combat-observer-population.json");
            File.WriteAllText(populationPath, JsonSerializer.Serialize(new
            {
                version = 4,
                kind = "verified_monster_runtime",
                map_id = 15,
                stats_verified = true,
                gameplay_ready = true,
                definitions = new[]
                {
                    new
                    {
                        id = 1, name = "CombatObserverFixture", sprite = "EN001A",
                        atlas = "Enemy/EN001A/atlas.json",
                        stats = new
                        {
                            level = 3, max_hp = 100, attack_damage = 20,
                            move_step = 8.0, aggro_radius = 100.0,
                            lose_target_radius = 150.0, leash_radius = 150.0,
                            attack_range = 20.0, attack_cooldown_ticks = 1000,
                            respawn_ticks = 300, wander_radius = 0.0,
                            wander_interval_ticks = 100
                        }
                    }
                },
                spawns = new[]
                {
                    new { source_index = 0, source_flags = 7, definition = 1,
                          p = new[] { 40, 8 } }
                }
            }));
            var population = MonsterPopulationCatalog.LoadVerified(
                populationPath, 15, grid);
            var runtime = new MonsterRuntime(grid, population,
                new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                    MaxPathQueriesPerStep: 1, MaxPathExpandedCells: 128));
            var coordinator = new MonsterWorldCoordinator([runtime],
                new Dictionary<int,VerifiedSkillRule>
                {
                    // Synthetic single-target damage/range fixture; MpCost=1
                    // matches the current-client 99001 source resource cost only.
                    [99001]=new(99001,40,48,1,MpCost:1)
                });

            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long observerAccount = (await store.CreateAccountAsync(
                "fixture_one", Password)).AccountId;
            long actorAccount = (await store.CreateAccountAsync(
                "fixture_two", Password)).AccountId;
            long observerId = (await store.CreateCharacterAsync(
                observerAccount,0,"CombatObserver",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("combat observer missing");
            long actorId = (await store.CreateCharacterAsync(
                actorAccount,0,"CombatActor",new CharacterSpawn(15,24,8)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("combat actor missing");
            await store.SetCharacterCombatProfileAsync(actorId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:two-client-combat-visual",
                    Race:0,Career:0,Strength:5,Dexterity:5,
                    Constitution:5,Wisdom:5,Level:1,
                    CurrentHp:100,MaxHp:100,Attack:60,Defense:10,
                    Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15,
                    CurrentMp:2,MaxMp:2))
                .ConfigureAwait(false);

            var router = new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity:8));
            var settlement = new PlayerCombatSettlement(
                store,new(Capacity:8),router.QueuePlayerDamage,
                committedLifecycle: result =>
                {
                    if (!router.TryPostPlayerDamageVisual(result))
                        throw new PlayerCombatSettlementException(
                            "combat observer damage visual queue full");
                    return Task.CompletedTask;
                });
            var pump = new WorldAoiPump(
                router,monsters:coordinator,playerCombat:settlement);
            var authorization = new IdentitySessionAuthorization(
                store,world,combat:settlement);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback,0);
            listener.Start(2);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results);
            var pumping = pump.RunAsync(stop.Token);
            var settling = settlement.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"COMBAT_OBSERVER_FIXTURE_READY port={port} " +
                    $"actor_a={observerId} actor_b={actorId}");
                Console.Out.Flush();

                using var first=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var firstTask=authorization.RunAuthenticatedAesSocketAsync(
                    first,settings,router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000));
                using var second=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var secondTask=authorization.RunAuthenticatedAesSocketAsync(
                    second,settings,router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000));

                var results=await Task.WhenAll(firstTask,secondTask)
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                var monster=runtime.SnapshotAll().Single();
                var actorProfile=await store.GetCharacterCombatProfileAsync(actorId)
                    .ConfigureAwait(false);
                var deadline=DateTime.UtcNow.AddSeconds(3);
                while(world.Statistics.ActiveActors!=0 ||
                      router.Statistics.ActiveSessions!=0)
                {
                    if(DateTime.UtcNow>=deadline)
                        throw new InvalidOperationException(
                            "combat observer left active actor/session");
                    await Task.Delay(5).ConfigureAwait(false);
                }
                if(monster.CurrentHp!=0 || monster.MaxHp!=100 ||
                    actorProfile?.Data.CurrentHp!=82 ||
                    actorProfile.Data.CurrentMp!=1 || actorProfile.Data.MaxMp!=2 ||
                    settlement.Statistics.HitsCommitted!=1 ||
                    settlement.Statistics.DamageCommitted!=18 ||
                    results[0].Reason!=AesWireEndReason.CleanEof ||
                    results[1].Reason!=AesWireEndReason.CleanEof ||
                    results[0].CommandsAccepted!=1 ||
                    results[1].CommandsAccepted!=3 ||
                    pump.FramesQueued<10)
                    throw new InvalidOperationException(
                        $"combat observer mismatch hp={monster.CurrentHp}/{monster.MaxHp} " +
                        $"actor_hp={actorProfile?.Data.CurrentHp} " +
                        $"hits={settlement.Statistics.HitsCommitted} " +
                        $"observer_cmd={results[0].CommandsAccepted} actor_cmd={results[1].CommandsAccepted} " +
                        $"aoi_frames={pump.FramesQueued}");

                Console.WriteLine(
                    $"COMBAT_OBSERVER_FIXTURE_PASS actor_a={observerId} actor_b={actorId} " +
                    $"monster_hp={monster.CurrentHp}/{monster.MaxHp} " +
                    $"actor_hp={actorProfile!.Data.CurrentHp}/{actorProfile.Data.MaxHp} " +
                    $"actor_mp={actorProfile.Data.CurrentMp}/{actorProfile.Data.MaxMp} " +
                    $"monster_damage={settlement.Statistics.DamageCommitted} " +
                    $"actor_commands={results[1].CommandsAccepted} skill_99001=true aoi_frames={pump.FramesQueued}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await Task.WhenAll(pumping,settling).ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("COMBAT_OBSERVER_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root,recursive:true); } catch { }
        }
    }

    private static async Task<int> RunAutoApproachAttackAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_auto_attack_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(CreateMap(root, 15), 15);
            string populationPath = Path.Combine(root, "auto-attack-population.json");
            File.WriteAllText(populationPath, JsonSerializer.Serialize(new
            {
                version = 4,
                kind = "verified_monster_runtime",
                map_id = 15,
                stats_verified = true,
                gameplay_ready = true,
                definitions = new[]
                {
                    new
                    {
                        id = 1, name = "AutoApproachFixture", sprite = "EN001A",
                        atlas = "Enemy/EN001A/atlas.json",
                        stats = new
                        {
                            level = 3, max_hp = 100, attack_damage = 1,
                            move_step = 8.0, aggro_radius = 1.0,
                            lose_target_radius = 16.0, leash_radius = 16.0,
                            attack_range = 1.0, attack_cooldown_ticks = 1000,
                            respawn_ticks = 300, wander_radius = 0.0,
                            wander_interval_ticks = 100
                        }
                    }
                },
                spawns = new[]
                {
                    new { source_index = 0, source_flags = 7, definition = 1,
                          p = new[] { 40, 8 } }
                }
            }));
            var population = MonsterPopulationCatalog.LoadVerified(
                populationPath, 15, grid);
            var runtime = new MonsterRuntime(grid, population,
                new(AiIntervalTicks: 1, MaxAiPerStep: 8,
                    MaxPathQueriesPerStep: 1, MaxPathExpandedCells: 128));
            var coordinator = new MonsterWorldCoordinator([runtime]);

            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(Username, Password))
                .AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "AutoApproachHero", new CharacterSpawn(15, 8, 8)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("auto approach character create failed");
            await store.SetCharacterCombatProfileAsync(characterId,
                new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:tls-auto-approach-fixture",
                    Race:0,Career:0,Strength:5,Dexterity:5,
                    Constitution:5,Wisdom:5,Level:1,
                    CurrentHp:100,MaxHp:100,Attack:60,Defense:10,
                    Hit:10,Dodge:3,AttackRange:16,AttackCooldownTicks:3))
                .ConfigureAwait(false);

            var router = new WorldResultRouter(
                new WorldResultRouterOptions(AttackRequestCapacity:8));
            var pump = new WorldAoiPump(router, monsters:coordinator);
            var authorization = new IdentitySessionAuthorization(store, world);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results);
            var pumping = pump.RunAsync(stop.Token);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"AUTO_ATTACK_FIXTURE_READY port={port} " +
                    $"username={Username} character={characterId}");
                Console.Out.Flush();

                using var client = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended = await authorization.RunAuthenticatedAesSocketAsync(
                    client, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                var monster = runtime.SnapshotAll().Single();
                var character = (await store.ListCharactersAsync(accountId)
                    .ConfigureAwait(false)).Single();
                if (monster.CurrentHp != 40 || monster.MaxHp != 100 ||
                    Math.Abs(character.X-40) > 0.001 || Math.Abs(character.Y-8) > 0.001 ||
                    ended.CommandsAccepted != 4 || router.Statistics.ActiveSessions != 0)
                    throw new InvalidOperationException(
                        $"auto approach E2E mismatch monster={monster.CurrentHp}/{monster.MaxHp} " +
                        $"position={character.X:F1},{character.Y:F1} " +
                        $"commands={ended.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"AUTO_ATTACK_FIXTURE_PASS aes=true " +
                    $"monster_hp={monster.CurrentHp}/{monster.MaxHp} " +
                    $"final_x={character.X:F1} final_y={character.Y:F1} " +
                    $"commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("AUTO_ATTACK_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive:true); } catch { }
        }
    }

    private static async Task<int> RunDiscardAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_discard_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(CreateMap(root, 15), 15);
            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(Username, Password))
                .AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "DiscardHero", new CharacterSpawn(15, 24, 24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("discard character create failed");

            long generation = await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var drops = await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(
                    15,24,24,1001500001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                    0,2701,2,LootSourceTrust.SyntheticTest,
                    "ci:godot-tls-discard")
            ]).ConfigureAwait(false);
            var grant = await store.ClaimGroundDropAsync(
                new TrustedPickupContext(characterId,19,1,1,15,24,24),
                drops.Single().DropId).ConfigureAwait(false);
            if (grant.Status != PickupStatus.Granted || grant.ItemUid <= 0)
                throw new InvalidOperationException(
                    "discard fixture could not seed durable inventory");

            var router = new WorldResultRouter();
            var pump = new WorldAoiPump(router);
            var authorization = new IdentitySessionAuthorization(
                store, world, pickupWorldGeneration:generation);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results);
            var pumping = pump.RunAsync(stop.Token);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"DISCARD_FIXTURE_READY port={port} username={Username} " +
                    $"character={characterId} item_uid={grant.ItemUid}");
                Console.Out.Flush();

                using var client = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended = await authorization.RunAuthenticatedAesSocketAsync(
                    client, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000))
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

                var remaining = await store.ListInventoryItemsAsync(characterId)
                    .ConfigureAwait(false);
                if (remaining.Count != 0 || ended.CommandsAccepted != 3 ||
                    router.Statistics.ActiveSessions != 0)
                    throw new InvalidOperationException(
                        $"discard E2E mismatch items={remaining.Count} " +
                        $"commands={ended.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"DISCARD_FIXTURE_PASS aes=true item_uid={grant.ItemUid} " +
                    $"remaining=0 commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("DISCARD_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunStorageAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_storage_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid = BlockingGrid.Load(CreateMap(root, 15), 15);
            await using var world = new ServerWorldHost(
                [grid], options: new WorldHostOptions(PublishIdleTicks: true));
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(Username, Password))
                .AccountId;
            long characterId = (await store.CreateCharacterAsync(
                accountId, 0, "StorageHero", new CharacterSpawn(15, 24, 24)))
                .Character?.CharacterId
                ?? throw new InvalidOperationException("storage character create failed");

            long generation = await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var drops = await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(
                    15,24,24,1001600001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                    0,2701,2,LootSourceTrust.SyntheticTest,
                    "ci:godot-tls-storage")
            ]).ConfigureAwait(false);
            var grant = await store.ClaimGroundDropAsync(
                new TrustedPickupContext(characterId,19,1,1,15,24,24),
                drops.Single().DropId).ConfigureAwait(false);
            if (grant.Status != PickupStatus.Granted || grant.ItemUid <= 0 ||
                grant.Quantity != 2)
                throw new InvalidOperationException(
                    "storage fixture could not seed durable inventory");
            using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(root, "identity.db"),
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "UPDATE item_instances SET enhancement_level=5 WHERE item_uid=$uid;";
                command.Parameters.AddWithValue("$uid", grant.ItemUid);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "storage fixture enhancement seed failed");
            }

            var storageCatalog = new NpcStorageCatalog([
                new NpcStorageCatalog.StoragePoint(
                    15,88,24,24,"ci:godot-tls-storage")
            ]);
            var router = new WorldResultRouter();
            var pump = new WorldAoiPump(router);
            var authorization = new IdentitySessionAuthorization(
                store, world, pickupWorldGeneration:generation,
                storages:storageCatalog);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results);
            var pumping = pump.RunAsync(stop.Token);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"STORAGE_FIXTURE_READY port={port} username={Username} " +
                    $"character={characterId} item_uid={grant.ItemUid} npc=88 enhancement=5");
                Console.Out.Flush();

                using var client = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended = await authorization.RunAuthenticatedAesSocketAsync(
                    client, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000))
                    .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);

                var inventory = await store.ListInventoryItemsAsync(characterId)
                    .ConfigureAwait(false);
                var storage = await store.ListStorageItemsAsync(characterId)
                    .ConfigureAwait(false);
                if (inventory.Count != 1 ||
                    inventory[0].ItemUid != grant.ItemUid ||
                    inventory[0].ItemDefinitionId != 2701 ||
                    inventory[0].Quantity != 2 ||
                    inventory[0].SourceDropId != drops.Single().DropId ||
                    inventory[0].EnhancementLevel != 5 ||
                    storage.Count != 0 ||
                    ended.CommandsAccepted != 4 ||
                    router.Statistics.ActiveSessions != 0)
                    throw new InvalidOperationException(
                        $"storage E2E mismatch inventory={inventory.Count} storage={storage.Count} " +
                        $"commands={ended.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"STORAGE_FIXTURE_PASS aes=true item_uid={grant.ItemUid} " +
                    $"quantity=2 source_drop={drops.Single().DropId} enhancement=5 storage=0 " +
                    $"commands={ended.CommandsAccepted}");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("STORAGE_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunCheckpointCrashProducerAsync(string databasePath)
    {
        try
        {
            string fullDb = Path.GetFullPath(databasePath);
            string root = Path.GetDirectoryName(fullDb)
                ?? throw new ArgumentException("checkpoint DB directory missing");
            Directory.CreateDirectory(root);
            var map = BlockingGrid.Load(CreateMap(root, 15), 15);
            await using var world = new ServerWorldHost([map]);
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(fullDb);
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(
                "crash_checkpoint", "Crash-Checkpoint-Password-123!")).AccountId;
            long characterId = (await store.CreateCharacterAsync(accountId, 0,
                "CrashCheckpointHero", new CharacterSpawn(15, 8, 8))).Character?.CharacterId
                ?? throw new InvalidOperationException("crash checkpoint character missing");

            var positions = new CharacterPositionCheckpoint(store,
                new(IntervalMilliseconds: 2000, MaxTrackedCharacters: 8));
            var router = new WorldResultRouter(changedObserver: positions.Observe);
            using var stop = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results, stop.Token);
            var checkpointing = positions.RunAsync(stop.Token);

            var actor = await world.TrustedRegisterAsync(characterId, 15, 8, 8)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("crash checkpoint register failed");
            if (!world.TryPostMove(new MoveTargetCommand(
                    actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch, 1, 40, 8)))
                throw new InvalidOperationException("crash checkpoint first move refused");

            var durableDeadline = DateTime.UtcNow.AddSeconds(7);
            while (true)
            {
                var rows = await store.ListCharactersAsync(accountId).ConfigureAwait(false);
                if (rows.Count == 1 && Math.Abs(rows[0].X - 40) < 0.001 &&
                    Math.Abs(rows[0].Y - 8) < 0.001)
                    break;
                if (DateTime.UtcNow >= durableDeadline)
                    throw new TimeoutException("periodic position 40,8 was not committed");
                await Task.Delay(25).ConfigureAwait(false);
            }

            long observedBefore = positions.Statistics.ObservedSnapshots;
            if (!world.TryPostMove(new MoveTargetCommand(
                    actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch, 2, 24, 8)))
                throw new InvalidOperationException("crash checkpoint volatile move refused");
            var volatileDeadline = DateTime.UtcNow.AddSeconds(1);
            while (positions.Statistics.ObservedSnapshots < observedBefore + 2)
            {
                if (DateTime.UtcNow >= volatileDeadline)
                    throw new TimeoutException("volatile movement was not observed");
                await Task.Delay(5).ConfigureAwait(false);
            }

            // The next periodic flush is still ~2s away. The parent process
            // SIGKILLs immediately after this marker, so 24,8 is intentionally
            // volatile while 40,8 is already committed by the real background pump.
            Console.WriteLine(
                $"CHECKPOINT_CRASH_READY db={fullDb} durable=40,8 volatile=24,8 " +
                $"observed={positions.Statistics.ObservedSnapshots} periodic_rows={positions.Statistics.PeriodicRowsWritten}");
            Console.Out.Flush();
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 3; // unreachable unless the parent failed to hard-kill us
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CHECKPOINT_CRASH_PRODUCER_FAIL " + error);
            return 1;
        }
    }

    private static async Task<int> RunCheckpointCrashVerifyAsync(string databasePath)
    {
        try
        {
            using var store = new SqliteIdentityStore(Path.GetFullPath(databasePath));
            await store.InitializeAsync().ConfigureAwait(false);
            var auth = await store.AuthenticateAsync(
                "crash_checkpoint", "Crash-Checkpoint-Password-123!").ConfigureAwait(false);
            if (auth.Status != AuthenticationStatus.Success || auth.Account is null ||
                auth.Account.Characters.Count != 1)
                throw new InvalidOperationException("checkpoint DB did not reopen/authenticate");
            var character = auth.Account.Characters[0];
            if (character.MapId != 15 || Math.Abs(character.X - 40) > 0.001 ||
                Math.Abs(character.Y - 8) > 0.001)
                throw new InvalidOperationException(
                    $"hard-kill recovery expected last committed 40,8, got {character.X},{character.Y}");
            Console.WriteLine(
                $"CHECKPOINT_CRASH_VERIFY_PASS map={character.MapId} x={character.X:F1} y={character.Y:F1}");
            Console.Out.Flush();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("CHECKPOINT_CRASH_VERIFY_FAIL " + error);
            return 1;
        }
    }

    private static async Task<int> RunReconnectAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_reconnect_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var map = BlockingGrid.Load(CreateMap(root, 15), 15);
            await using var world = new ServerWorldHost([map]);
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId = (await store.CreateAccountAsync(Username, Password)).AccountId;
            var created = await store.CreateCharacterAsync(accountId, 0,
                "ReconnectHero", new CharacterSpawn(15, 8, 8));
            long characterId = created.Character?.CharacterId ??
                throw new InvalidOperationException("reconnect character missing");

            var router = new WorldResultRouter();
            var authorization = new IdentitySessionAuthorization(store, world);
            var aoiPump = new WorldAoiPump(router);
            var routerTask = router.RunAsync(world.Results);
            using var aoiStop = new CancellationTokenSource();
            var aoiTask = aoiPump.RunAsync(aoiStop.Token);

            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(2);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine($"RECONNECT_FIXTURE_READY port={port} username={Username} character={characterId}");
                Console.Out.Flush();

                var first = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var firstTask = authorization.RunAuthenticatedAesSocketAsync(
                    first, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));

                string? command = await Console.In.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                if (command != "DROP")
                    throw new InvalidOperationException(
                        $"Expected DROP after first authoritative move, got {command ?? "<EOF>"}");

                first.Dispose(); // intentional network failure, not graceful Client logout
                var firstEnded = await firstTask.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                var releaseDeadline = DateTime.UtcNow.AddSeconds(4);
                while (world.Statistics.ActiveActors != 0 ||
                       router.Statistics.ActiveSessions != 0)
                {
                    if (DateTime.UtcNow >= releaseDeadline)
                        throw new InvalidOperationException("first reconnect generation did not release");
                    await Task.Delay(5).ConfigureAwait(false);
                }
                var persisted = await store.ListCharactersAsync(accountId).ConfigureAwait(false);
                if (persisted.Count != 1 || persisted[0].MapId != 15 ||
                    Math.Abs(persisted[0].X - 24) > 0.001 ||
                    Math.Abs(persisted[0].Y - 8) > 0.001)
                    throw new InvalidOperationException(
                        $"first disconnect position not persisted x={persisted[0].X} y={persisted[0].Y}");
                Console.WriteLine(
                    $"RECONNECT_FIXTURE_DROPPED first_reason={firstEnded.Reason} persisted_x={persisted[0].X:F1} persisted_y={persisted[0].Y:F1}");
                Console.Out.Flush();

                using var second = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                var secondTask = authorization.RunAuthenticatedAesSocketAsync(
                    second, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));
                var secondEnded = await secondTask.WaitAsync(TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);

                releaseDeadline = DateTime.UtcNow.AddSeconds(4);
                while (world.Statistics.ActiveActors != 0 ||
                       router.Statistics.ActiveSessions != 0)
                {
                    if (DateTime.UtcNow >= releaseDeadline)
                        throw new InvalidOperationException("second reconnect generation did not release");
                    await Task.Delay(5).ConfigureAwait(false);
                }

                var final = await store.ListCharactersAsync(accountId).ConfigureAwait(false);
                if (final.Count != 1 || final[0].MapId != 15 ||
                    Math.Abs(final[0].X - 40) > 0.001 ||
                    Math.Abs(final[0].Y - 8) > 0.001 ||
                    secondEnded.CommandsAccepted != 3 ||
                    router.Statistics.ReceiptsQueued < 2)
                    throw new InvalidOperationException(
                        $"reconnect completion mismatch second={secondEnded.Reason} moves={secondEnded.CommandsAccepted} receipts={router.Statistics.ReceiptsQueued} x={final[0].X} y={final[0].Y}");

                await world.StopAsync().ConfigureAwait(false);
                await routerTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Console.WriteLine(
                    $"RECONNECT_FIXTURE_PASS first_reason={firstEnded.Reason} second_reason={secondEnded.Reason} persisted_first=24,8 persisted_final=40,8 second_commands={secondEnded.CommandsAccepted} receipts={router.Statistics.ReceiptsQueued} active=0");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                aoiStop.Cancel();
                await aoiTask.ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("RECONNECT_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<int> RunMultiplayerAsync(bool crossmap, bool portals = false, bool reconnect = false)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "sgtest_multi_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var map = BlockingGrid.Load(CreateMap(root, 15), 15);
            var maps = crossmap || portals
                ? new[] { map, BlockingGrid.Load(CreateMap(root, 13), 13) }
                : new[] { map };
            await using var world = new ServerWorldHost(maps);
            await world.StartAsync().ConfigureAwait(false);
            using var store = new SqliteIdentityStore(Path.Combine(root, "identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long aAccount = (await store.CreateAccountAsync(
                "fixture_one", Password)).AccountId;
            long bAccount = (await store.CreateAccountAsync(
                "fixture_two", Password)).AccountId;
            var aRecord = await store.CreateCharacterAsync(aAccount, 0,
                "MultiHeroOne", new CharacterSpawn(15, 8, 8));
            var bRecord = await store.CreateCharacterAsync(bAccount, 0,
                "MultiHeroTwo", new CharacterSpawn(15, 24, 8));
            long aId = aRecord.Character?.CharacterId ??
                throw new InvalidOperationException("multi fixture first character missing");
            long bId = bRecord.Character?.CharacterId ??
                throw new InvalidOperationException("multi fixture second character missing");
            if (!crossmap && !portals && !reconnect)
            {
                var worldChatProfile = new CharacterCombatProfileData(
                    CombatProfileTrust.SyntheticTest,
                    "ci:world-chat-multi-fixture",
                    Race: 0, Career: 0, Strength: 5, Dexterity: 5,
                    Constitution: 5, Wisdom: 5, Level: 10,
                    CurrentHp: 100, MaxHp: 100,
                    Attack: 1, Defense: 1, Hit: 1, Dodge: 1,
                    AttackRange: 48, AttackCooldownTicks: 15);
                await store.SetCharacterCombatProfileAsync(aId, worldChatProfile)
                    .ConfigureAwait(false);
                await store.SetCharacterCombatProfileAsync(bId, worldChatProfile)
                    .ConfigureAwait(false);
                var createdClan=await store.CreateClanAsync(aId,"星門先遣隊")
                    .ConfigureAwait(false);
                if(createdClan.Status!=ClanStoreStatus.Success||createdClan.Clan is null)
                    throw new InvalidOperationException("multi fixture clan create failed");
                var joinedClan=await store.JoinClanAsync(bId,createdClan.Clan.ClanId)
                    .ConfigureAwait(false);
                if(joinedClan.Status!=ClanStoreStatus.Success)
                    throw new InvalidOperationException("multi fixture clan join failed");
            }
            var router = new WorldResultRouter();
            var auditedPortals = portals ? PortalCatalog.LoadNpcDocuments([
                CreatePortalDocument(root, 15), CreatePortalDocument(root, 13)]) : null;
            var parties=new PartyCoordinator();
            var clans=!crossmap&&!portals&&!reconnect
                ?new ClanCoordinator(store):null;
            var friends=!crossmap&&!portals&&!reconnect
                ?new FriendCoordinator(store):null;
            var authorization = new IdentitySessionAuthorization(
                store, world, auditedPortals, parties:parties,clans:clans,friends:friends);
            var aoiPump = new WorldAoiPump(router);
            var routerTask = router.RunAsync(world.Results);
            using var aoiStop = new CancellationTokenSource();
            var aoiTask = aoiPump.RunAsync(aoiStop.Token);
            var settings = AesCbcSettings.FromEnvironment();

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(reconnect ? 3 : 2);
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var prefix = reconnect ? "MULTI_RECONNECT" : portals ? "PORTAL" : crossmap ? "CROSSMAP" : "MULTI";
                Console.WriteLine($"{prefix}_FIXTURE_READY port={port} actor_a={aId} actor_b={bId}");
                Console.Out.Flush();

                using var first = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var firstTask = authorization.RunAuthenticatedAesSocketAsync(
                    first, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));
                using var second = await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var secondTask = authorization.RunAuthenticatedAesSocketAsync(
                    second, settings, router,
                    new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));
                if (reconnect)
                {
                    var firstActorSession = await secondTask
                        .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    if (firstActorSession.Reason != AesWireEndReason.CleanEof ||
                        firstActorSession.CommandsAccepted != 3)
                        throw new InvalidOperationException(
                            $"multi reconnect first actor session mismatch reason={firstActorSession.Reason} commands={firstActorSession.CommandsAccepted}");

                    using var third = await listener.AcceptTcpClientAsync()
                        .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                    var thirdTask = authorization.RunAuthenticatedAesSocketAsync(
                        third, settings, router,
                        new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds: 15000));
                    var reconnectResults = await Task.WhenAll(firstTask, thirdTask)
                        .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    var reconnectDeadline = DateTime.UtcNow.AddSeconds(3);
                    while (world.Statistics.ActiveActors != 0)
                    {
                        if (DateTime.UtcNow >= reconnectDeadline)
                            throw new InvalidOperationException("multi reconnect fixture left ghost World actors");
                        await Task.Delay(5).ConfigureAwait(false);
                    }
                    var finalCharacters = await store.ListCharactersAsync(bAccount)
                        .ConfigureAwait(false);
                    if (reconnectResults[0].Reason != AesWireEndReason.CleanEof ||
                        reconnectResults[1].Reason != AesWireEndReason.CleanEof ||
                        reconnectResults[1].CommandsAccepted != 2 ||
                        finalCharacters.Count != 1 || finalCharacters[0].MapId != 15 ||
                        Math.Abs(finalCharacters[0].X - 24) > 0.001 ||
                        Math.Abs(finalCharacters[0].Y - 8) > 0.001 ||
                        router.Statistics.ActiveSessions != 0 ||
                        router.Statistics.ReceiptsQueued < 2 ||
                        aoiPump.FramesQueued < 10)
                        throw new InvalidOperationException(
                            $"multi reconnect incomplete observer={reconnectResults[0].Reason} first={firstActorSession.Reason} rejoin={reconnectResults[1].Reason} " +
                            $"first_commands={firstActorSession.CommandsAccepted} rejoin_commands={reconnectResults[1].CommandsAccepted} " +
                            $"routes={router.Statistics.ActiveSessions} receipts={router.Statistics.ReceiptsQueued} aoi_frames={aoiPump.FramesQueued} " +
                            $"final={(finalCharacters.Count == 1 ? $"{finalCharacters[0].MapId}:{finalCharacters[0].X},{finalCharacters[0].Y}" : "missing")}");
                    await world.StopAsync().ConfigureAwait(false);
                    await routerTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    Console.WriteLine(
                        $"MULTI_RECONNECT_FIXTURE_PASS actor_a={aId} actor_b={bId} first_commands={firstActorSession.CommandsAccepted} rejoin_commands={reconnectResults[1].CommandsAccepted} persisted_final=24,8 receipts={router.Statistics.ReceiptsQueued} aoi_frames={aoiPump.FramesQueued} active=0");
                    Console.Out.Flush();
                    return 0;
                }
                if (crossmap)
                {
                    // Test-only coordination: Python waits for real Godot's
                    // observer to see authoritative Delta BEFORE triggering
                    // the trusted server transition. No teleport RPC exists.
                    async Task TransferOnSignalAsync(string command, int mapId)
                    {
                        string? received = await Console.In.ReadLineAsync()
                            .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                        if (received != command)
                            throw new InvalidOperationException(
                                $"Expected test-only command {command}, got {received ?? "<EOF>"}");
                        var moved = await router.TrustedTransferAsync(
                            world, bId, mapId, 24, 8).ConfigureAwait(false)
                            ?? throw new InvalidOperationException(
                                $"Authoritative transfer to map {mapId} denied");
                        Console.WriteLine(
                            $"CROSSMAP_FIXTURE_TRANSFER map={moved.MapId} epoch={moved.MapEpoch} connection={moved.ConnectionEpoch}");
                        Console.Out.Flush();
                    }

                    await TransferOnSignalAsync("TO13", 13).ConfigureAwait(false);
                    await TransferOnSignalAsync("TO15", 15).ConfigureAwait(false);
                }
                var results = await Task.WhenAll(firstTask, secondTask)
                    .WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (world.Statistics.ActiveActors != 0)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw new InvalidOperationException("multi fixture left ghost World actors");
                    await Task.Delay(5).ConfigureAwait(false);
                }
                if (results[0].Reason != AesWireEndReason.CleanEof ||
                    results[1].Reason != AesWireEndReason.CleanEof ||
                    results[1].CommandsAccepted != (portals ? 9 : crossmap ? 13 : 7) ||
                    parties.PartyCount != 0 ||
                    router.Statistics.ActiveSessions != 0 ||
                    router.Statistics.ReceiptsQueued < (crossmap || portals ? 2 : 1) ||
                    aoiPump.FramesQueued < (crossmap || portals ? 10 : 6))
                    throw new InvalidOperationException(
                        $"multiplayer TLS/AOI incomplete a={results[0].Reason} b={results[1].Reason} " +
                        $"moves={results[1].CommandsAccepted} parties={parties.PartyCount} routes={router.Statistics.ActiveSessions} " +
                        $"receipts={router.Statistics.ReceiptsQueued} aoi_frames={aoiPump.FramesQueued}");
                await world.StopAsync().ConfigureAwait(false);
                await routerTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                Console.WriteLine($"{prefix}_FIXTURE_PASS actor_a={aId} actor_b={bId} moves={results[1].CommandsAccepted} party_count={parties.PartyCount} aoi_frames={aoiPump.FramesQueued} active=0");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                aoiStop.Cancel();
                await aoiTask.ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine((portals ? "PORTAL" : crossmap ? "CROSSMAP" : "MULTI") + "_FIXTURE_FAIL " + error);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Test-only stand-in for converter-produced npc/v1. Uses audited metadata
    /// shape and ORIGINAL Map IDs / Spawn IDs, but synthetic NPC positions and
    /// 3x3 test Blocking. Private original files never enter this repository.
    /// </summary>
    private static string CreatePortalDocument(string root, int sourceMap)
    {
        int target = sourceMap == 15 ? 13 : 15;
        int spawnId = sourceMap == 15 ? 46 : 1;
        string file = Path.Combine(root, "npcs_" + sourceMap + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            schema = "sgtest.npcs/v1",
            coordinate_space = "world_pixels",
            map_id = sourceMap,
            npcs = new[]
            {
                new
                {
                    spawn_id = spawnId,
                    p = new[] { 24, 8 },
                    interaction = new
                    {
                        mode = "confirm_transfer",
                        adapter = "audited_TS_Common_v1",
                        dependencies_complete = true,
                        destination_status = "walkable",
                        choice = 0,
                        destination = new { map_id = target, p = new[] { 24, 8 } }
                    }
                }
            }
        }));
        return file;
    }

    internal static string CreateMap(
        string root, int mapId, int width = 3, int height = 3)
    {
        string directory = Path.Combine(root, "map_" + mapId);
        Directory.CreateDirectory(directory);
        const int cell = 16;
        if (width is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(height));
        var raw = new byte[8 + width * height];
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4, 4), height);
        File.WriteAllBytes(Path.Combine(directory, "blocking.dat"), raw);
        string sha = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(directory, "world.json"), JsonSerializer.Serialize(new
        {
            version = 3,
            map_id = mapId,
            collision_cell = cell,
            size = new[] { width * cell, height * cell },
            collision_sha256 = sha
        }));
        return directory;
    }

}
