using SGTest.Server.Visibility;
using SGTest.Server.Trade;
using SGTest.Server.Skill;
using SGTest.Server.PlayerShop;
using SGTest.Server.Party;
using SGTest.Server.Pet;
using SGTest.Server.Mecha;
using SGTest.Server.Clan;
using SGTest.Server.Friend;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using SGTest.Server.Transport;
using System.Text.Json;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class AesGameHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = Parse(args);
            string root = Path.GetFullPath(Required(options, "--content"));
            string database = Path.GetFullPath(Required(options, "--identity"));
            var aesSettings = AesCbcSettings.FromEnvironment();
            if (!IPAddress.TryParse(options.GetValueOrDefault("--bind", "127.0.0.1"), out var address))
                throw new ArgumentException("--bind must be an IP address");
            int port = Number(options, "--port", 7777, 0, 65535);
            int maximum = Number(options, "--max-connections", 128, 1, 4096);
            int perAddress = Number(options, "--max-per-address", Math.Min(8, maximum), 1, maximum);
            var authDefaults=new AuthenticationRateLimitOptions();
            int authAddressFailures=Number(
                options,"--auth-failures-per-address",
                authDefaults.MaxFailuresPerAddress,1,10000);
            int authAccountFailures=Number(
                options,"--auth-failures-per-account",
                authDefaults.MaxFailuresPerAccount,1,10000);
            int authWindowSeconds=Number(
                options,"--auth-failure-window-seconds",
                checked((int)authDefaults.EffectiveFailureWindow.TotalSeconds),
                1,86400);
            int authLockoutSeconds=Number(
                options,"--auth-lockout-seconds",
                checked((int)authDefaults.EffectiveLockout.TotalSeconds),
                1,86400);
            string registrationMode=options.GetValueOrDefault(
                "--account-registration","disabled");
            bool registrationEnabled=registrationMode switch
            {
                "enabled"=>true,
                "disabled"=>false,
                _=>throw new ArgumentException(
                    "--account-registration must be enabled or disabled")
            };
            var registrationDefaults=new AccountRegistrationRateLimitOptions();
            int registrationAttempts=Number(
                options,"--registration-attempts-per-address",
                registrationDefaults.MaxAttemptsPerAddress,1,100);
            int registrationWindowSeconds=Number(
                options,"--registration-window-seconds",
                checked((int)registrationDefaults.EffectiveWindow.TotalSeconds),
                1,86400);
            int checkpointMs = Number(options, "--position-checkpoint-ms", 5000, 500, 60000);
            if (options.ContainsKey("--seconds"))
                stop.CancelAfter(TimeSpan.FromSeconds(Number(options, "--seconds", 1, 1, 86400)));
            var maps = new[] { 13, 15 }.Select(id =>
                BlockingGrid.Load(Path.Combine(root, "maps", id.ToString()), id)).ToArray();

            string[] characterSpawnKeys =
                ["--character-spawn-map", "--character-spawn-x", "--character-spawn-y"];
            bool anyCharacterSpawn =
                characterSpawnKeys.Any(options.ContainsKey);
            if (anyCharacterSpawn &&
                characterSpawnKeys.Any(key => !options.ContainsKey(key)))
                throw new ArgumentException(
                    "Character creation spawn requires map, x and y together");
            CharacterCreationPolicy? characterCreation = null;
            if (options.ContainsKey("--character-appearance-rules") && !anyCharacterSpawn)
                throw new ArgumentException("Appearance rules require explicit character spawn configuration");
            if (anyCharacterSpawn)
            {
                int mapId = Number(options, "--character-spawn-map", 0, 1, 65535);
                double x = Real(options, "--character-spawn-x");
                double y = Real(options, "--character-spawn-y");
                var map = maps.SingleOrDefault(candidate => candidate.MapId == mapId)
                    ?? throw new ArgumentException(
                        "--character-spawn-map must be one of the served maps");
                if (!map.TryWorldCell(x, y, out var cell) || !map.IsWalkable(cell))
                    throw new ArgumentException(
                        "Character creation spawn must be inside a walkable Blocking cell");
                var appearanceRules=options.TryGetValue("--character-appearance-rules",out var appearancePath)
                    ? CharacterAppearanceRules.LoadVerified(Path.GetFullPath(appearancePath)) : null;
                characterCreation = new(
                    new CharacterSpawn(mapId, x, y),
                    appearanceRules,
                    CharacterInitialProfileRules.OldParadiseLevelOne);
            }

            var monsterRuntimes = new List<MonsterRuntime>();
            foreach (var map in maps)
            {
                string populationPath = Path.Combine(
                    root, "maps", map.MapId.ToString(), "population.json");
                if (!File.Exists(populationPath))
                    continue;
                MonsterPopulation? population;
                try
                {
                    population = MonsterPopulationCatalog.LoadForWorld(
                        populationPath, map.MapId, map);
                }
                catch (InvalidDataException error)
                {
                    throw new ArgumentException(
                        $"Map {map.MapId} monster population is not verified gameplay content: {error.Message}",
                        error);
                }
                if (population is not null && population.Spawns.Count > 0)
                    monsterRuntimes.Add(new MonsterRuntime(map, population));
            }
            string? skillManifest=options.GetValueOrDefault("--skill-rules");
            IReadOnlyDictionary<int,VerifiedSkillRule>? skillRules=
                skillManifest is null ? null :
                SkillRuleCatalog.LoadVerified(Path.GetFullPath(skillManifest));
            string? petCombatManifest=options.GetValueOrDefault("--pet-combat-rules");
            IReadOnlyDictionary<PetCombatRuleKey,VerifiedPetCombatRule>? petCombatRules=
                petCombatManifest is null ? null :
                PetCombatRuleCatalog.LoadVerified(Path.GetFullPath(petCombatManifest));
            string? petSkillManifest=options.GetValueOrDefault("--pet-skill-rules");
            IReadOnlyDictionary<PetSkillRuleKey,VerifiedPetSkillRule>? petSkillRules=
                petSkillManifest is null ? null :
                PetSkillRuleCatalog.LoadVerified(Path.GetFullPath(petSkillManifest));
            string? petGrowthManifest=options.GetValueOrDefault("--pet-growth-rules");
            PetGrowthCurve? petGrowth=petGrowthManifest is null ? null :
                PetGrowthRuleCatalog.LoadVerified(Path.GetFullPath(petGrowthManifest));
            string? petEquipmentManifest=
                options.GetValueOrDefault("--pet-equipment-rules");
            IReadOnlyDictionary<int,PetEquipmentRule>? petEquipmentRules=
                petEquipmentManifest is null ? null :
                PetEquipmentCatalog.LoadVerified(Path.GetFullPath(petEquipmentManifest));
            MonsterWorldCoordinator? monsters = monsterRuntimes.Count == 0
                ? null : new MonsterWorldCoordinator(
                    monsterRuntimes,skillRules,petCombatRules,petSkillRules);
            // No original source-verified manifest means no fabricated drops.
            string? lootManifest = options.GetValueOrDefault("--loot-rules");
            IReadOnlyList<MonsterDropRule>? dropRules = lootManifest is null
                ? null : MonsterDropCatalog.LoadVerified(
                    Path.GetFullPath(lootManifest),monsterRuntimes);
            string? experienceManifest=
                options.GetValueOrDefault("--experience-rules");
            IReadOnlyList<CharacterExperienceRewardRule>? experienceRules=
                experienceManifest is null ? null :
                CharacterExperienceRewardCatalog.LoadVerified(
                    Path.GetFullPath(experienceManifest),monsterRuntimes);
            // Explicit audited content only. If omitted, no HP recovery item
            // can be used; client-side names or .tres never grant authority.
            string? progressionManifest=
                options.GetValueOrDefault("--character-progression-rules");
            CharacterProgressionRules? progressionRules=progressionManifest is null
                ?null:CharacterProgressionRules.LoadVerified(
                    Path.GetFullPath(progressionManifest));
            string? recoveryManifest=options.GetValueOrDefault("--recovery-rules");
            IReadOnlyDictionary<int,RecoveryItemRule>? recoveryRules=
                recoveryManifest is null ? null :
                RecoveryItemCatalog.LoadVerified(Path.GetFullPath(recoveryManifest));
            var npcDocuments = new[] { 13, 15 }.Select(id =>
                Path.Combine(root, "maps", id.ToString(), "npcs.json")).ToArray();
            var portals = PortalCatalog.LoadNpcDocuments(npcDocuments);
            string? shopManifest=options.GetValueOrDefault("--shop-rules");
            NpcShopCatalog? shops=shopManifest is null ? null :
                NpcShopCatalog.LoadVerified(
                    Path.GetFullPath(shopManifest),npcDocuments);
            string? storageManifest=options.GetValueOrDefault("--storage-rules");
            NpcStorageCatalog? storages=storageManifest is null ? null :
                NpcStorageCatalog.LoadVerified(
                    Path.GetFullPath(storageManifest),npcDocuments);
            string? equipmentManifest=options.GetValueOrDefault("--equipment-rules");
            IReadOnlyDictionary<int,EquipmentRule>? equipmentRules=
                equipmentManifest is null ? null :
                EquipmentItemCatalog.LoadVerified(Path.GetFullPath(equipmentManifest));
            string? mechaEquipmentManifest=
                options.GetValueOrDefault("--mecha-equipment-rules");
            IReadOnlyDictionary<int,MechaEquipmentRule>? mechaEquipmentRules=
                mechaEquipmentManifest is null ? null :
                MechaEquipmentCatalog.LoadVerified(
                    Path.GetFullPath(mechaEquipmentManifest));
            string? mechaBatteryManifest=
                options.GetValueOrDefault("--mecha-battery-rules");
            MechaBatteryCatalog? mechaBatteryRules=
                mechaBatteryManifest is null ? null :
                MechaBatteryCatalog.LoadVerified(Path.GetFullPath(mechaBatteryManifest));
            string? bankManifest=options.GetValueOrDefault("--bank-rules");
            NpcBankCatalog? banks=bankManifest is null ? null :
                NpcBankCatalog.LoadVerified(
                    Path.GetFullPath(bankManifest),npcDocuments);
            string? repairManifest=options.GetValueOrDefault("--repair-rules");
            string? repairItemManifest=options.GetValueOrDefault("--repair-item-rules");
            if((repairManifest is null)!=(repairItemManifest is null))
                throw new ArgumentException(
                    "--repair-rules and --repair-item-rules must be supplied together");
            NpcRepairCatalog? repairs=repairManifest is null ? null :
                NpcRepairCatalog.LoadVerified(
                    Path.GetFullPath(repairManifest),npcDocuments);
            IReadOnlyDictionary<int,RepairItemRule>? repairItemRules=
                repairItemManifest is null ? null :
                RepairItemCatalog.LoadVerified(Path.GetFullPath(repairItemManifest));
            string? enhancementNpcManifest=
                options.GetValueOrDefault("--enhancement-npc-rules");
            string? enhancementItemManifest=
                options.GetValueOrDefault("--enhancement-item-rules");
            if((enhancementNpcManifest is null)!=(enhancementItemManifest is null))
                throw new ArgumentException(
                    "--enhancement-npc-rules and --enhancement-item-rules "+
                    "must be supplied together");
            NpcEnhancementCatalog? enhancers=enhancementNpcManifest is null
                ? null:NpcEnhancementCatalog.LoadVerified(
                    Path.GetFullPath(enhancementNpcManifest),npcDocuments);
            IReadOnlyDictionary<int,EquipmentEnhancementItemRule>?
                enhancementItemRules=enhancementItemManifest is null
                    ? null:EquipmentEnhancementCatalog.LoadVerified(
                        Path.GetFullPath(enhancementItemManifest));
            string? questManifest=options.GetValueOrDefault("--quest-rules");
            NpcQuestCatalog? quests=questManifest is null ? null :
                NpcQuestCatalog.LoadVerified(
                    Path.GetFullPath(questManifest),npcDocuments);
            var gmAccounts=ParseGmAccounts(
                options.GetValueOrDefault("--gm-accounts"));
            string? tradeItemManifest=options.GetValueOrDefault("--trade-item-rules");
            IReadOnlyDictionary<int,TradeItemRule>? tradeItemRules=
                tradeItemManifest is null ? null :
                TradeItemCatalog.LoadVerified(Path.GetFullPath(tradeItemManifest));
            using var identity = new SqliteIdentityStore(database);
            await identity.InitializeAsync(stop.Token);
            // Reserve even when no drop rule table is enabled, preventing
            // pick-up replay-key collisions after a process restart.
            long lootGeneration = await identity.AllocateLootWorldGenerationAsync(
                stop.Token);
            MonsterLootSettlement? loot = dropRules is null
                ? null : new MonsterLootSettlement(identity,lootGeneration,dropRules);
            // One startup-only SQLite snapshot. Map transfers, reconnect and
            // WorldReady resync rebuild from the SAME map-bucketed AOI state.
            var activeDrops = new List<GroundDropRecord>();
            foreach (var map in maps)
            {
                var entries = await identity.ListActiveGroundDropsAsync(
                    map.MapId,10000,stop.Token);
                // Saturated snapshot can hide records; reject instead of
                // silently forgetting items. Production never publishes
                // manually injected synthetic test merchandise.
                if (entries.Count >= 10000 ||
                    entries.Any(x=>x.Trust!=LootSourceTrust.Verified))
                    throw new InvalidDataException(
                        "Ground drop startup snapshot saturated or synthetic test loot found");
                activeDrops.AddRange(entries);
            }
            await using var world = new ServerWorldHost(
                maps,
                options: monsters is null
                    ? null
                    : new WorldHostOptions(PublishIdleTicks: true));
            var positions = new CharacterPositionCheckpoint(identity,
                new(IntervalMilliseconds: checkpointMs));
            var router = new WorldResultRouter(changedObserver: positions.Observe);
            var combat = new PlayerCombatSettlement(
                identity,
                delivery: router.QueuePlayerDamage,
                committedLifecycle: async result =>
                {
                    if(result.SourceKind is PlayerDamageSourceKind.PlayerPvp or
                        PlayerDamageSourceKind.PlayerSkill)
                    {
                        var now=DateTimeOffset.UtcNow;
                        CharacterPvpState pvp=result.Died
                            ? await identity.MarkPvpWhiteKillAsync(
                                result.AttackerActorId,result.VictimWasNormalWhite,now)
                                .ConfigureAwait(false)
                            : await identity.MarkPvpHostileAttackAsync(
                                result.AttackerActorId,result.VictimWasNormalWhite,now)
                                .ConfigureAwait(false);
                        var published=router.PublishPlayerPvpState(
                            result.AttackerActorId,result.AttackerConnectionEpoch,pvp);
                        if(published==GatewaySendStatus.Backpressured)
                            throw new PlayerCombatSettlementException(
                                "Committed PvP crime identity update queue full");
                    }
                    if (result.Died)
                    {
                        var stopped = await world.TrustedSetCombatDeadAsync(
                            result.ActorId, result.ConnectionEpoch, result.MapEpoch, true)
                            .ConfigureAwait(false);
                        if (stopped is null)
                            throw new PlayerCombatSettlementException(
                                "Committed lethal damage could not stop authoritative World actor");
                    }
                    if(result.SourceKind==PlayerDamageSourceKind.Monster&&
                       !router.TryPostPlayerDamageVisual(result))
                        throw new PlayerCombatSettlementException(
                            "Committed player damage visual queue full");
                    if(result.SourceKind==PlayerDamageSourceKind.PlayerPvp&&
                       !router.TryPostPlayerPvpCombatVisual(result))
                        throw new PlayerCombatSettlementException(
                            "Committed PvP combat visual queue full");
                    if(result.SourceKind==PlayerDamageSourceKind.PlayerSkill&&
                       !router.TryPostPlayerPvpSkillVisual(result))
                        throw new PlayerCombatSettlementException(
                            "Committed PvP skill visual queue full");
                });
            PlayerTradeCoordinator? trades=tradeItemRules is null
                ? null:new PlayerTradeCoordinator(identity,world,tradeItemRules,combat);
            PlayerShopCoordinator? playerShops=trades is null
                ? null:new PlayerShopCoordinator(
                    identity,world,tradeItemRules!,trades,combat);
            var parties=new PartyCoordinator();
            var clans=new ClanCoordinator(identity);
            var friends=new FriendCoordinator(identity);
            var pets=new PetCoordinator(identity,petGrowth,petEquipmentRules);
            var mechas=new MechaCoordinator(
                identity,mechaEquipmentRules,mechaBatteryRules);
            CharacterExperienceSettlement? experience=experienceRules is null
                ? null:new CharacterExperienceSettlement(
                    identity,router,lootGeneration,experienceRules,
                    petGrowth is null ? null : pets,progressionRules);
            var authRateLimiter=new AuthenticationRateLimiter(new(
                MaxFailuresPerAddress:authAddressFailures,
                MaxFailuresPerAccount:authAccountFailures,
                FailureWindow:TimeSpan.FromSeconds(authWindowSeconds),
                Lockout:TimeSpan.FromSeconds(authLockoutSeconds)));
            AccountRegistrationRateLimiter? registrationRateLimiter=
                registrationEnabled
                    ?new AccountRegistrationRateLimiter(new(
                        MaxAttemptsPerAddress:registrationAttempts,
                        Window:TimeSpan.FromSeconds(registrationWindowSeconds)))
                    :null;
            var aoi = new WorldAoiPump(
                router, monsters: monsters, playerCombat: combat, loot: loot,
                experience: experience,parties: parties,
                initialGroundDrops: activeDrops);
            var authorization = new IdentitySessionAuthorization(
                identity, world, portals, positions, combat,
                pickupWorldGeneration: lootGeneration,
                recoveryRules: recoveryRules,
                characterCreation: characterCreation,
                shops: shops,
                storages: storages,
                equipmentRules: equipmentRules,
                banks: banks,
                repairs: repairs,
                repairItemRules: repairItemRules,
                enhancers: enhancers,
                enhancementItemRules: enhancementItemRules,
                quests: quests,
                trades: trades,
                playerShops: playerShops,
                parties: parties,
                clans: clans,
                friends: friends,
                pets: pets,
                mechas: mechas,
                progressionRules: progressionRules,
                rateLimiter: authRateLimiter,
                registrationRateLimiter: registrationRateLimiter,
                gmAccounts: gmAccounts);
            await using var listener = new AesGatewayListener(address, port, authorization,
                aesSettings, router, new(MaxConnections: maximum, MaxConnectionsPerAddress: perAddress));
            await world.StartAsync();
            using var pumps = new CancellationTokenSource();
            var routing = router.RunAsync(world.Results, pumps.Token);
            var visibility = aoi.RunAsync(pumps.Token);
            var checkpointing = positions.RunAsync(pumps.Token);
            var combatSettlement = combat.RunAsync(pumps.Token);
            // Unlike other continuously-polled pumps, drain queued loot on
            // controlled shutdown before releasing the shared SQLite store.
            var lootSettlement = loot?.RunAsync() ??
                Task.Delay(Timeout.Infinite,pumps.Token);
            var experienceSettlement=experience?.RunAsync() ??
                Task.Delay(Timeout.Infinite,pumps.Token);
            try
            {
                var accepting = listener.RunAsync(stop.Token);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    result = "AES_HOST_READY", endpoint = listener.LocalEndpoint.ToString(),
                    max_connections = maximum, max_per_address = perAddress,
                    auth_failures_per_address = authAddressFailures,
                    auth_failures_per_account = authAccountFailures,
                    auth_failure_window_seconds = authWindowSeconds,
                    auth_lockout_seconds = authLockoutSeconds,
                    account_registration_enabled = registrationEnabled,
                    registration_attempts_per_address = registrationAttempts,
                    registration_window_seconds = registrationWindowSeconds,
                    maps = maps.Select(m => m.MapId), audited_portals = portals.Count,
                    monster_maps = monsterRuntimes.Select(m => m.MapId),
                    monsters = monsterRuntimes.Sum(m => m.Count),
                    position_checkpoint_ms = checkpointMs,
                    loot_rule_count = dropRules?.Count ?? 0,
                    character_experience_settlement_enabled = experience is not null,
                    verified_experience_rule_count = experienceRules?.Count ?? 0,
                    pet_experience_settlement_enabled =
                        experience is not null&&petGrowth is not null,
                    verified_recovery_rule_count = recoveryRules?.Count ?? 0,
                    verified_skill_rule_count = skillRules?.Count ?? 0,
                    verified_shop_count = shops?.Count ?? 0,
                    verified_storage_count = storages?.Count ?? 0,
                    verified_equipment_rule_count = equipmentRules?.Count ?? 0,
                    verified_mecha_equipment_rule_count =
                        mechaEquipmentRules?.Count ?? 0,
                    verified_mecha_battery_rule_count =
                        mechaBatteryRules?.Count ?? 0,
                    verified_bank_count = banks?.Count ?? 0,
                    verified_repair_count = repairs?.Count ?? 0,
                    verified_repair_item_rule_count = repairItemRules?.Count ?? 0,
                    verified_enhancement_npc_count = enhancers?.Count ?? 0,
                    verified_enhancement_item_rule_count =
                        enhancementItemRules?.Count ?? 0,
                    verified_quest_count = quests?.Count ?? 0,
                    verified_trade_item_rule_count = tradeItemRules?.Count ?? 0,
                    player_shop_enabled = playerShops is not null,
                    online_party_enabled = true,
                    gm_account_count = gmAccounts.Count,
                    unpicked_ground_drops = activeDrops.Count,
                    character_creation_enabled = characterCreation is not null,
                    character_creation_map = characterCreation?.Spawn.MapId,
                    character_appearance_choices = characterCreation?.AppearanceRules?.Choices.Count ?? 0
                }));
                var stopping = Task.Delay(Timeout.Infinite, stop.Token);
                var completed = await Task.WhenAny(
                    accepting, routing, visibility, checkpointing, combatSettlement,
                    lootSettlement,experienceSettlement,stopping);
                if (completed != stopping && !stop.IsCancellationRequested)
                {
                    await completed; // propagate faults; never serve after a result/AOI pump failed
                    throw new InvalidOperationException("A live server pipeline stopped unexpectedly");
                }
            }
            finally
            {
                stop.Cancel();
                try
                {
                    // World and pumps remain live until authenticated sockets have
                    // completed their trusted Disconnect/Router detach cleanup.
                    await listener.DisposeAsync();
                }
                finally
                {
                    await world.StopAsync();
                    pumps.Cancel();
                    try { await Task.WhenAll(
                        routing, visibility, checkpointing, combatSettlement); }
                    catch (OperationCanceledException) when (pumps.IsCancellationRequested) { }
                    finally
                    {
                        loot?.Complete();
                        experience?.Complete();
                        if(loot is not null)await lootSettlement;
                        if(experience is not null)await experienceSettlement;
                    }
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                result = "AES_HOST_STOPPED", listener = listener.Statistics,
                authentication_rate_limit = authRateLimiter.Statistics,
                account_registration_rate_limit = registrationRateLimiter?.Statistics,
                world = world.Statistics, router = router.Statistics,
                monster_coordinator = monsters?.Statistics,
                positions = positions.Statistics,
                combat = combat.Statistics,
                loot = loot?.Statistics,
                character_experience = experience?.Statistics,
                character_experience_settlement_enabled = experience is not null,
                verified_experience_rule_count = experienceRules?.Count ?? 0,
                pet_experience_settlement_enabled =
                    experience is not null&&petGrowth is not null,
                verified_recovery_rule_count = recoveryRules?.Count ?? 0,
                verified_skill_rule_count = skillRules?.Count ?? 0,
                pet_growth_enabled = petGrowth is not null,
                verified_shop_count = shops?.Count ?? 0,
                verified_storage_count = storages?.Count ?? 0,
                verified_equipment_rule_count = equipmentRules?.Count ?? 0,
                verified_mecha_equipment_rule_count =
                    mechaEquipmentRules?.Count ?? 0,
                verified_mecha_battery_rule_count =
                    mechaBatteryRules?.Count ?? 0,
                verified_bank_count = banks?.Count ?? 0,
                verified_repair_count = repairs?.Count ?? 0,
                verified_repair_item_rule_count = repairItemRules?.Count ?? 0,
                verified_quest_count = quests?.Count ?? 0,
                verified_trade_item_rule_count = tradeItemRules?.Count ?? 0,
                player_shop_enabled = playerShops is not null
            }));
            return 0;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine("AES_HOST_CONFIGURATION: " + error.Message);
            Console.Error.WriteLine("Usage: serve --content <PRIVATE_ROOT> --identity <DB> " +
                "[--bind 127.0.0.1] [--port 7777] [--max-connections 128] [--max-per-address 8] " +
                "[--auth-failures-per-address 12] [--auth-failures-per-account 6] " +
                "[--auth-failure-window-seconds 60] [--auth-lockout-seconds 30] " +
                "[--account-registration disabled|enabled] " +
                "[--registration-attempts-per-address 3] [--registration-window-seconds 600] " +
                "[--position-checkpoint-ms 5000] [--loot-rules <VERIFIED_JSON>] " +
                "[--experience-rules <VERIFIED_JSON>] " +
                "[--recovery-rules <VERIFIED_JSON>] [--skill-rules <VERIFIED_JSON>] " +
                "[--pet-combat-rules <VERIFIED_JSON>] [--pet-skill-rules <VERIFIED_JSON>] [--pet-growth-rules <VERIFIED_JSON>] " +
                "[--pet-equipment-rules <VERIFIED_JSON>] " +
                "[--shop-rules <VERIFIED_JSON>] [--storage-rules <VERIFIED_JSON>] " +
                "[--equipment-rules <VERIFIED_JSON>] [--mecha-equipment-rules <VERIFIED_JSON>] [--mecha-battery-rules <VERIFIED_JSON>] " +
                "[--bank-rules <VERIFIED_JSON>] " +
                "[--repair-rules <VERIFIED_JSON> --repair-item-rules <VERIFIED_JSON>] " +
                "[--quest-rules <VERIFIED_JSON>] [--trade-item-rules <VERIFIED_JSON>] " +
                "[--gm-accounts <user1,user2>] " +
                "[--character-spawn-map <13|15> --character-spawn-x <X> --character-spawn-y <Y>] " +
                "[--character-appearance-rules <VERIFIED_JSON>] [--character-progression-rules <VERIFIED_JSON>] " +
                "[--seconds N]");
            return 2;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                     CryptographicException or System.Net.Sockets.SocketException or
                                     WorldResultRoutingException or AoiBackpressureException or
                                     CharacterPositionCheckpointException or
                                     PlayerCombatSettlementException or MonsterLootSettlementException or
                                     CharacterExperienceSettlementException or
                                     PetExperienceDeathSettlementException or InvalidDataException)
        {
            Console.Error.WriteLine("AES_HOST_FAIL: " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        string[] known = ["--content", "--identity", "--bind", "--port",
                          "--max-connections", "--max-per-address",
                          "--auth-failures-per-address", "--auth-failures-per-account",
                          "--auth-failure-window-seconds", "--auth-lockout-seconds",
                          "--account-registration",
                          "--registration-attempts-per-address",
                          "--registration-window-seconds",
                          "--position-checkpoint-ms",
                          "--loot-rules", "--experience-rules",
                          "--recovery-rules", "--skill-rules",
                          "--pet-combat-rules", "--pet-skill-rules", "--pet-growth-rules",
                          "--pet-equipment-rules",
                          "--shop-rules", "--storage-rules", "--equipment-rules",
                          "--mecha-equipment-rules", "--mecha-battery-rules", "--bank-rules",
                          "--repair-rules", "--repair-item-rules", "--quest-rules",
                          "--trade-item-rules", "--gm-accounts",
                          "--character-spawn-map", "--character-spawn-x",
                          "--character-spawn-y", "--character-appearance-rules", "--character-progression-rules", "--seconds"];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length % 2 != 0) throw new ArgumentException("Every option requires a value");
        for (int i = 0; i < args.Length; i += 2)
            if (!known.Contains(args[i]) || string.IsNullOrWhiteSpace(args[i + 1]) ||
                !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Unknown, duplicate or empty option: " + args[i]);
        return values;
    }

    private static HashSet<string> ParseGmAccounts(string? raw)
    {
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(raw is null)return result;
        foreach(string part in raw.Split(',',StringSplitOptions.TrimEntries))
        {
            if(part.Length is <3 or >32||
               part.Any(c=>!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
                   >= '0' and <= '9' or '_'))||
               !result.Add(part))
                throw new ArgumentException(
                    "--gm-accounts must contain unique valid account usernames");
        }
        if(result.Count>32)
            throw new ArgumentException("--gm-accounts supports at most 32 accounts");
        return result;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing " + name);

    private static int Number(Dictionary<string, string> options, string name, int fallback, int min, int max)
    {
        if (!options.TryGetValue(name, out var value)) return fallback;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) &&
               number >= min && number <= max
            ? number : throw new ArgumentException("Invalid " + name);
    }

    private static double Real(Dictionary<string, string> options, string name)
    {
        string value = Required(options, name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double number) && double.IsFinite(number)
            ? number : throw new ArgumentException("Invalid " + name);
    }
}
